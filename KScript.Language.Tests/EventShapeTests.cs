using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Pages;

namespace KScript.Language.Tests
{
    /// <summary>
    /// $event in handlers is typed from what the handler gets (EventFields), $event.data in an on-event listener from
    /// the matching @emit_event calls, and built-in values' fields show with their types on hover.
    /// </summary>
    public sealed class EventShapeTests : IDisposable
    {
        private readonly string folder = Path.Combine(Path.GetTempPath(), "kscript-event-shapes-" + Guid.NewGuid().ToString("N"));
        private readonly LanguageService service = new LanguageService();

        public EventShapeTests()
        {
            Directory.CreateDirectory(Path.Combine(folder, "_components"));
            File.WriteAllText(Path.Combine(folder, "kscript.json"), "{}");
            EmittedEvents.ClearCache();
        }

        public void Dispose() => Directory.Delete(folder, recursive: true);

        private const string Component = """
            import KScript.Web

            type NAME extends Component {
              deleted_id: int = 0
              title: string = ''
              moved: list = []

              fn render() => ```html
              <p>{$deleted_id} {$title}</p>
              MARKUP
              ```
            }
            """;

        private (DocumentAnalysis Analysis, string Text) Write(string name, string markup)
        {
            string text = Component.Replace("NAME", name).Replace("MARKUP", markup.Replace("\n", "\n  "));
            string path = Path.Combine(folder, "_components", name.ToLowerInvariant() + ".kscript");
            File.WriteAllText(path, text);
            return (TestHelpers.WithoutUnused(service.Analyze(text, path)), text);
        }

        private string Hover((DocumentAnalysis Analysis, string Text) file, string at, int skip = 0)
        {
            int offset = file.Text.IndexOf(at, StringComparison.Ordinal) + skip;
            Assert.True(offset >= skip, $"'{at}' is not in the file.");
            return service.GetHover(file.Analysis, offset)?.Markdown;
        }

        private List<string> FieldsAfter((DocumentAnalysis Analysis, string Text) file, string at)
        {
            int offset = file.Text.IndexOf(at, StringComparison.Ordinal) + at.Length;
            return service.GetCompletions(file.Analysis, offset, ".").Select(c => c.Label).ToList();
        }

        [Fact]
        public void Event_data_takes_the_type_of_what_the_matching_emit_sends()
        {
            Write("NoteCard", "<button @on-click(@emit_event('note-deleted', data: 42))>Delete</button>");
            var board = Write("Board", "<span hidden @on-event(deleted_id = $event.data) event-name=\"note-deleted\"></span>");

            TestHelpers.AssertClean(board.Analysis);
            string data = Hover(board, "$event.data", "$event.".Length + 1);
            Assert.Contains(".data: number", data);
            Assert.Contains("What @emit_event sends with 'note-deleted': from _components/notecard.kscript (line", data);
        }

        [Fact]
        public void Event_data_from_several_emits_and_from_this_file_is_listed()
        {
            Write("NoteCard", "<button @on-click(@emit_event('note-deleted', 'first'))>Delete</button>");
            var board = Write("Board", """
                <span hidden @on-event(title = $event.data) event-name="note-deleted"></span>
                <button @on-click(@emit_event('note-deleted', data: 'again'))>Again</button>
                """);

            string data = Hover(board, "$event.data", "$event.".Length + 1);
            Assert.Contains(".data: string", data);
            Assert.Contains("this file (line", data);
            Assert.Contains("_components/notecard.kscript (line", data);
        }

        [Fact]
        public void Event_data_says_when_nothing_emits_the_event()
        {
            var board = Write("Board", "<span hidden @on-event(title = $event.data) event-name=\"nobody-sends-this\"></span>");

            string data = Hover(board, "$event.data", "$event.".Length + 1);
            Assert.Contains("No @emit_event call for 'nobody-sends-this' was found", data);
        }

        [Fact]
        public void An_emit_without_data_gives_null()
        {
            Write("NoteCard", "<button @on-click(@emit_event('note-deleted'))>Delete</button>");
            var board = Write("Board", "<span hidden @on-event(title = $event.data ?? $event.name) event-name=\"note-deleted\"></span>");

            Assert.Contains(".data: null", Hover(board, "$event.data", "$event.".Length + 1));
            Assert.Contains(".name: string", Hover(board, "$event.name", "$event.".Length + 1));
        }

        [Fact]
        public void Hovering_event_shows_its_type_and_fields_without_losing_words()
        {
            var board = Write("Board", "<button @on-click(title = $event.key)>Go</button>");

            string hover = Hover(board, "$event.key", 1);
            Assert.Contains("built-in $event: event", hover);
            Assert.Contains("The browser event this `on-click` handler runs for.", hover);
            Assert.Contains("`key: string`", hover);
            Assert.Contains("`checked: bool`", hover);
            Assert.DoesNotContain("`data", hover);
            Assert.Contains("For a key event: the key pressed", Hover(board, "$event.key", "$event.".Length + 1));
        }

        [Fact]
        public void Each_handler_offers_only_the_fields_its_event_has()
        {
            var board = Write("Board", """
                <ul @on-sort(moved = $event.order)><li sort-item="a">a</li></ul>
                <button @on-click(title = $event.key)>Go</button>
                <span hidden @on-event(title = $event.source)></span>
                """);

            List<string> sort = FieldsAfter(board, "moved = $event.");
            Assert.Contains("order", sort);
            Assert.Contains("item", sort);
            Assert.DoesNotContain("data", sort);
            Assert.Contains("list<string>", Hover(board, "$event.order", "$event.".Length + 1));

            List<string> click = FieldsAfter(board, "title = $event.");
            Assert.Contains("key", click);
            Assert.DoesNotContain("order", click);
            Assert.DoesNotContain("data", click);

            Assert.Contains("data", FieldsAfter(board, "@on-event(title = $event."));
        }

        [Fact]
        public void A_field_the_event_does_not_have_is_reported()
        {
            var board = Write("Board", "<button @on-click(title = $event.dta)>Go</button>");
            Assert.Contains(board.Analysis.Diagnostics, d => d.Message.StartsWith("event has no field 'dta'", StringComparison.Ordinal));
        }

        [Fact]
        public void The_event_table_matches_what_live_js_sends()
        {
            string live = File.ReadAllText(Path.Combine(FormatterTests.RepoRoot(), "KScript.Web", "Pages", "live.js"))
                + File.ReadAllText(Path.Combine(FormatterTests.RepoRoot(), "KScript.Web", "Pages", "live-drag.js"));
            IEnumerable<string> names = EventFields.Common
                .Concat(EventFields.HandlersWithExtraFields.SelectMany(h => EventFields.ForPage(h)))
                .Select(f => f.Name).Distinct().Where(n => n != "files");
            foreach (string name in names)
            {
                Assert.True(live.Contains("eventInfo." + name, StringComparison.Ordinal) || live.Contains(" " + name + ":", StringComparison.Ordinal)
                    || live.Contains(", " + name + " ", StringComparison.Ordinal) || live.Contains(", " + name + ",", StringComparison.Ordinal),
                    $"$event.{name} is described for the editor, but live.js doesn't send it.");
            }
        }

        [Fact]
        public void Os_and_script_describe_their_fields()
        {
            string text = "echo $os.name\necho $script.dir\n";
            string path = Path.Combine(folder, "main.kscript");
            DocumentAnalysis analysis = service.Analyze(text, path);
            Assert.Contains("windows, macos or linux", service.GetHover(analysis, text.IndexOf("name", StringComparison.Ordinal))?.Markdown);
            Assert.Contains("The folder this script is in.", service.GetHover(analysis, text.IndexOf("dir", StringComparison.Ordinal))?.Markdown);
            Assert.Contains("temp", service.GetCompletions(analysis, text.IndexOf("name", StringComparison.Ordinal), ".").Select(c => c.Label));
        }
    }
}
