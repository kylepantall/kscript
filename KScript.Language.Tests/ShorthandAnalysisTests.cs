using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Syntax;

namespace KScript.Language.Tests
{
    /// <summary>A handler's optional named shorthands (toggle:, call:, increment:, set:, add:, remove:) in the checker.</summary>
    public sealed class ShorthandAnalysisTests : IDisposable
    {
        private readonly string folder = Path.Combine(Path.GetTempPath(), "kscript-shorthands-" + Guid.NewGuid().ToString("N"));
        private readonly LanguageService service = new LanguageService();

        public ShorthandAnalysisTests() => Directory.CreateDirectory(Path.Combine(folder, "_components"));

        public void Dispose() => Directory.Delete(folder, recursive: true);

        private const string Board = """
            import KScript.Web

            type TagBoard extends Component {
              open: bool = false
              count: int = 0
              tab: string = 'all'
              draft: string = ''
              tags: string[] = []

              fn save(note: string) {
                draft = $note
              }

              fn clear() {
                draft = ''
              }

              fn render() => ```html
              <p>{$open} {$count} {$tab} {$draft} {$tags->join(', ')}</p>
              HANDLERS
              ```
            }
            """;

        private DocumentAnalysis Check(string handlers)
        {
            string text = Board.Replace("HANDLERS", handlers.Replace("\n", "\n  "));
            string path = Path.Combine(folder, "_components", "tag-board.kscript");
            File.WriteAllText(path, text);
            return TestHelpers.WithoutUnused(service.Analyze(text, path));
        }

        private static string Messages(DocumentAnalysis a) => string.Join("\n", a.Diagnostics.Select(d => d.Code + " " + d.Message));

        [Fact]
        public void Every_action_checks_clean_on_a_field_of_its_type()
        {
            TestHelpers.AssertClean(Check("""
                <button @on-click(toggle: $open)>a</button>
                <button @on-click(call: $clear)>b</button>
                <button @on-click(call: $save, with: ['hi'])>c</button>
                <button @on-click(increment: $count)>d</button>
                <button @on-click(increment: $count, by: 5)>e</button>
                <button @on-click(decrement: $count, by: 2)>f</button>
                <button @on-click(set: $tab, to: 'notes')>g</button>
                <button @on-click(add: $draft, to: $tags, set: $draft, to: '')>h</button>
                <button @on-click(remove: 'x', from: $tags)>i</button>
                <button @on-click(toggle: $open, increment: $count)>j</button>
                <button on-click="toggle: $open">k</button>
                """));
        }

        [Theory]
        [InlineData("toggle: $tab", "KS5005 toggle: works on a bool field, and $tab is string.")]
        [InlineData("increment: $tab", "KS5005 increment: works on a number field, and $tab is string.")]
        [InlineData("add: 'x', to: $tab", "KS5005 add: works on a list field, and $tab is string.")]
        [InlineData("set: $tab", "KS2003 set: needs to: as well.")]
        [InlineData("toggle: $open, by: 2", "KS2003 by: goes straight after increment: or decrement:, and only once.")]
        [InlineData("increment: $count, by: 2, by: 3", "KS2003 by: goes straight after increment: or decrement:, and only once.")]
        [InlineData("explode: $open", "KS2003 A handler has no 'explode:' shorthand. It takes toggle:, call:, increment:, decrement:, set:, add:, remove:, a function to call, or statements.")]
        [InlineData("toggle: $open, count += 1", "Shorthands and statements can't be mixed in one handler.")]
        [InlineData("call: $tab", "KS5005 call: needs a function or one of the component's methods, and $tab is string.")]
        [InlineData("toggle: $nope", "$nope is not defined")]
        [InlineData("call: $nope", "$nope is not defined")]
        [InlineData("call: $save, with: [$nope]", "$nope is not defined")]
        public void A_mistake_in_a_shorthand_is_reported(string handler, string message)
        {
            Assert.Contains(message, Messages(Check($"<button @on-click({handler})>x</button>")));
        }

        [Fact]
        public void A_misspelt_action_offers_the_right_name()
        {
            string text = Board.Replace("HANDLERS", "<button @on-click(incremnt: $count)>x</button>");
            DocumentAnalysis analysis = Check("<button @on-click(incremnt: $count)>x</button>");
            Diagnostic problem = Assert.Single(analysis.Diagnostics);
            Assert.Equal("incremnt", text.Substring(problem.Span.Start, problem.Span.Length));
            Assert.Equal("Change to increment", Assert.Single(problem.Fixes).Title);
        }

        [Fact]
        public void A_field_a_shorthand_changes_counts_as_written()
        {
            // count is only ever changed by the handler, so it isn't reported as never changed or unused.
            DocumentAnalysis analysis = Check("<button @on-click(increment: $count)>x</button>");
            Assert.DoesNotContain(analysis.Diagnostics, d => d.Message.Contains("count", StringComparison.Ordinal));
        }
    

        [Fact]
        public void Class_switches_check_clean_in_both_forms()
        {
            TestHelpers.AssertClean(Check("""
                <li class="item" @class(open: $open, 'is-busy': $count > 3)>a</li>
                <li @class({ $open: ['open', 'wide'], [$open, $count > 1]: 'both', $open or $count > 9: 'any' })>b</li>
                <li @class({ $open: $tab })>c</li>
                """));
        }

        [Theory]
        [InlineData("<li @class(open: $tab)>x</li>", "KS5005 @class: $tab must be true or false (or a list of them), and it is string.")]
        [InlineData("<li @class({ [$open, $tab]: 'x' })>x</li>", "KS5005 @class: [$open, $tab] must be true or false (or a list of them), and it is string.")]
        [InlineData("<li @class({ $open: 'not a class' })>x</li>", "KS3006 @class: 'not a class' isn't a class name.")]
        [InlineData("<li @class(open)>x</li>", "KS3006 @class: Each entry is a class and a condition, such as done: $todo.done.")]
        [InlineData("<li @class({ $nope: 'x' })>x</li>", "$nope is not defined")]
        public void A_mistake_in_a_class_switch_is_reported(string tag, string message)
        {
            Assert.Contains(message, Messages(Check(tag)));
        }
    

        private (List<CompletionItem> Items, HoverResult Hover) At(string handlers)
        {
            (string text, int offset) = TestHelpers.WithCaret(Board.Replace("HANDLERS", handlers));
            string path = Path.Combine(folder, "_components", "tag-board.kscript");
            File.WriteAllText(path, text);
            DocumentAnalysis analysis = service.Analyze(text, path);
            return (service.GetCompletions(analysis, offset), service.GetHover(analysis, offset));
        }

        [Fact]
        public void Completion_offers_the_shorthands_and_the_companion_that_goes_next()
        {
            List<string> start = At("<button @on-click(\u2038)>x</button>").Items.Select(i => i.Label).ToList();
            Assert.Contains("toggle:", start);
            Assert.Contains("increment:", start);

            List<string> next = At("<button @on-click(increment: $count, \u2038)>x</button>").Items.Select(i => i.Label).ToList();
            Assert.Equal("by:", next[0]);
            Assert.Contains("toggle:", next);
            Assert.DoesNotContain("open", next);

            Assert.Equal("to:", At("<button @on-click(set: $tab, t\u2038)>x</button>").Items[0].Label);
            Assert.DoesNotContain(At("<button @on-click(count += 1, \u2038)>x</button>").Items, i => i.Label == "toggle:");
        }

        [Fact]
        public void Hover_on_a_shorthand_shows_what_it_stands_for()
        {
            HoverResult hover = At("<button @on-click(add: $draft, t\u2038o: $tags)>x</button>").Hover;
            Assert.Contains("**add:** (handler shorthand)", hover.Markdown);
            Assert.Contains("Same as `tags = $tags->push($draft)`.", hover.Markdown);
            Assert.Contains("Same as `open = not $open`", At("<button @on-click(tog\u2038gle: $open)>x</button>").Hover.Markdown);
            Assert.DoesNotContain("Same as", At("<button @on-click(ca\u2038ll: $clear)>x</button>").Hover.Markdown);
        }
    }
}
