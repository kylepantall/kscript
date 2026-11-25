using KScript.Language.Analysis;
using KScript.Language.Features;

using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests
{
    /// <summary>
    /// The editor as a typed language: what functions return, types that flow through set and defaults, member
    /// completion everywhere, inlay type hints, typed hovers, type tokens, and notebook-style blocks.
    /// </summary>
    public class TypedEditorTests
    {
        private sealed class FakeFiles : IFileSystem
        {
            private readonly Dictionary<string, string> files;
            public FakeFiles(Dictionary<string, string> files) => this.files = files.ToDictionary(k => Path.GetFullPath(k.Key), k => k.Value);
            public bool FileExists(string path) => files.ContainsKey(Path.GetFullPath(path));
            public bool DirectoryExists(string path) => files.Keys.Any(f => f.StartsWith(Path.GetFullPath(path) + Path.DirectorySeparatorChar, StringComparison.Ordinal));
            public string ReadText(string path) => files.TryGetValue(Path.GetFullPath(path), out string t) ? t : null;
            public IEnumerable<string> ListFiles(string directory, string pattern) => Enumerable.Empty<string>();
        }

        private static readonly LanguageService Service = new LanguageService();

        private static DocumentAnalysis Open(string body, LanguageService service = null, string path = "/tmp/typed/test.ks.xml") =>
            (service ?? Service).Analyze(Script(body), path);

        private static List<CompletionItem> Complete(string body, LanguageService service = null)
        {
            (string text, int offset) = WithCaret(Script(body));
            LanguageService s = service ?? Service;
            return s.GetCompletions(s.Analyze(text, "/tmp/typed/test.ks.xml"), offset);
        }

        private static List<string> Labels(string body) => Complete(body).Select(i => i.Label).ToList();

        private static Symbol Named(DocumentAnalysis a, string name) => a.Symbols.First(s => s.Name == name && s.SourcePath == null);

        [Fact]
        public void FunctionsKnowWhatTheyReturn()
        {
            DocumentAnalysis a = Open(
                "<fn name=\"double\" params=\"x: int\" value=\"$x * 2\" />\n" +
                "<fn name=\"greet\" params=\"who\">\n  <if test=\"$who eq ''\"><return value=\"null\" /></if>\n  <return value=\"'Hi ' + $who\" />\n</fn>\n" +
                "<fn name=\"person\" value=\"{ name: 'Joan', age: 82 }\" />");
            Assert.Equal("number", Named(a, "double").ReturnShape.Display());
            Assert.Equal("string", Named(a, "greet").ReturnShape.Display());
            Assert.Equal("{ name: string, age: number }", Named(a, "person").ReturnShape.Display());
        }

        [Fact]
        public void ATypeCatchCanChangeTheReturnTypeOfItsMethods()
        {
            var analysis = Service.Analyze("""
                type Probe {
                  fn status(offline: bool) {
                    if $offline { throw Network "down" }
                    return 200
                  }
                } catch Network { return 'offline' }
                """, "probe.kscript");
            Assert.Equal("any", Named(analysis, "status").ReturnShape.Type);
        }

        [Fact]
        public void ResultsOfCallsHaveTypesEvenWhenTheFunctionComesLater()
        {
            // The call is above the function, so this needs a second pass.
            List<string> labels = Labels("<let name=\"p\" value=\"@person()\" />\n<echo>{ $p.‸ }</echo>\n<fn name=\"person\" value=\"{ name: 'Joan', age: 82 }\" />");
            Assert.Equal(new[] { "age", "name" }, labels.OrderBy(l => l));

            List<string> methods = Labels("<let name=\"s\" value=\"'x' |> @shout()\" />\n<echo>{ $s->‸ }</echo>\n<fn name=\"shout\" params=\"t\" value=\"$t->upper() + '!'\" />");
            Assert.Contains("upper", methods);
            Assert.DoesNotContain("filter", methods);
        }

        [Fact]
        public void ChainsOfFunctionsSettle()
        {
            DocumentAnalysis a = Open("<let name=\"r\" value=\"@a()\" />\n<fn name=\"a\" value=\"@b()\" />\n<fn name=\"b\" value=\"@c()\" />\n<fn name=\"c\" value=\"[1, 2]\" />");
            Assert.Equal("list<number>", Named(a, "r").Shape.Display());
        }

        [Fact]
        public void RecursiveFunctionsDontLoop()
        {
            DocumentAnalysis a = Open("<fn name=\"fact\" params=\"n\">\n  <if test=\"$n le 1\"><return value=\"1\" /></if>\n  <return value=\"$n * @fact($n - 1)\" />\n</fn>");
            Assert.Equal("number", Named(a, "fact").ReturnShape.Display());
        }

        [Fact]
        public void ImportedFunctionsKnowWhatTheyReturn()
        {
            LanguageService service = new LanguageService(files: new FakeFiles(new Dictionary<string, string>
            {
                ["/tmp/typed/lib.ks.xml"] = "<kscript>\n  <fn name=\"make\" value=\"{ id: 1, tags: ['a'] }\" />\n  <let name=\"limit\" const=\"yes\" value=\"10\" />\n</kscript>",
            }));
            List<string> fields = Complete("<import path=\"lib.ks.xml\" />\n<let name=\"m\" value=\"@make()\" />\n<echo>{ $m.‸ }</echo>", service).Select(i => i.Label).ToList();
            Assert.Equal(new[] { "id", "tags" }, fields.OrderBy(l => l));
        }

        [Fact]
        public void ImportCyclesStop()
        {
            LanguageService service = new LanguageService(files: new FakeFiles(new Dictionary<string, string>
            {
                ["/tmp/typed/a.ks.xml"] = "<kscript><import path=\"b.ks.xml\" /><fn name=\"fa\" value=\"@fb()\" /></kscript>",
                ["/tmp/typed/b.ks.xml"] = "<kscript><import path=\"a.ks.xml\" /><fn name=\"fb\" value=\"@fa()\" /></kscript>",
            }));
            DocumentAnalysis a = service.Analyze("<kscript><import path=\"a.ks.xml\" /><let name=\"x\" value=\"@fa()\" /></kscript>", "/tmp/typed/main.ks.xml");
            Assert.True(Named(a, "x").Shape.IsVague);
        }

        [Fact]
        public void SetFillsInAVariableDeclaredAsNull()
        {
            Assert.Contains("upper", Labels("<let name=\"x\" value=\"null\" />\n<set name=\"x\" value=\"'a'\" />\n<echo>{ $x->‸ }</echo>"));
            Assert.Contains("upper", Labels("<let name=\"x\" value=\"null\" />\n<set>x = 'a'</set>\n<echo>{ $x->‸ }</echo>"));
            // A known type isn't replaced by another one: from then on the variable could hold either, so nothing is claimed.
            DocumentAnalysis a = Open("<let name=\"n\" value=\"1\" />\n<set name=\"n\" value=\"'a'\" />");
            Assert.True(Named(a, "n").Shape.IsVague);
            Assert.Equal("number", Named(Open("<let name=\"n\" value=\"1\" />\n<set name=\"n\" value=\"2\" />"), "n").Shape.Display());
        }

        [Fact]
        public void ParametersTakeTheTypeOfTheirDefault()
        {
            DocumentAnalysis a = Open("<fn name=\"f\" params=\"count = 0, label = 'x', on = true, items = []\" value=\"$count\" />");
            Assert.Equal("number", Named(a, "count").Shape.Display());
            Assert.Equal("string", Named(a, "label").Shape.Display());
            Assert.Equal("bool", Named(a, "on").Shape.Display());
            Assert.Equal("list", Named(a, "items").Shape.Display());
            Assert.Equal("number", Named(a, "f").ReturnShape.Display());
        }

        [Fact]
        public void MatchExpressionsHaveTheTypeOfTheirArms()
        {
            DocumentAnalysis a = Open("<let name=\"n\" value=\"2\" />\n<let name=\"label\" value=\"match $n { 1 => 'one', _ => 'many' }\" />");
            Assert.Equal("string", Named(a, "label").Shape.Display());
        }

        [Theory]
        [InlineData("test.kscript", "choice = 'other'\nlarge = $choice > 9\necho $large")]
        [InlineData("test.ks.xml", "<kscript><let name=\"choice\" value=\"'other'\" /><let name=\"large\" value=\"$choice gt 9\" /><echo value=\"$large\" /></kscript>")]
        public void ComparingTextWithANumberHasBooleanType(string path, string source)
        {
            DocumentAnalysis analysis = Service.Analyze(source, path);

            Assert.Empty(analysis.Diagnostics);
            Assert.Equal("bool", Named(analysis, "large").Shape.Display());
        }

        [Theory]
        [InlineData("<echo>$x->‸</echo>")]
        [InlineData("<echo>Value: $x->up‸</echo>")]
        public void MethodsCompleteAfterBareReferencesInText(string echo)
        {
            List<CompletionItem> items = Complete("<let name=\"x\" value=\"'hi'\" />\n" + echo);
            Assert.Contains(items, i => i.Label == "upper");
            Assert.DoesNotContain(items, i => i.Label == "filter");
        }

        [Fact]
        public void FieldsCompleteAfterBareReferencesInText()
        {
            Assert.Equal(new[] { "age", "name" }, Labels("<let name=\"p\" value=\"{ name: 'Joan', age: 82 }\" />\n<echo>Hi $p.‸</echo>").OrderBy(l => l));
        }

        [Fact]
        public void TheValuesOwnMethodsComeFirst()
        {
            List<CompletionItem> items = Complete("<let name=\"x\" value=\"'hi'\" />\n<echo>{ $x->‸ }</echo>").OrderBy(i => i.SortText).ToList();
            int upper = items.FindIndex(i => i.Label == "upper");
            int type = items.FindIndex(i => i.Label == "type");
            int round = items.FindIndex(i => i.Label == "round");
            Assert.True(upper < type && type < round, string.Join(", ", items.Select(i => i.Label)));
            Assert.Equal("->upper(): string", items[upper].Detail);
        }

        [Fact]
        public void ListMethodsKeepTheItemType()
        {
            CompletionItem filter = Complete("<let name=\"xs\" value=\"[{ n: 1 }]\" />\n<echo>{ $xs->‸ }</echo>").First(i => i.Label == "filter");
            Assert.Equal("->filter(fn): list<{ n: number }>", filter.Detail);
        }

        [Fact]
        public void HoversShowStructuralTypes()
        {
            (string text, int offset) = WithCaret(Script("<let name=\"people‸\" value=\"[{ name: 'Joseph', age: 28, address: { city: 'Leeds', postcode: 'LS1' } }]\" />"));
            HoverResult hover = Service.GetHover(Service.Analyze(text, "test.ks.xml"), offset);
            Assert.Contains("let $people: list<{\n  name: string,\n  age: number,\n  address: { city: string, postcode: string }\n}>", hover.Markdown);

            (text, offset) = WithCaret(Script("<fn name=\"double\" params=\"x: int\" value=\"$x * 2\" />\n<echo>{ @dou‸ble(2) }</echo>"));
            Assert.Contains("@double(x: int): number", Service.GetHover(Service.Analyze(text, "test.ks.xml"), offset).Markdown);
        }

        [Fact]
        public void InlayHintsShowTypesThatAreNotWrittenDown()
        {
            string body =
                "<let name=\"n\" value=\"3\" />\n" +
                "<let name=\"people\" value=\"[{ name: 'Joan', age: 82 }]\" />\n" +
                "<let name=\"names\" value=\"$people->map(fn(p) => $p.name)\" />\n" +
                "<let name=\"t\" type=\"int\" value=\"@now()->year\" />\n" +
                "<for each=\"p\" in=\"$people\"><echo>$p.name</echo></for>\n" +
                "<fn name=\"double\" params=\"x: int, by = 2\" value=\"$x * $by\" />\n" +
                "<fn name=\"unknown\" params=\"x\" value=\"$x\" />";
            DocumentAnalysis a = Open(body);
            string text = a.Source.Text;
            List<string> hints = Service.GetInlayHints(a).Select(h => text.Substring(0, h.Offset).Split('\n').Last().Trim() + "⟦" + h.Label + "⟧").ToList();

            Assert.Contains("<let name=\"people⟦: list<{ name: string, age: number }>⟧", hints);
            Assert.Contains("<let name=\"names⟦: list<string>⟧", hints);
            Assert.Contains("<let name=\"names\" value=\"$people->map(fn(p⟦: { name: string, age: number }⟧", hints);
            Assert.Contains("<for each=\"p⟦: { name: string, age: number }⟧", hints);
            Assert.Contains("<fn name=\"double\" params=\"x: int, by = 2\"⟦ → number⟧", hints);
            // Written-down types get none: a literal, type="int", x: int, by = 2, or nothing known.
            Assert.DoesNotContain(hints, h => h.StartsWith("<let name=\"n⟦", StringComparison.Ordinal) || h.StartsWith("<let name=\"t⟦", StringComparison.Ordinal));
            Assert.DoesNotContain(hints, h => h.Contains("x⟦") || h.Contains("by⟦") || h.Contains("unknown"));
        }

        [Fact]
        public void KscriptReturnHintsFollowTheParameterList()
        {
            string text = "fn none() => 1\nfn one(key) => $key == 'a'\nfn spaced (a, b = (1)) {\n  return 'x'\n}\n";
            DocumentAnalysis a = Service.Analyze(text, "test.kscript");
            List<string> hints = Service.GetInlayHints(a).Where(h => h.Label.StartsWith(" →", StringComparison.Ordinal))
                .Select(h => text.Substring(0, h.Offset).Split('\n').Last() + "⟦" + h.Label + "⟧").ToList();
            Assert.Equal(new[] { "fn none()⟦ → number⟧", "fn one(key)⟦ → bool⟧", "fn spaced (a, b = (1))⟦ → string⟧" }, hints);
        }

        [Theory]
        [InlineData("test.kscript", "fn declared(value: int): number { return $value }\nfn inferred() => 1")]
        [InlineData("test.ks.xml", "<kscript><fn name=\"declared\" returns=\"number\" value=\"1\" />\n<fn name=\"inferred\" value=\"1\" /></kscript>")]
        public void ReturnHintsOnlyAppearForInferredTypes(string path, string text)
        {
            DocumentAnalysis analysis = Service.Analyze(text, path);
            InlayHint hint = Assert.Single(Service.GetInlayHints(analysis), h => h.Label.StartsWith(" →", StringComparison.Ordinal));

            Assert.Equal(" → number", hint.Label);
            Assert.Equal(1, analysis.Source.GetPosition(hint.Offset).Line);
        }

        [Fact]
        public void AReceiverSpecificMethodOnAnyHasNoConcreteReturnHint()
        {
            const string text = "fn trim_dynamic(key: any) => $key->trim()\n"
                + "fn trim_text(key: string) => $key->trim()\n"
                + "fn stringify(key: any) => $key->to_string()\n";
            var analysis = Service.Analyze(text, "trim.kscript");
            var hints = Service.GetInlayHints(analysis).Where(h => h.Label.StartsWith(" →", StringComparison.Ordinal))
                .Select(h => (analysis.Source.GetPosition(h.Offset).Line, h.Label));
            Assert.Equal(new[] { (1, " → string"), (2, " → string") }, hints);
        }

        [Fact]
        public void WideMapsGetShortHints()
        {
            DocumentAnalysis a = Open("<let name=\"m\" value=\"{ alpha: 1, beta: 2, gamma: 3, delta: 4, epsilon: 5, zeta: 6, eta: 7 }\" />");
            Assert.Equal(": { alpha, beta, gamma, delta, epsilon, … }", Service.GetInlayHints(a).Single().Label);
        }

        [Fact]
        public void TypeNamesAreHighlightedAsTypes()
        {
            DocumentAnalysis a = Open("<fn name=\"f\" params=\"count: int, name: string = 'x'\" value=\"1\" />\n<let name=\"y\" type=\"list\" value=\"[]\" />");
            List<string> types = Service.GetSemanticTokens(a).Where(t => t.Type == SemanticTokenType.Type).Select(t => a.Source.Text.Substring(t.Span.Start, t.Span.Length)).ToList();
            Assert.Equal(new[] { "int", "string", "list" }, types);
        }

        [Fact]
        public void JsonWrittenInAnEchoIsNotCheckedAsKScript()
        {
            AssertClean(Open("<echo format=\"json\">\n  { \"name\": \"Joan\", \"tags\": [1, 2] }\n</echo>"));
        }

        [Fact]
        public void CommentsHeadBlocks()
        {
            DocumentAnalysis a = Open(
                "<import package=\"KScript.IO\" />\n\n" +
                "<!-- Setup -->\n<let name=\"a\" value=\"1\" />\n<let name=\"b\" value=\"2\" />\n\n" +
                "<!-- Work -->\n<!-- More about the work. -->\n<if test=\"$a gt 0\">\n  <echo>yes</echo>\n</if>\n<else>\n  <echo>no</echo>\n</else>");
            List<CodeBlock> blocks = Service.GetBlocks(a);
            Assert.Equal(new[] { "import", "Setup", "Work" }, blocks.Select(b => b.Label));
            Assert.EndsWith("</else>", a.Source.Text.Substring(blocks[2].Span.Start, blocks[2].Span.Length));
        }

        [Fact]
        public void WithoutCommentsFunctionsAndLoopsAreBlocks()
        {
            DocumentAnalysis a = Open(
                "<let name=\"a\" value=\"1\" />\n<let name=\"b\" value=\"2\" />\n" +
                "<fn name=\"f\">\n  <return value=\"1\" />\n</fn>\n" +
                "<if test=\"$a\">\n  <echo>a</echo>\n</if>\n<else>\n  <echo>b</echo>\n</else>\n" +
                "<echo>done</echo>\n<echo>really</echo>");
            Assert.Equal(new[] { "let a", "fn f", "if", "echo" }, Service.GetBlocks(a).Select(b => b.Label));
        }

        [Fact]
        public void PagesHaveNoBlocks()
        {
            Assert.Empty(Service.GetBlocks(Service.Analyze("<h1>Hi</h1>\n<p>There</p>", "/tmp/site/index.html")));
        }

        [Fact]
        public void MultiLineElementsFold()
        {
            DocumentAnalysis a = Open("<!--\n  About\n-->\n<fn name=\"f\">\n  <let name=\"x\" value=\"1\" />\n  <return value=\"$x\" />\n</fn>");
            List<FoldingRegion> folds = Service.GetFoldingRegions(a);
            Assert.Contains(folds, f => f.IsComment && f.StartLine == 1 && f.EndLine == 3);
            Assert.Contains(folds, f => !f.IsComment && f.StartLine == 4 && f.EndLine == 6);
            Assert.Contains(folds, f => !f.IsComment && f.StartLine == 0);
        }
    }
}
