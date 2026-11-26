using KScript.Language.Analysis;
using KScript.Language.Features;

namespace KScript.Language.Tests
{
    /// <summary>@focus(), @autogrow() and @pending(...) in the checker.</summary>
    public sealed class BrowserDirectiveAnalysisTests : IDisposable
    {
        private readonly string folder = Path.Combine(Path.GetTempPath(), "kscript-browser-directives-" + Guid.NewGuid().ToString("N"));
        private readonly LanguageService service = new LanguageService();

        public BrowserDirectiveAnalysisTests() => Directory.CreateDirectory(Path.Combine(folder, "_components"));

        public void Dispose() => Directory.Delete(folder, recursive: true);

        private const string Note = """
            import KScript.Web

            type NoteCard extends Component {
              editing: bool = false
              title: string = ''

              fn delete() {
                title = ''
              }

              fn render() => ```html
              <p>{$editing} {$title}</p>
              MARKUP
              ```
            }
            """;

        private DocumentAnalysis Check(string markup)
        {
            string text = Note.Replace("MARKUP", markup.Replace("\n", "\n  "));
            string path = Path.Combine(folder, "_components", "note-card.kscript");
            File.WriteAllText(path, text);
            return TestHelpers.WithoutUnused(service.Analyze(text, path));
        }

        private static string Messages(DocumentAnalysis a) => string.Join("\n", a.Diagnostics.Select(d => d.Code + " " + d.Message));

        [Fact]
        public void The_directives_check_clean()
        {
            TestHelpers.AssertClean(Check("""
                <input @focus()>
                <input @focus($editing)>
                <input focus="{$editing}">
                <textarea @autogrow()></textarea>
                <article @pending('crumple', for: 'delete')>
                  <button @on-click($delete)>Delete</button>
                </article>
                <p @pending(['busy', 'dim'])>Saving</p>
                """));
        }

        [Theory]
        [InlineData("<input @focus($title)>", "KS5005 @focus: $title must be true or false, and it is string.")]
        [InlineData("<div @autogrow()></div>", "Use @autogrow() on a textarea.")]
        [InlineData("<p @pending()>x</p>", "@pending needs a class")]
        [InlineData("<p @pending('not a class')>x</p>", "@pending: 'not a class' isn't a class name.")]
        [InlineData("<p @pending('busy', when: 'delete')>x</p>", "@pending: @pending has no when: option.")]
        [InlineData("<p @pending('busy', for: $delete)>x</p>", "@pending: for: takes the method's name in quotes, for: 'delete'.")]
        [InlineData("<p @pending(for: 'delete')>x</p>", "@pending needs a class before for:")]
        public void A_mistake_is_reported(string markup, string message)
        {
            Assert.Contains(message, Messages(Check(markup)));
        }

        [Fact]
        public void Completion_offers_the_directives_after_an_at()
        {
            (string text, int offset) = TestHelpers.WithCaret(Note.Replace("MARKUP", "<textarea @‸></textarea>"));
            string path = Path.Combine(folder, "_components", "note-card.kscript");
            File.WriteAllText(path, text);
            List<CompletionItem> items = service.GetCompletions(service.Analyze(text, path), offset);
            Assert.Contains(items, i => i.Label.Contains("focus", StringComparison.Ordinal));
            Assert.Contains(items, i => i.Label.Contains("autogrow", StringComparison.Ordinal));
            Assert.Contains(items, i => i.Label.Contains("pending", StringComparison.Ordinal));
        }
    }
}
