using KScript.Language.Features;

namespace KScript.Language.Tests
{
    /// <summary>
    /// Completion everywhere a script can be written, in both formats: each position must offer something, and the
    /// thing that works there. Naming positions (a new variable, an import alias) are the only ones left empty.
    /// </summary>
    public class CompletionEverywhereTests
    {
        private static readonly string Folder = Path.Combine(Path.GetTempPath(), "kscript-everywhere");

        private static readonly LanguageService Service = new LanguageService(files: new MemoryFiles()
            .Add(Path.Combine(Folder, "lib.kscript"), "/// The limit.\nconst limit = 3\nenum Size { s, m }\ntype Repo {\n  name: string\n}\nfn add(a, b) => $a + $b\n")
            .Add(Path.Combine(Folder, "lib.ks.xml"), "<kscript>\n  <fn name=\"twice\" params=\"n\" value=\"$n * 2\" />\n</kscript>"));

        private static List<CompletionItem> Complete(string textWithCaret, string file)
        {
            (string text, int offset) = TestHelpers.WithCaret(textWithCaret);
            return Service.GetCompletions(Service.Analyze(text, Path.Combine(Folder, file)), offset);
        }

        private const string Code = "main.kscript";
        private const string Xml = "main.ks.xml";

        public static IEnumerable<object[]> Positions() => new[]
        {
            // .kscript: statements, typed or not yet started.
            new object[] { Code, "‸", "echo" },
            new object[] { Code, "x = 1\n‸", "x" },
            new object[] { Code, "x = 1\ne‸", "echo" },
            new object[] { Code, "fn f(a) {\n  ‸\n}", "return" },
            new object[] { Code, "for x in [1] {\n  ‸\n}", "break" },
            new object[] { Code, "while true {\n  b‸\n}", "continue" },
            new object[] { Code, "try {\n} catch err {\n  ‸\n}", "err" },
            new object[] { Code, "x = 1\nmatch $x {\n  ‸\n}", "case" },
            new object[] { Code, "if true {\n} e‸", "else" },
            new object[] { Code, "x = 1; e‸", "echo" },

            // .kscript: expressions, started or not.
            new object[] { Code, "x = ‸", "@now" },
            new object[] { Code, "x = e‸", "@env" },
            new object[] { Code, "echo ‸", "@now" },
            new object[] { Code, "fn f(a) {\n  return ‸\n}", "$a" },
            new object[] { Code, "if ‸ {\n}", "true" },
            new object[] { Code, "for x in ‸ {\n}", "@range" },
            new object[] { Code, "x = 1 +\n  ‸", "$x" },
            new object[] { Code, "y = 1\nx = [1, ‸]", "$y" },
            new object[] { Code, "x = { a: ‸ }", "@now" },
            new object[] { Code, "y = @round(‸)", "digits:" },
            new object[] { Code, "y = @round(1, d‸)", "digits:" },
            new object[] { Code, "x = fn($a) => ‸", "@now" },
            new object[] { Code, "x = match 1 { 1 => ‸ }", "@now" },
            new object[] { Code, "x = [1]\ny = $x |> ‸", "map" },
            new object[] { Code, "x = 'a'\ny = $x->‸", "upper" },
            new object[] { Code, "x = { a: 1 }\ny = $x.‸", "a" },
            new object[] { Code, "echo \"hi { ‸ }\"", "@now" },
            new object[] { Code, "echo \"hi $‸\"", "args" },
            new object[] { Code, "x = @‸", "now" },
            new object[] { Code, "x = $‸", "x" },
            new object[] { Code, "enum E { a, b }\nx = $E.‸", "a" },

            // .kscript: types, imports and packages.
            new object[] { Code, "fn f(a: ‸) {\n}", "string" },
            new object[] { Code, "fn f(a: s‸) {\n}", "string" },
            new object[] { Code, "type Point {\n  x: int\n}\nfn f(a: ‸) {\n}", "Point" },
            new object[] { Code, "import KScript.H‸ttp", "KScript.Http" },
            new object[] { Code, "type T { a: ‸ }", "int" },
            new object[] { Code, "import ‸", "KScript.IO" },
            new object[] { Code, "import KScript.‸", "KScript.Http" },
            new object[] { Code, "import \"‸\"", "lib.kscript" },
            new object[] { Code, "import ‸ from \"lib.kscript\"", "limit" },
            new object[] { Code, "import add, ‸ from \"lib.kscript\"", "Size" },
            new object[] { Code, "import \"lib.kscript\" as lib\nx = lib.‸", "limit" },
            new object[] { Code, "import \"lib.kscript\"\nx = @‸", "add" },
            new object[] { Code, "import \"lib.kscript\"\nx = $‸", "limit" },
            new object[] { Code, "import \"lib.kscript\"\nx = $Size.‸", "m" },
            new object[] { Code, "KScript.‸", "Routes" },
            new object[] { Code, "KScript.R‸", "Routes" },

            // .kscript: routes.
            new object[] { Code, "KScript.Routes {\n  ‸\n}", "Get" },
            new object[] { Code, "KScript.Routes {\n  G‸\n}", "Group" },
            new object[] { Code, "KScript.Routes {\n  Get(‸)\n}", "path" },
            new object[] { Code, "KScript.Routes {\n  import \"lib.ks.xml\" as people\n  Get(path: \"/x\", to: ‸)\n}", "people.twice" },
            new object[] { Code, "KScript.Routes {\n  Get(path: \"/people/{id}\") {\n    return ‸\n  }\n}", "$id" },
            new object[] { Code, "KScript.Routes {\n  Get(path: \"/x\") {\n    return $request.‸\n  }\n}", "query" },
            new object[] { Code, "KScript.Routes {\n  Post(path: \"/x\") {\n    return $‸\n  }\n}", "json" },

            // XML scripts.
            new object[] { Xml, "<kscript>\n‸\n</kscript>", "echo" },
            new object[] { Xml, "<kscript>\n  e‸\n</kscript>", "echo" },
            new object[] { Xml, "<kscript>\n<‸\n</kscript>", "echo" },
            new object[] { Xml, "<kscript>\n<e‸\n</kscript>", "echo" },
            new object[] { Xml, "<kscript>\n<echo ‸></echo>\n</kscript>", "value" },
            new object[] { Xml, "<kscript>\n<if test=\"‸\"></if>\n</kscript>", "true" },
            new object[] { Xml, "<kscript>\n<let name=\"x\" value=\"‸\" />\n</kscript>", "@now" },
            new object[] { Xml, "<kscript>\n<let name=\"x\" value=\"@round(‸)\" />\n</kscript>", "digits:" },
            new object[] { Xml, "<kscript>\n<echo>{ ‸ }</echo>\n</kscript>", "@now" },
            new object[] { Xml, "<kscript>\n<echo>$‸</echo>\n</kscript>", "args" },
            new object[] { Xml, "<kscript>\n<echo>‸</echo>\n</kscript>", "$args" },
            new object[] { Xml, "<kscript>\n<import package=\"‸\" />\n</kscript>", "KScript.IO" },
            new object[] { Xml, "<kscript>\n<import path=\"lib.kscript\" names=\"‸\" />\n</kscript>", "add" },
            new object[] { Xml, "<kscript>\n<fn name=\"f\" params=\"a: ‸\"></fn>\n</kscript>", "string" },
            new object[] { Xml, "<kscript>\n<fn name=\"f\" params=\"a = ‸\"></fn>\n</kscript>", "@now" },
            new object[] { Xml, "<kscript>\n<for each=\"x\" in=\"‸\"></for>\n</kscript>", "@range" },
            new object[] { Xml, "<kscript>\n<fn name=\"go\"></fn>\n<call fn=\"‸\" />\n</kscript>", "go" },
            new object[] { Xml, "<KScript.‸", "KScript.Routes" },
            new object[] { Xml, "<KScript.R‸", "KScript.Routes" },
            new object[] { Xml, "<KScript.Routes>\n<G‸\n</KScript.Routes>", "Get" },
            new object[] { Xml, "<kscript>\n<match value=\"1\">\n<‸\n</match>\n</kscript>", "case" },
        };

        [Theory]
        [MemberData(nameof(Positions))]
        public void Every_position_offers_what_works_there(string file, string text, string expected)
        {
            List<CompletionItem> items = Complete(text, file);
            Assert.NotEmpty(items);
            Assert.Contains(items, i => i.Label == expected);
        }

        [Theory]
        [InlineData(Code, "import KScript.Math as ‸")]
        [InlineData(Code, "fn f(‸) {\n}")]
        [InlineData(Code, "// e‸")]
        [InlineData(Code, "import \"lib.kscript\" as lib\nx = $lib.‸")]
        [InlineData(Code, "import a from \"missing.kscript\"\nimport ‸ from \"missing.kscript\"")]
        [InlineData(Xml, "<kscript>\n<echo>hello e‸</echo>\n</kscript>")]
        public void Naming_positions_comments_and_mistakes_offer_nothing(string file, string text) => Assert.Empty(Complete(text, file));

        [Fact]
        public void Only_cases_go_in_a_match()
        {
            Assert.Equal(new[] { "case", "default", "when" }, Complete("x = 1\nmatch $x {\n  ‸\n}", Code).Select(i => i.Label).OrderBy(l => l, StringComparer.Ordinal));
            List<string> xml = Complete("<kscript>\n<match value=\"1\">\n<‸\n</match>\n</kscript>", Xml).Select(i => i.Label).ToList();
            Assert.Contains("default", xml);
            Assert.DoesNotContain("echo", xml);
        }

        [Fact]
        public void Package_items_are_offered_only_once_imported()
        {
            // A command, statement or element from a package the script doesn't import isn't offered: it wouldn't work.
            Assert.DoesNotContain(Complete("x = 1\ny = @‸", Code), i => i.Label == "read_text");
            CompletionItem read = Assert.Single(Complete("import KScript.IO\ny = @‸", Code), i => i.Label == "read_text");
            Assert.Null(read.AdditionalEdits);
            Assert.Contains("now", Complete("x = 1\ny = @‸", Code).Select(i => i.Label));

            Assert.DoesNotContain(Complete("import KScript.IO\n‸", Code), i => i.Label == "input");
            Assert.Contains(Complete("import KScript.Terminal\n‸", Code), i => i.Label == "input");
            Assert.DoesNotContain(Complete("<kscript>\n  <echo>hi</echo>\n  <‸\n</kscript>", Xml), i => i.Label == "picker");
            Assert.Contains(Complete("<kscript>\n  <import package=\"KScript.Terminal\" />\n  <‸\n</kscript>", Xml), i => i.Label == "picker");
            Assert.DoesNotContain(Complete("KScript.Routes {\n  Get(path: \"/x\") {\n    return @‸\n  }\n}", Code), i => i.Label == "read_text");

            // Inside a function too: KScript.Document's Section and Paragraph need the import.
            List<string> inFn = Complete("fn report() {\n  ‸\n}", Code).Select(i => i.Label).ToList();
            Assert.DoesNotContain("Section", inFn);
            Assert.DoesNotContain("Document", inFn);
            Assert.Contains("echo", inFn);
            List<string> imported = Complete("import KScript.Document\n\nfn report() {\n  ‸\n}", Code).Select(i => i.Label).ToList();
            Assert.Contains("Section", imported);
            Assert.Contains("Document", imported);

            // Methods too: ->markdown_escape() is KScript.Markdown's.
            Assert.DoesNotContain("markdown_escape", Complete("echo 'a'->‸", Code).Select(i => i.Label));
            Assert.Contains("markdown_escape", Complete("import KScript.Markdown\necho 'a'->‸", Code).Select(i => i.Label));

            // A package that is already imported is listed last when importing.
            List<CompletionItem> packages = Complete("import KScript.IO\nimport ‸", Code);
            Assert.Equal("imported", Assert.Single(packages, i => i.Label == "KScript.IO").Detail);
            Assert.DoesNotContain(packages, i => i.Label == "KScript.Core");
        }

        [Fact]
        public void Statements_come_as_snippets_in_the_file_format()
        {
            List<CompletionItem> code = Complete("‸", Code);
            Assert.Equal("type ${1:Name} {\n\t${2:field}: ${3:string}\n}", Assert.Single(code, i => i.Label == "type").InsertText);
            Assert.Equal("enum ${1:Name} { ${2:a}, ${3:b} }", Assert.Single(code, i => i.Label == "enum").InsertText);
            Assert.StartsWith("describe(name: \"${1}\") {", Assert.Single(Complete("import KScript.Test\n‸", Code), i => i.Label == "describe").InsertText, StringComparison.Ordinal);

            List<CompletionItem> routes = Complete("KScript.‸", Code);
            Assert.Equal("Routes {\n\t$0\n}", Assert.Single(routes, i => i.Label == "Routes").InsertText);
            Assert.Equal("KScript.Http", Assert.Single(routes, i => i.Label == "Routes").Detail);
            Assert.DoesNotContain(routes, i => i.Label == "Get");
            Assert.StartsWith("Get(path: \"${1}\") {", Assert.Single(Complete("KScript.Routes {\n  ‸\n}", Code), i => i.Label == "Get").InsertText, StringComparison.Ordinal);
            Assert.Equal("KScript.Routes>\n\t$0\n</KScript.Routes>", Assert.Single(Complete("<KScript.‸", Xml), i => i.Label == "KScript.Routes").InsertText);
            Assert.Null(Assert.Single(Complete("<KScript.R‸ a=\"1\">", Xml), i => i.Label == "KScript.Routes").InsertText);

            // A plain word statement has no snippet.
            Assert.Null(Assert.Single(code, i => i.Label == "exit").InsertText);
        }

        [Fact]
        public void Imported_file_members_keep_their_kind()
        {
            List<CompletionItem> members = Complete("import \"lib.kscript\" as lib\nx = lib.‸", Code);
            Assert.Equal(CompletionKind.Function, Assert.Single(members, i => i.Label == "add").Kind);
            CompletionItem limit = Assert.Single(members, i => i.Label == "limit");
            Assert.Equal(CompletionKind.Constant, limit.Kind);
            Assert.Null(limit.InsertText);

            List<CompletionItem> names = Complete("import ‸ from \"lib.kscript\"", Code);
            Assert.Equal(CompletionKind.Function, Assert.Single(names, i => i.Label == "add").Kind);
            Assert.Equal(CompletionKind.Constant, Assert.Single(names, i => i.Label == "limit").Kind);
        }

        [Fact]
        public void Docs_are_written_in_the_files_own_format()
        {
            // A .kscript file shows names and examples as .kscript; element names in the text show as code, not hidden as HTML.
            CompletionItem code = Assert.Single(Complete("import KScript.Test\n‸", Code), i => i.Label == "after_each");
            Assert.StartsWith("**after_each**", code.Documentation, StringComparison.Ordinal);
            Assert.Contains("in its file or `describe`, even", code.Documentation, StringComparison.Ordinal);
            Assert.Contains("```kscript\nafter_each {\n  cart = []\n}\n```", code.Documentation, StringComparison.Ordinal);
            Assert.Contains("| Argument |", Assert.Single(Complete("import KScript.IO\n‸", Code), i => i.Label == "write").Documentation, StringComparison.Ordinal);

            CompletionItem xml = Assert.Single(Complete("<kscript>\n<import package=\"KScript.Test\" />\n<‸\n</kscript>", Xml), i => i.Label == "after_each");
            Assert.StartsWith("**<after_each>**", xml.Documentation, StringComparison.Ordinal);
            Assert.Contains("in its file or `<describe>`, even", xml.Documentation, StringComparison.Ordinal);
            Assert.Contains("```xml\n<after_each>", xml.Documentation, StringComparison.Ordinal);

            (string text, int offset) = TestHelpers.WithCaret("import KScript.Test\nafter_ea‸ch {\n}");
            Assert.Contains("```kscript\nafter_each {", Service.GetHover(Service.Analyze(text, Path.Combine(Folder, Code)), offset).Markdown, StringComparison.Ordinal);

            // Text already in backticks stays as it is; an example that isn't a script stays XML.
            Assert.Equal("`<if $x>` or `else`", LanguageService.TagsAsCode("`<if $x>` or <else />", code: true));
            Assert.Null(LanguageService.TagsAsCode(null, code: true));
            Assert.Null(LanguageService.ExampleAsCode("<live id=\"search\"><input bind=\"query\"></live>"));
        }

        [Fact]
        public void Planned_elements_still_explain_themselves_on_hover()
        {
            (string text, int offset) = TestHelpers.WithCaret("<kscript>\n<ren‸der />\n</kscript>");
            Assert.Contains("planned", Service.GetHover(Service.Analyze(text, Path.Combine(Folder, Xml)), offset).Markdown, StringComparison.Ordinal);
        }
    }
}
