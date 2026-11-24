using KScript.Language.Features;

namespace KScript.Language.Tests
{
    public class LiveAttributeCompletionTests
    {
        private static readonly string Root = Path.Combine(Path.GetTempPath(), "kscript-live-attributes");
        private static readonly LanguageService Service = new(files: new MemoryFiles()
            .Add(Path.Combine(Root, "_components", "card.html"), "<prop name=\"title\" required />"));

        private static List<CompletionItem> At(string markup, string trigger = null, bool fenced = false, string file = "index.html")
        {
            if (fenced)
            {
                file = "_components/board.kscript";
                markup = "import KScript.Web\ntype Board extends Component {\n  fn render(): Component {\n    return ```html\n    " + markup + "\n    ```\n  }\n}";
            }
            var (text, offset) = TestHelpers.WithCaret(markup);
            return Service.GetCompletions(Service.Analyze(text, Path.Combine(Root, file)), offset, trigger);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Completes_named_action_at_the_sigil_and_preserves_existing_handler(bool fenced)
        {
            var item = Assert.Single(At("<live id=\"board\"><span hidden @‸></span></live>", "@", fenced), i => i.Label == "@live-action");
            Assert.Equal("@live-action", item.Label);
            Assert.Equal(1, item.ReplaceSpan.Length);
            Assert.Contains("@live-action('${1:name}') @on-action(", item.InsertText);
            var existing = Assert.Single(At("<live id=\"board\"><span @live-a‸ on-action=\"@move()\"></span></live>", fenced: fenced));
            Assert.Equal("@live-action('${1:name}')", existing.InsertText);
            Assert.Equal("@live-a".Length, existing.ReplaceSpan.Length);
            Assert.DoesNotContain(At("<live id=\"board\"><span live-action=\"move\" on-action=\"@move()\" @‸></span></live>", "@", fenced), i => i.Label == "@live-action");
            Assert.Empty(At("<live id=\"board\"><span @live-action('move-‸') on-action=\"@move()\"></span></live>", fenced: fenced));
        }

        [Fact]
        public void Offers_attributes_in_live_scope_and_on_eligible_elements()
        {
            var span = At("<span ‸></span>", " ", fenced: true);
            Assert.Contains(span, i => i.Label == "@on-action");
            Assert.Contains(span, i => i.Label == "@live-action");
            Assert.DoesNotContain(span, i => i.Label is "@bind" or "@bind-on" or "@live-upload" or "@live-portal" or "@on-submit");
            Assert.Contains(At("<template ‸></template>", " ", file: "_components/card.html"), i => i.Label == "@live-portal");
            Assert.DoesNotContain(At("<span ‸></span>", " "), i => i.Label is "@live-action" or "@on-action");
            Assert.Contains(At("<a href=\"/\" live-n‸>Home</a>"), i => i.Label == "live-navigate");
            var invocation = At("<card ‸ />", " ", fenced: true);
            Assert.Contains(invocation, i => i.Label == "title");
            Assert.Contains(invocation, i => i.Label == "@live-id");
            Assert.DoesNotContain(invocation, i => i.Label == "@live-action");
            Assert.DoesNotContain(At("<span title=\"name @‸\"></span>", "@", fenced: true), i => i.Label == "@live-action");
            Assert.DoesNotContain(At("<span>hello @‸</span>", "@", fenced: true), i => i.Label == "@live-action");
            Assert.DoesNotContain(At("<input type=\"FILE\" ‸>", " ", fenced: true), i => i.Label == "@bind");
            Assert.DoesNotContain(At("<span draggable=\"false\" ‸></span>", " ", fenced: true), i => i.Label == "@drag-value");
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Binding_values_are_scoped_and_binding_modes_are_exhaustive(bool fenced)
        {
            const string begin = "<live id=\"board\"><state name=\"draft\" value=\"''\" /><state name=\"secret\" locked value=\"0\" />";
            Assert.Equal(new[] { "draft" }, At(begin + "<input bind=\"‸\"></live>", "\"", fenced).Select(i => i.Label));
            Assert.Equal("'draft'", Assert.Single(At(begin + "<input @bind(‸)></live>", "(", fenced)).InsertText);
            Assert.Equal("'draft'", Assert.Single(At(begin + "<input @bind(dr‸)></live>", fenced: fenced)).InsertText);
            Assert.Equal(new[] { "input", "change", "blur", "action" },
                At(begin + "<input bind=\"draft\" bind-on=\"‸\"></live>", "\"", fenced).Select(i => i.Label));
            Assert.Equal(new[] { "inner" }, At(begin + "<live id=\"child\"><state name=\"inner\" value=\"1\" /><input bind=\"‸\"></live></live>",
                "\"", fenced).Select(i => i.Label));
            Assert.Empty(At(begin + "<input bind-on=\"‸\"></live>", "\"", fenced));
            var invalid = Service.Analyze(begin + "<input bind=\"draft\" bind-on=\"click\"></live>", Path.Combine(Root, "index.html"));
            Assert.Contains(invalid.Errors, e => e.Message.Contains("bind-on") && e.Message.Contains("change"));
        }

        [Fact]
        public void Class_bindings_use_declared_stored_fields_and_fix_typos_in_place()
        {
            const string editor = """
                import KScript.Web
                type NoteEditor extends Component {
                  title: string
                  done: bool
                  caption: string => $title
                  fn render(): Component {
                    return ```html
                    <input @bind('titel') @bind-on('blur')>
                    ```
                  }
                }
                """;
            var doc = Service.Analyze(editor, Path.Combine(Root, "_components", "note-editor.kscript"));
            var error = Assert.Single(doc.Errors, e => doc.Source[e.Span] == "titel");
            Assert.Contains("Did you mean 'title'", error.Message);
            Assert.Equal("title", Assert.Single(Assert.Single(error.Fixes).Changes).NewText);
            var empty = Service.Analyze(editor.Replace("titel", ""), doc.Path);
            var items = Service.GetCompletions(empty, empty.Source.Text.IndexOf("@bind('", StringComparison.Ordinal) + 7, "'");
            Assert.Equal(new[] { "done", "title" }, items.Select(i => i.Label).OrderBy(n => n));
            Assert.Empty(Service.Analyze(editor.Replace("titel", "title"), doc.Path).Errors);
        }
    }
}
