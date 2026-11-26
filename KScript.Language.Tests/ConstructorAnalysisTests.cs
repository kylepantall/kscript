using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Syntax;
using KScript.Syntax.Markup;

namespace KScript.Language.Tests
{
    /// <summary>construct({ ... }) { ... } and one-value handlers in the parser, the checker, the converter and completion.</summary>
    public sealed class ConstructorAnalysisTests : IDisposable
    {
        private readonly string folder = Path.Combine(Path.GetTempPath(), "kscript-construct-" + Guid.NewGuid().ToString("N"));
        private readonly LanguageService service = new LanguageService();

        public ConstructorAnalysisTests() => Directory.CreateDirectory(Path.Combine(folder, "_components"));

        public void Dispose() => Directory.Delete(folder, recursive: true);

        private string Write(string name, string text)
        {
            string path = Path.Combine(folder, name);
            File.WriteAllText(path, text);
            return path;
        }

        private DocumentAnalysis Check(string name, string text) => TestHelpers.WithoutUnused(service.Analyze(text, Write(name, text)));

        private static string Messages(DocumentAnalysis a) => string.Join("\n", a.Diagnostics.Select(d => d.Code + " " + d.Message));

        private const string Point = """
            type Point {
              construct({
                x: number,
                y: number = 2,
              }) {
                sum = $x + $y
              }

              sum: number
            }

            """;

        [Fact]
        public void A_constructor_parses_into_its_fields_and_its_statements()
        {
            MarkupDocument document = CodeParser.Parse(Point);
            Assert.Empty(document.Diagnostics);
            MarkupElement type = document.RootElements.First().ChildElements.First();
            MarkupElement construct = TypeBody.Constructor(type);
            Assert.Equal(new[] { "field", "field", "do" }, construct.ChildElements.Select(c => c.Name));
            Assert.Equal(new[] { "x", "y", "sum" }, TypeBody.Fields(type).Select(f => f.GetAttributeValue("name")));
            Assert.Equal(new[] { "x", "y" }, TypeBody.Inputs(type).Select(f => f.GetAttributeValue("name")));
            Assert.Equal("2", construct.ChildElements.ElementAt(1).GetAttributeValue("default").Trim());
            Assert.Empty(TypeBody.Fields(null));
            Assert.Null(TypeBody.Constructor(null));
        }

        [Theory]
        [InlineData("type T {\n  construct(x: int)\n}\n", "List what a caller may pass in braces")]
        [InlineData("type T {\n  construct({ x: int }\n}\n", "missing its closing )")]
        [InlineData("type T {\n  construct({ x: int", "missing its closing }")]
        public void An_unfinished_constructor_says_what_is_missing(string code, string message)
        {
            Assert.Contains(CodeParser.Parse(code).Diagnostics, d => d.Message.Contains(message));
        }

        [Fact]
        public void A_field_named_construct_is_still_a_field()
        {
            MarkupDocument document = CodeParser.Parse("type T {\n  construct: string\n}\n");
            Assert.Empty(document.Diagnostics);
            Assert.Equal("field", document.RootElements.First().ChildElements.First().ChildElements.Single().Name);
        }

        [Fact]
        public void The_checker_knows_the_fields_and_refuses_own_state_from_a_caller()
        {
            TestHelpers.AssertClean(Check("ok.kscript", Point + "p = Point(x: 1)\necho \"{$p.x} {$p.y} {$p.sum}\"\n"));
            string problems = Messages(Check("bad.kscript", Point + "echo Point(x: 1, sum: 3)\n"));
            Assert.Contains("KS5007 'sum' is Point's own state and can't be passed in. construct takes: x, y.", problems);
        }

        [Theory]
        [InlineData("type T {\n  construct({ a: int })\n  construct({ b: int })\n}\n", "T has two construct blocks")]
        [InlineData("type T {\n  construct({ a: int, a: int })\n}\n", "The field 'a' is listed twice")]
        [InlineData("type T {\n  construct({ a: int => 1 })\n}\n", "can't be worked out with =>")]
        [InlineData("type Age with [int] {\n  construct({ a: int })\n}\n", "can only hold")]
        public void A_mistake_in_a_constructor_is_reported(string code, string message)
        {
            Assert.Contains(message, Messages(Check("mistake.kscript", code)));
        }

        [Fact]
        public void A_constructor_converts_to_xml_and_back()
        {
            string xml = ScriptConverter.Convert(Point + "type Named {\n  /// Who it is.\n  construct({ name: string })\n}\n", "point.kscript").Text;
            Assert.Contains("<construct>\n      <field name=\"x\" type=\"number\" />", xml);
            Assert.Contains("<set name=\"sum\" value=\"$x + $y\" />", xml);
            string code = ScriptConverter.Convert(xml, "point.ks.xml").Text;
            Assert.Contains("  construct({ x: number, y: number = 2 }) {\n    sum = $x + $y\n  }\n", code);
            // More than fits on a line goes one field per line, each with its comma.
            string many = ScriptConverter.Convert("type Wide {\n  construct({ first_name: string, last_name: string, email: email, joined: date })\n}\n", "wide.kscript").Text;
            Assert.Contains("  construct({\n    first_name: string,\n    last_name: string,\n    email: email,\n    joined: date,\n  })\n", ScriptConverter.Convert(many, "wide.ks.xml").Text);
            Assert.Contains("  /// Who it is.\n  construct({ name: string })\n", code);
            // An attribute the short form can't hold keeps the type in its element form.
            Assert.Contains("construct(", ScriptConverter.Convert("<kscript><type name=\"T\"><construct><field name=\"a\" type=\"int\" /></construct></type></kscript>", "t.ks.xml").Text);
            Assert.DoesNotContain("construct({", ScriptConverter.Convert("<kscript><type name=\"T\"><construct other=\"1\"><field name=\"a\" type=\"int\" /></construct></type></kscript>", "t.ks.xml").Text);
        }

        [Fact]
        public void A_type_body_offers_construct_until_it_has_one()
        {
            (string text, int offset) = TestHelpers.WithCaret("type T {\n  name: string\n  c‸\n}\n");
            List<CompletionItem> items = service.GetCompletions(service.Analyze(text, Write("offer.kscript", text)), offset, null);
            CompletionItem construct = Assert.Single(items, i => i.Label == "construct");
            Assert.StartsWith("construct({", construct.InsertText);
            Assert.Contains("caller may pass", construct.Documentation);
            (text, offset) = TestHelpers.WithCaret("type T {\n  construct({ name: string })\n  c‸\n}\n");
            Assert.DoesNotContain(service.GetCompletions(service.Analyze(text, Write("offer.kscript", text)), offset, null), i => i.Label == "construct");
        }

        private const string Card = """
            import KScript.Web

            type FlipCard extends Component {
              construct({ title: string, open: bool = false })

              count: int
              label: string = 'x'

              fn add() {
                count += 2
              }

              fn render(): Component {
                return ```html
                <p>{$title} {$open} {$count} {$label}</p>
                HANDLERS
                ```
              }
            }
            """;

        private string Card1(string handlers) => Card.Replace("HANDLERS", handlers);

        [Fact]
        public void A_handler_can_be_a_bool_to_flip_or_a_method_to_call()
        {
            TestHelpers.AssertClean(Check("_components/flip-card.kscript", Card1("<button @on-click(toggle: $open)>Flip</button><button on-click=\"$add\">Add</button>")));
            string problems = Messages(Check("_components/flip-card.kscript", Card1("<button @on-click($label)>Bad</button><button @on-click($nope)>Bad</button>")));
            Assert.Contains("A handler that is one value calls a function, and $label is string.", problems);
            Assert.Contains("$nope is not defined", problems);
        }

        [Fact]
        public void Toggle_flips_a_bool_and_a_bare_bool_on_a_page_offers_toggle()
        {
            TestHelpers.AssertClean(Check("_components/flip-card.kscript", Card1("<button @on-click(toggle: $open)>Flip</button><button on-click=\"toggle: $open\">Flip</button>")));

            string bare = Card1("<button @on-click($open)>Flip</button>");
            Diagnostic problem = Assert.Single(Check("_components/flip-card.kscript", bare).Diagnostics);
            Assert.Equal("A handler's value is an action to run, and $open is a bool. Write toggle: $open to flip it.", problem.Message);
            Assert.Equal("$open", bare.Substring(problem.Span.Start, problem.Span.Length));
            QuickFix fix = Assert.Single(problem.Fixes);
            Assert.Equal("Change to toggle: $open", fix.Title);
            Assert.Contains("@on-click(toggle: $open)", AnalyzerTests.Apply(bare, fix.Changes));
            TestHelpers.AssertClean(Check("_components/flip-card.kscript", AnalyzerTests.Apply(bare, fix.Changes)));
        }

        [Fact]
        public void A_misspelt_shorthand_offers_the_right_name()
        {
            string text = Card1("<button @on-click(togle: $open)>x</button>");
            Diagnostic problem = Assert.Single(Check("_components/flip-card.kscript", text).Diagnostics);
            Assert.Equal("togle", text.Substring(problem.Span.Start, problem.Span.Length));
            TestHelpers.AssertClean(Check("_components/flip-card.kscript", AnalyzerTests.Apply(text, Assert.Single(problem.Fixes).Changes)));
        }

        [Fact]
        public void A_window_handler_can_be_a_bool_or_a_function()
        {
            const string window = """
                import KScript.UI

                Window(title: "Flip") {
                  open = false
                  label = 'x'

                  /// @deprecated Use open.
                  fn add() {
                    open = true
                  }

                  Text "{$open}{$label}"
                  Button(on_click: $open) "Toggle"
                  Button(on_click: $add) "Add"
                  HANDLER
                }
                """;
            Assert.Equal("KS4008 @add is deprecated. Use open.", Messages(Check("win.kscript", window.Replace("HANDLER", string.Empty))).Trim());
            Assert.Contains("$label is string", Messages(Check("win.kscript", window.Replace("HANDLER", "Button(on_click: $label) \"Bad\""))));
        }

        [Fact]
        public void A_page_can_pass_only_what_the_component_constructor_lists()
        {
            Write("_components/flip-card.kscript", Card1("<button @on-click(toggle: $open)>Flip</button>"));
            TestHelpers.AssertClean(Check("index.html", "<flip-card title=\"a\" open=\"{true}\" />"));
            Assert.Contains("KS5007 'count' is the component's own state and can't be passed in.", Messages(Check("index.html", "<flip-card title=\"a\" count=\"{2}\" />")));
            (string text, int offset) = TestHelpers.WithCaret("<flip-card ‸ />");
            List<string> props = service.GetCompletions(service.Analyze(text, Write("index.html", text)), offset, null).Select(i => i.Label).ToList();
            Assert.Contains("title", props);
            Assert.DoesNotContain("count", props);
        }
    }
}
