using KScript.Language.Analysis;
using KScript.Syntax.Markup;

namespace KScript.Language.Tests
{
    /// <summary>The .kscript format must build the same tree as the XML it stands for, with exact positions.</summary>
    public class CodeParserTests
    {
        private static MarkupDocument Code(string text)
        {
            MarkupDocument doc = CodeParser.Parse(text);
            Assert.True(doc.Diagnostics.Count == 0, string.Join("\n", doc.Diagnostics));
            return doc;
        }

        private static MarkupElement Root(string text) => Assert.Single(Code(text).RootElements);

        /// <summary>A compact picture of a tree: elements, attributes and non-blank text, ignoring layout.</summary>
        private static string Shape(MarkupElement e) =>
            e.Name
            + (e.Attributes.Count > 0 ? "(" + string.Join(", ", e.Attributes.Select(a => a.Name + ": " + a.ValueText?.Trim())) + ")" : string.Empty)
            + string.Concat(e.Children.OfType<MarkupText>().Where(t => !t.IsWhitespace).Select(t => " \"" + t.Value.Trim() + "\""))
            + (e.ChildElements.Any() ? " { " + string.Join("; ", e.ChildElements.Select(Shape)) + " }" : string.Empty);

        private static string RepoFile(params string[] parts)
        {
            string dir = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(dir, "KScript.sln")))
            {
                dir = Path.GetDirectoryName(dir);
            }
            return Path.Combine(new[] { dir }.Concat(parts).ToArray());
        }

        [Theory]
        [InlineData("let n = 3", "kscript { let(name: n, value: 3) }")]
        [InlineData("const config = { debug: false }", "kscript { let(name: config, value: { debug: false }, const: yes) }")]
        [InlineData("let count: int = '4'", "kscript { let(name: count, type: int, value: '4') }")]
        [InlineData("set total += $price * 2", "kscript { set(name: total, op: +=, value: $price * 2) }")]
        [InlineData("$x += 1; @greet('Hi')", "kscript { do \"$x += 1\"; do \"@greet('Hi')\" }")]
        [InlineData("echo \"Hello, $name!\"", "kscript { echo \"Hello, $name!\" }")]
        [InlineData("echo $list[1]", "kscript { echo(value: $list[1]) }")]
        [InlineData("echo(format: json) $data", "kscript { echo(format: json, value: $data) }")]
        [InlineData("echo \"a\" + $b", "kscript { echo(value: \"a\" + $b) }")]
        [InlineData("import KScript.Math as math", "kscript { import(package: KScript.Math, as: math) }")]
        [InlineData("import \"lib/x.kscript\" as x", "kscript { import(src: lib/x.kscript, as: x) }")]
        [InlineData("import a, b from \"lib/x.kscript\"", "kscript { import(names: a, b, src: lib/x.kscript) }")]
        [InlineData("for key, value at i in $map { echo $key }", "kscript { for(each: key, value, index: i, in: $map) { echo(value: $key) } }")]
        [InlineData("while true { break }", "kscript { while(test: true) { break } }")]
        [InlineData("fn double(x: int, y = 2) => $x * $y", "kscript { fn(name: double, params: x: int, y = 2, value: $x * $y) }")]
        [InlineData("fn hi() { return }", "kscript { fn(name: hi) { return } }")]
        [InlineData("throw Validation \"Bad {$x}\"", "kscript { throw(type: Validation) \"Bad {$x}\" }")]
        [InlineData("exit 2", "kscript { exit(code: 2) }")]
        [InlineData("input(to: name) \"Name: \"", "kscript { input(to: name) \"Name:\" }")]
        [InlineData("move(from: $a, to: \"x, y\")", "kscript { move(from: $a, to: x, y) }")]
        [InlineData("Row(gap: 8, wrap) { Text \"Hi\" }", "kscript { Row(gap: 8, wrap: yes) { Text \"Hi\" } }")]
        [InlineData("Button(on-click: \"n = $n + 1\") \"Add\"", "kscript { Button(on-click: n = $n + 1) \"Add\" }")]
        public void Statements(string code, string shape)
        {
            Assert.Equal(shape, Shape(Root(code)));
        }

        [Fact]
        public void IfChainsBecomeSiblings()
        {
            Assert.Equal("kscript { if(test: $a) { echo \"a\" }; elif(test: $b) { echo \"b\" }; elif(test: $c) { echo \"c\" }; else { echo \"d\" } }",
                Shape(Root("if $a {\n  echo \"a\"\n} elif $b {\n  echo \"b\"\n}\nelse if $c { echo \"c\" } else {\n  echo \"d\"\n}")));
        }

        [Fact]
        public void CatchAndFinallyGoInsideTry()
        {
            Assert.Equal("kscript { try { do \"@risky()\"; catch(as: err, when: $err.type == 'X') { echo \"x\" }; catch { echo \"other\" }; finally { echo \"done\" } } }",
                Shape(Root("try {\n  @risky()\n} catch err when $err.type == 'X' {\n  echo \"x\"\n} catch {\n  echo \"other\"\n} finally {\n  echo \"done\"\n}")));
        }

        [Fact]
        public void MatchCases()
        {
            Assert.Equal("kscript { match(value: $c) { case(is: 1, 2) { echo \"low\" }; case(when: $c > 9) { echo \"high\" }; default { echo \"?\" } } }",
                Shape(Root("match $c {\n  case 1, 2 { echo \"low\" }\n  when $c > 9 { echo \"high\" }\n  default { echo \"?\" }\n}")));
        }

        [Fact]
        public void ExpressionsContinueOverLines()
        {
            MarkupElement root = Root("let names = $people\n  |> filter(fn($p) => $p.age >= 18)\n  |> map(fn($p) => $p.name)\nlet m = {\n  a: 1,\n  b: 2\n}\nlet t = $a and\n  $b");
            List<MarkupElement> lets = root.ChildElements.ToList();
            Assert.Equal(3, lets.Count);
            Assert.EndsWith("|> map(fn($p) => $p.name)", lets[0].GetAttributeValue("value"));
            Assert.Equal("{\n  a: 1,\n  b: 2\n}", lets[1].GetAttributeValue("value"));
            Assert.Equal("$a and\n  $b", lets[2].GetAttributeValue("value"));
        }

        [Fact]
        public void HeaderBraceOpensTheBlockButBracketsInsideDoNot()
        {
            MarkupElement root = Root("if $m->has({ a: 1 }->keys()[0]) { echo \"yes\" }");
            Assert.Equal("$m->has({ a: 1 }->keys()[0])", root.ChildElements.Single().GetAttributeValue("test"));
        }

        [Fact]
        public void CommentsAndDocComments()
        {
            MarkupElement root = Root("// a comment\n/* block\ncomment */\n/// Doubles a number.\n/// Twice as big.\nfn double(x) => $x * 2 // trailing\nlet s = 'a // not a comment'");
            MarkupElement fn = root.ChildElements.First();
            Assert.Equal("Doubles a number. Twice as big.", fn.GetAttributeValue("description"));
            Assert.Equal("$x * 2", fn.GetAttributeValue("value"));
            Assert.Equal("'a // not a comment'", root.ChildElements.Last().GetAttributeValue("value"));
        }

        [Fact]
        public void AttributeAndTextPositionsPointIntoTheFile()
        {
            const string text = "let total = $price * 2\necho \"Hi \\\"{$name}\\\"\"";
            MarkupElement root = Root(text);
            MarkupAttribute value = root.ChildElements.First().GetAttribute("value");
            Assert.Equal("$price * 2", text.Substring(value.Value.SourceSpan.Start, value.Value.SourceSpan.Length));

            MarkupText echo = root.ChildElements.Last().SingleText;
            Assert.Equal("Hi \"{$name}\"", echo.Value);
            int name = echo.Value.IndexOf("$name", StringComparison.Ordinal);
            Assert.Equal("$name", text.Substring(echo.Content.ToSource(name), 5));
        }

        [Fact]
        public void APackageElementAloneIsTheRoot()
        {
            MarkupElement root = Root("// a window\nKScript.Window(title: \"Hi\") {\n  Text \"Hello\"\n}");
            Assert.Equal("KScript.Window(title: Hi) { Text \"Hello\" }", Shape(root));
            Assert.Null(root.Parent);
        }

        [Theory]
        [InlineData("if $a { echo \"a\"", "KS1003")]
        [InlineData("echo \"unclosed", "KS1001")]
        [InlineData("}", "KS1001")]
        [InlineData("else { }", "KS3007")]
        [InlineData("break continue", "KS1001")]
        [InlineData("for x { }", "KS3005")]
        [InlineData("set x", "KS3005")]
        public void ReportsMistakes(string code, string diagnostic)
        {
            MarkupDocument doc = CodeParser.Parse(code);
            Assert.Contains(doc.Diagnostics, d => d.Code == diagnostic);
        }

        [Fact]
        public void ParserIsChosenByExtension()
        {
            Assert.True(MarkupParser.IsCodePath("a/b/code.kscript"));
            Assert.False(MarkupParser.IsCodePath("a/b/code.ks.xml"));
            Assert.Equal(SourceSyntax.Code, MarkupParser.Parse(new Syntax.Text.SourceText("let x = 1"), DocumentKind.Script, "x.kscript").Syntax);
            Assert.Equal(SourceSyntax.Xml, MarkupParser.Parse(new Syntax.Text.SourceText("<kscript/>"), DocumentKind.Script, "x.ks.xml").Syntax);
        }

        /// <summary>Each .kscript sample is a rewrite of an XML one; both must give the same elements and attributes.</summary>
        [Theory]
        [InlineData("samples/kscript2/oldest.ks.xml", "samples/code-format/oldest.kscript")]
        [InlineData("samples/kscript2/move-files.ks.xml", "samples/code-format/move-files.kscript")]
        [InlineData("samples/kscript2/lib/strings.ks.xml", "samples/code-format/lib/strings.kscript")]
        [InlineData("samples/life-lab/desktop.ks.xml", "samples/life-lab/desktop.kscript")]
        public void SameTreeAsTheXmlVersion(string xmlFile, string codeFile)
        {
            MarkupDocument xml = MarkupParser.Parse(File.ReadAllText(RepoFile(xmlFile.Split('/'))), DocumentKind.Script);
            MarkupDocument code = Code(File.ReadAllText(RepoFile(codeFile.Split('/'))));
            Assert.Equal(Outline(xml.RootElements.Single()), Outline(code.RootElements.Single()));
        }

        /// <summary>Element names and attribute names in order (values differ in spelling, such as lt and &lt;).</summary>
        private static List<string> Outline(MarkupElement root) =>
            new[] { root }.Concat(root.Ancestors()).Take(1).Concat(Descendants(root))
                .Select(e => e.Name + "(" + string.Join(",", e.Attributes.Select(a => a.Name)
                    .Concat(e.Name is "let" or "set" && e.SingleText is MarkupText t && !t.IsWhitespace ? new[] { "value" } : Array.Empty<string>())
                    .Where(n => n != "description" && n != "src").OrderBy(n => n)) + ")")
                .ToList();

        private static IEnumerable<MarkupElement> Descendants(MarkupElement e) =>
            e.ChildElements.SelectMany(c => new[] { c }.Concat(Descendants(c)));

        [Fact]
        public void AnalyzerChecksCodeFiles()
        {
            DocumentAnalysis ok = Analyzer.Analyze("let x = 2\necho \"{ $x * 2 }\"", "test.kscript");
            Assert.Empty(ok.Diagnostics);

            const string text = "let x = 2\necho \"{ $y }\"";
            DocumentAnalysis bad = Analyzer.Analyze(text, "test.kscript");
            Diagnostic d = Assert.Single(bad.Diagnostics);
            Assert.Equal("KS4001", d.Code);
            Assert.Equal(text.IndexOf("$y", StringComparison.Ordinal) + 1, d.Span.Start);
        }
    }
}
