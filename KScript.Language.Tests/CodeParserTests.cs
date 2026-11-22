using KScript.Language.Features;
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
        [InlineData("n = 3", "kscript { do \"n = 3\" }")]
        [InlineData("const config = { debug: false }", "kscript { let(name: config, value: { debug: false }, const: yes) }")]
        [InlineData("count: int = '4'", "kscript { let(name: count, type: int, value: '4') }")]
        [InlineData("point.x = 3", "kscript { do \"point.x = 3\" }")]
        [InlineData("list[0] += 1", "kscript { do \"list[0] += 1\" }")]
        [InlineData("total += $price * 2", "kscript { do \"total += $price * 2\" }")]
        [InlineData("$x += 1; @greet('Hi')", "kscript { do \"$x += 1\"; do \"@greet('Hi')\" }")]
        [InlineData("echo \"Hello, $name!\"", "kscript { echo \"Hello, $name!\" }")]
        [InlineData("echo $list[1]", "kscript { echo(value: $list[1]) }")]
        [InlineData("echo(format: json) $data", "kscript { echo(format: json, value: $data) }")]
        [InlineData("echo \"a\" + $b", "kscript { echo(value: \"a\" + $b) }")]
        [InlineData("import KScript.Math as math", "kscript { import(package: KScript.Math, as: math) }")]
        [InlineData("import \"lib/x.kscript\" as x", "kscript { import(path: lib/x.kscript, as: x) }")]
        [InlineData("import a, b from \"lib/x.kscript\"", "kscript { import(names: a, b, path: lib/x.kscript) }")]
        [InlineData("for key, value at i in $map { echo $key }", "kscript { for(each: key, value, at: i, in: $map) { echo(value: $key) } }")]
        [InlineData("while true { break }", "kscript { while(test: true) { break } }")]
        [InlineData("fn double(x: int, y = 2) => $x * $y", "kscript { fn(name: double, params: x: int, y = 2, value: $x * $y) }")]
        [InlineData("fn hi() { return }", "kscript { fn(name: hi) { return } }")]
        [InlineData("throw Validation \"Bad {$x}\"", "kscript { throw(type: Validation) \"Bad {$x}\" }")]
        [InlineData("exit 2", "kscript { exit(code: 2) }")]
        [InlineData("input(into: name) \"Name: \"", "kscript { input(into: name) \"Name:\" }")]
        [InlineData("move(from: $a, to: \"x, y\")", "kscript { move(from: $a, to: x, y) }")]
        [InlineData("Row(gap: 8, wrap) { Text \"Hi\" }", "kscript { Row(gap: 8, wrap: yes) { Text \"Hi\" } }")]
        [InlineData("Button(on_click: \"n = $n + 1\") \"Add\"", "kscript { Button(on_click: n = $n + 1) \"Add\" }")]
        [InlineData("type User { login: string; id: int }", "kscript { type(name: User) { field(name: login, type: string); field(name: id, type: int) } }")]
        [InlineData("type User(fields: \"login: string\", extra: error)", "kscript { type(name: User, fields: login: string, extra: error) }")]
        [InlineData("type Repo {\n  stars: int from stargazers_count\n  owner from \"owner.login\"\n  tags?: string[] = []\n  label => $name + '!'\n}",
            "kscript { type(name: Repo) { field(name: stars, type: int, from: stargazers_count); field(name: owner, from: owner.login); field(name: tags?, type: string[], default: []); field(name: label, value: $name + '!') } }")]
        [InlineData("type(name: T, fields: \"a\")", "kscript { type(name: T, fields: a) }")]
        [InlineData("type = 3", "kscript { do \"type = 3\" }")]
        [InlineData("type\t T { a }", "kscript { type(name: T) { field(name: a) } }")]
        [InlineData("type", "kscript { type }")]
        [InlineData("type  ", "kscript { type }")]
        [InlineData("type \"x\"", "kscript { type \"x\" }")]
        [InlineData("type T { a from x; b from y}", "kscript { type(name: T) { field(name: a, from: x); field(name: b, from: y) } }")]
        [InlineData("type T { a from x= 1 }", "kscript { type(name: T) { field(name: a, from: x, default: 1) } }")]
        [InlineData("enum E {}", "kscript { enum(name: E, values: ) }")]
        [InlineData("enum Size { small, medium,\n  large }", "kscript { enum(name: Size, values: small, medium,\n  large) }")]
        [InlineData("enum(name: Size, values: \"a, b\")", "kscript { enum(name: Size, values: a, b) }")]
        public void Statements(string code, string shape)
        {
            Assert.Equal(shape, Shape(Root(code)));
        }

        [Fact]
        public void IfChainsBecomeSiblings()
        {
            Assert.Equal("kscript { if(test: $a) { echo \"a\" }; elif(test: $b) { echo \"b\" }; elif(test: $c) { echo \"c\" }; else { echo \"d\" } }",
                Shape(Root("if $a {\n  echo \"a\"\n} elif $b {\n  echo \"b\"\n}\nelif $c { echo \"c\" } else {\n  echo \"d\"\n}")));
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
                Shape(Root("match $c {\n  1, 2 => echo \"low\"\n  _ when $c > 9 => { echo \"high\" }\n  _ => echo \"?\"\n}")));
        }

        [Fact]
        public void ExpressionsContinueOverLines()
        {
            MarkupElement root = Root("names = $people\n  |> filter(fn(p) => $p.age >= 18)\n  |> map(fn(p) => $p.name)\nm = {\n  a: 1,\n  b: 2\n}\nt = $a and\n  $b");
            List<MarkupElement> lets = root.ChildElements.ToList();
            Assert.All(lets, l => Assert.Equal("do", l.Name));
            Assert.Equal(3, lets.Count);
            Assert.EndsWith("|> map(fn(p) => $p.name)", lets[0].SingleText.Value);
            Assert.Equal("m = {\n  a: 1,\n  b: 2\n}", lets[1].SingleText.Value);
            Assert.Equal("t = $a and\n  $b", lets[2].SingleText.Value);
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
            MarkupElement root = Root("// a comment\n/* block\ncomment */\n/// Doubles a number.\n/// Twice as big.\nfn double(x) => $x * 2 // trailing\nconst s = 'a // not a comment'");
            MarkupElement fn = root.ChildElements.First();
            Assert.Equal("Doubles a number.\nTwice as big.", fn.GetAttributeValue("description"));
            Assert.Equal("$x * 2", fn.GetAttributeValue("value"));
            Assert.Equal("'a // not a comment'", root.ChildElements.Last().GetAttributeValue("value"));
        }

        [Fact]
        public void AttributeAndTextPositionsPointIntoTheFile()
        {
            const string text = "total = $price * 2\necho \"Hi \\\"{$name}\\\"\"";
            MarkupElement root = Root(text);
            MarkupText value = root.ChildElements.First().SingleText;
            Assert.Equal("total = $price * 2", text.Substring(value.Content.SourceSpan.Start, value.Content.SourceSpan.Length));

            MarkupText echo = root.ChildElements.Last().SingleText;
            Assert.Equal("Hi \"{$name}\"", echo.Value);
            int name = echo.Value.IndexOf("$name", StringComparison.Ordinal);
            Assert.Equal("$name", text.Substring(echo.Content.ToSource(name), 5));
        }

        [Fact]
        public void APackageElementIsAStatementAfterItsImport()
        {
            MarkupElement root = Root("// a window\nimport KScript.UI\n\nWindow(title: \"Hi\") {\n  Text \"Hello\"\n}");
            Assert.Equal("kscript", root.Name);
            Assert.Equal(new[] { "import", "Window" }, root.ChildElements.Select(c => c.Name));
            Assert.Equal("Hi", root.ChildElements.Last().GetAttributeValue("title"));
        }

        /// <summary>A package is named only in its import: KScript.Window { } is an error whose fix drops the prefix.</summary>
        [Fact]
        public void AStatementWithThePackagePrefixIsAnError()
        {
            string text = "import KScript.UI\n\nKScript.Window(title: \"Hi\") {\n  Text \"Hello\"\n}\n";
            MarkupDocument doc = CodeParser.Parse(text);
            Diagnostic d = Assert.Single(doc.Diagnostics);
            Assert.Equal("KS1001", d.Code);
            Assert.Equal(DiagnosticSeverity.Error, d.Severity);
            Assert.Contains("Write Window", d.Message);
            Assert.Equal("KScript.Window", text.Substring(d.Span.Start, d.Span.Length));
            TextChange change = Assert.Single(Assert.Single(d.Fixes).Changes);
            Assert.Equal(text.Replace("KScript.Window", "Window"), text.Remove(change.Span.Start, change.Span.Length).Insert(change.Span.Start, change.NewText));
            // The element is still read, under its own name, so the rest of the file is checked as usual.
            Assert.Equal(new[] { "import", "Window" }, doc.RootElements.Single().ChildElements.Select(c => c.Name));
        }

        [Theory]
        [InlineData("if $a { echo \"a\"", "KS1003")]
        [InlineData("echo \"unclosed", "KS1001")]
        [InlineData("}", "KS1001")]
        [InlineData("else { }", "KS3007")]
        [InlineData("break continue", "KS1001")]
        [InlineData("for x { }", "KS3005")]
        [InlineData("let x = 1", "KS3002")]
        [InlineData("type T { 3 }", "KS1001")]
        [InlineData("type T {\n  3\n  a\n}", "KS1001")]
        [InlineData("type T { 3; a }", "KS1001")]
        [InlineData("type T { 3", "KS1003")]
        [InlineData("type T { a from x", "KS1003")]
        [InlineData("type T { a: }", "KS3005")]
        [InlineData("type T { a from }", "KS3005")]
        [InlineData("type T { a = }", "KS3005")]
        [InlineData("type T { a: int b }", "KS1001")]
        [InlineData("type T { a: int", "KS1003")]
        [InlineData("enum E a, b", "KS1001")]
        [InlineData("enum E { a, b", "KS1003")]
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
            Assert.Equal(SourceSyntax.Code, MarkupParser.Parse(new Syntax.Text.SourceText("x = 1"), DocumentKind.Script, "x.kscript").Syntax);
            Assert.Equal(SourceSyntax.Xml, MarkupParser.Parse(new Syntax.Text.SourceText("<kscript/>"), DocumentKind.Script, "x.ks.xml").Syntax);
        }

        /// <summary>Each .kscript sample is a rewrite of an XML one; both must give the same elements and attributes.</summary>
        [Theory]
        [InlineData("samples/xml/oldest.ks.xml", "samples/scripts/oldest.kscript")]
        [InlineData("samples/xml/move-files.ks.xml", "samples/scripts/move-files.kscript")]
        [InlineData("samples/xml/lib/strings.ks.xml", "samples/scripts/lib/strings.kscript")]
        [InlineData("samples/xml/types/repo.ks.xml", "samples/scripts/types/repo.kscript")]
        [InlineData("samples/xml/types/owner.ks.xml", "samples/scripts/types/owner.kscript")]
        [InlineData("samples/xml/types/visibility.ks.xml", "samples/scripts/types/visibility.kscript")]
        [InlineData("samples/xml/errors/order-not-found.ks.xml", "samples/scripts/errors/order-not-found.kscript")]
        [InlineData("samples/xml/errors/out-of-stock.ks.xml", "samples/scripts/errors/out-of-stock.kscript")]
        [InlineData("samples/xml/repos.ks.xml", "samples/scripts/repos.kscript")]
        public void SameTreeAsTheXmlVersion(string xmlFile, string codeFile)
        {
            MarkupDocument xml = MarkupParser.Parse(File.ReadAllText(RepoFile(xmlFile.Split('/'))), DocumentKind.Script);
            MarkupDocument code = Code(File.ReadAllText(RepoFile(codeFile.Split('/'))));
            Assert.Equal(SamePackageForm(Outline(xml.RootElements.Single())), SamePackageForm(Outline(code.RootElements.Single())));
        }

        /// <summary>
        /// An XML file names its package in the root tag (&lt;KScript.Window&gt;, imports inside); a .kscript file imports it
        /// and writes Window { }. Both are the same script, so the root, the imports and the prefix are left out.
        /// </summary>
        private static List<string> SamePackageForm(List<string> outline) =>
            outline.Where(o => o != "kscript()" && !o.StartsWith("import(", StringComparison.Ordinal))
                .Select(o => o.StartsWith("KScript.", StringComparison.Ordinal) ? o.Substring("KScript.".Length) : o).ToList();

        /// <summary>Element names and attribute names (values differ in spelling, such as lt and &lt;).</summary>
        private static List<string> Outline(MarkupElement root) =>
            new[] { root }.Concat(root.Ancestors()).Take(1).Concat(Descendants(root))
                // let, set and do are all "x = value" in .kscript, so they compare as one kind of statement.
                .Select(e => e.Name is "let" or "set" or "do" && !IsYes(e.GetAttributeValue("const")) && e.GetAttribute("type") == null
                    ? "assign"
                    : e.Name + "(" + string.Join(",", e.Attributes.Select(a => a.Name).Where(n => n != "description" && n != "path").OrderBy(n => n)) + ")")
                .ToList();

        private static bool IsYes(string v) => v == "yes";

        private static IEnumerable<MarkupElement> Descendants(MarkupElement e) =>
            e.ChildElements.SelectMany(c => new[] { c }.Concat(Descendants(c)));

        [Fact]
        public void OneAssignmentDeclaresThenChanges()
        {
            AssertNoDiagnostics("total = 0\nfor i in 0..3 {\n  total += $i\n  last = $i\n  echo $last\n}\necho $total");

            DocumentAnalysis a = Analyzer.Analyze("for i in 0..3 {\n  tmp = $i\n}\necho $tmp\ncount += 1\nconst k = 1\nk = 2", "test.kscript");
            Assert.Contains(a.Diagnostics, d => d.Code == "KS4001" && d.Message.Contains("$tmp"));
            Assert.Contains(a.Diagnostics, d => d.Code == "KS4002" && d.Message.Contains("count = ..."));
            Assert.Contains(a.Diagnostics, d => d.Code == "KS4003");
        }

        private static void AssertNoDiagnostics(string code)
        {
            DocumentAnalysis analysis = Analyzer.Analyze(code, "test.kscript");
            Assert.True(analysis.Diagnostics.Count == 0, string.Join("\n", analysis.Diagnostics));
        }

        [Fact]
        public void KeepsCommentsAndBlankLines()
        {
            MarkupElement root = Root("// one\nx = 1\n\n/* two */\ny = 2 // three");
            Assert.Equal(new[] { "one", "two", "three" }, root.Children.OfType<MarkupComment>().Select(c => c.Text));
            Assert.Contains(root.Children.OfType<MarkupText>(), t => t.IsWhitespace && t.Value.Count(c => c == '\n') >= 2);
        }

        [Fact]
        public void BlockCommentsAtALinesEndDontEndTheStatement()
        {
            // After an operator, the statement goes on past the comment to the next line.
            Assert.Equal("kscript { do \"x = 1 + /* tax */\n  2\" }", Shape(Root("x = 1 + /* tax */\n  2")));
            Assert.Equal("kscript { do \"x = /* soon */ /* really */\n  1\" }", Shape(Root("x = /* soon */ /* really */\n  1")));
            // Otherwise the comment is its own, after the statement.
            MarkupElement root = Root("x = 1 /* one */\ny = 2");
            Assert.Equal("kscript { do \"x = 1\"; do \"y = 2\" }", Shape(root));
            Assert.Equal("one", Assert.Single(root.Children.OfType<MarkupComment>()).Text.Trim());

            Assert.Contains(CodeParser.Parse("x = 1 /* never closed").Diagnostics, d => d.Message.Contains("missing its closing */"));
            // A stray */ with no /* is just text: the line ends in an operator-like character and goes on...
            Assert.Single(CodeParser.Parse("x = 2 */\n  3").RootElements.Single().ChildElements);
            // ...unless the next line can only be a new statement, which leaves this one unfinished.
            Assert.Equal(2, CodeParser.Parse("x = 2 */\ny = 3").RootElements.Single().ChildElements.Count());
        }

        [Fact]
        public void BlockCommentsSurviveWritingCodeBackOut()
        {
            string code = ScriptConverter.ToCode(CodeParser.Parse("/* one line */\nx = 1\n/* first\n\nlast */\ny = 2\n")).Text;
            Assert.Equal("/* one line */\nx = 1\n/*\n  first\n\n  last\n*/\ny = 2\n", code);
        }

        [Fact]
        public void AnalyzerChecksCodeFiles()
        {
            DocumentAnalysis ok = Analyzer.Analyze("x = 2\necho \"{ $x * 2 }\"", "test.kscript");
            Assert.Empty(ok.Diagnostics);

            const string text = "x = 2\necho \"{ $x + $y }\"";
            DocumentAnalysis bad = Analyzer.Analyze(text, "test.kscript");
            Diagnostic d = Assert.Single(bad.Diagnostics);
            Assert.Equal("KS4001", d.Code);
            Assert.Equal(text.IndexOf("$y", StringComparison.Ordinal) + 1, d.Span.Start);
        }
    
        [Fact]
        public void TypesDocumentTheirFields()
        {
            MarkupElement type = Root("/// A repository.\ntype Repo {\n  /// How many stars.\n  stars: int\n  // a note\n\n  name\n}").ChildElements.Single();
            Assert.Equal("A repository.", type.GetAttributeValue("description"));
            Assert.Equal("How many stars.", type.ChildElements.First().GetAttributeValue("description"));
            Assert.Equal("name", type.ChildElements.Last().GetAttributeValue("name"));
            Assert.Contains(type.Children, c => c is MarkupComment { Text: "a note" });

            MarkupElement e = Root("/// Sizes.\nenum Size { s, m }").ChildElements.Single();
            Assert.Equal("Sizes.", e.GetAttributeValue("description"));
        }

        [Fact]
        public void TypesInCodeFilesGetTheSameChecksAndCompletion()
        {
            const string types = "enum Size { s, m }\ntype Repo {\n  name: string\n  stars: int from stargazers_count\n  size: Size = 's'\n}\n";
            AssertNoDiagnostics(types + "r = Repo({ name: 'k', stargazers_count: 1 })\necho \"$r.name { $r.stars }\"");

            DocumentAnalysis bad = Analyzer.Analyze(types + "r = Repo({})\necho $r.stras\ntype T { a: Nope }", "test.kscript");
            Assert.Contains(bad.Diagnostics, d => d.Code == "KS5010" && d.Message.Contains("stars"));
            Diagnostic nope = Assert.Single(bad.Diagnostics, d => d.Message.Contains("Nope"));
            Assert.Equal("Nope", (types + "r = Repo({})\necho $r.stras\ntype T { a: Nope }").Substring(nope.Span.Start, nope.Span.Length));

            var service = new LanguageService();
            (string text, int offset) = TestHelpers.WithCaret(types + "r = Repo({})\necho \"{ $r.‸ }\"");
            Assert.Contains(service.GetCompletions(service.Analyze(text, "test.kscript"), offset), i => i.Label == "stars");
            (text, offset) = TestHelpers.WithCaret(types + "type T { a: ‸ }");
            Assert.Contains(service.GetCompletions(service.Analyze(text, "test.kscript"), offset), i => i.Label == "Size");
        }
    }
}
