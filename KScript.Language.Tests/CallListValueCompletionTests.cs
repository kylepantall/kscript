using KScript.Language.Analysis;
using KScript.Language.Features;

namespace KScript.Language.Tests
{
    public class CallListValueCompletionTests
    {
        private static readonly LanguageService Service = new LanguageService(files: EmptyFileSystem.Instance);
        private const string Modes = "enum Mode { quiet, loud }\nfn run(label: string, modes: Mode[]) => $modes\n";

        private static List<CompletionItem> Complete(string marked, string trigger = null)
        {
            (string text, int offset) = TestHelpers.WithCaret(marked);
            return Service.GetCompletions(Service.Analyze(text, "main.kscript"), offset, trigger);
        }

        [Theory]
        [InlineData("@run('a', [‸])")]
        [InlineData("@run(modes: [quiet, ‸], label: 'a')")]
        [InlineData("@run(label: 'a', [‸])")]
        [InlineData("@run(label: 'a', [‸")]
        [InlineData("'a' |> @run([‸])")]
        public void List_items_use_the_type_of_the_bound_parameter(string call)
        {
            Assert.Contains(Complete(Modes + "echo " + call), i => i.Kind == CompletionKind.Value && i.Label == "loud");
        }

        [Theory]
        [InlineData("@run('a', [[‸]])")]
        [InlineData("@run('a', [[1, ‸]])")]
        [InlineData("@run('a', [1][‸])")]
        public void An_outer_lists_item_type_does_not_leak_into_another_expression(string call)
        {
            Assert.DoesNotContain(Complete(Modes + "echo " + call), i => i.Kind == CompletionKind.Value && i.Label == "loud");
        }

        [Fact]
        public void Completing_a_native_list_item_replaces_the_whole_value_in_argument_syntax()
        {
            const string code = "fn zones(values: timezone[]) => $values\necho @zones(values: [UTC, Europe/Lo‸nd])";
            (string text, _) = TestHelpers.WithCaret(code);
            CompletionItem london = Assert.Single(Complete(code), i => i.Label == "Europe/London");
            Assert.Equal("'Europe/London'", london.InsertText);
            Assert.Equal("Europe/Lond", text.Substring(london.ReplaceSpan.Start, london.ReplaceSpan.Length));
        }

        [Fact]
        public void An_optional_boolean_list_offers_its_item_values()
        {
            List<CompletionItem> items = Complete("fn flags(values: bool[]?) => $values\necho @flags([true,‸])", ",");
            Assert.Contains(items, i => i.Kind == CompletionKind.Value && i.Label == "false" && i.InsertText == " false");
        }

        [Theory]
        [InlineData("fn choose(name: string, market: Market) => $market\necho @choose(name: 'a', ‸)")]
        [InlineData("fn choose(market: Market) => $market\necho @choose(market: ‸)")]
        [InlineData("fn choose(markets: Market[]) => $markets\necho @choose(markets: [‸])")]
        public void A_user_functions_scalar_and_list_choices_follow_the_refinements_allowed_values(string call)
        {
            List<CompletionItem> items = Complete("type Market with [country] { one_of: [GB, IE] }\n" + call);
            Assert.Contains(items, i => i.Label == "GB" && i.Kind == CompletionKind.Value);
            Assert.Contains(items, i => i.Label == "IE" && i.Kind == CompletionKind.Value);
            Assert.DoesNotContain(items, i => i.Label == "FR" && i.Kind == CompletionKind.Value);
        }

        [Theory]
        [InlineData("fn pick(v: Tag) => $v\necho @pick(‸)")]
        [InlineData("fn pick(v: Tag) => $v\necho @pick(v: ‸)")]
        [InlineData("fn pick(v: Tag[]) => $v\necho @pick([‸])")]
        public void A_text_choice_named_null_inserts_a_string_expression(string call)
        {
            const string declaration = "type Tag with [string] { one_of: ['null', 'new'] }\n";
            Assert.Contains(Complete(declaration + call), i => i.Kind == CompletionKind.Value && i.Label == "null" && i.InsertText == "'null'");
        }
    }
}
