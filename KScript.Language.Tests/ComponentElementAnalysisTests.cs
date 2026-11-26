using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Syntax;
using KScript.Syntax.Markup;

namespace KScript.Language.Tests
{
    /// <summary>Components drawn as statements, and a render for each place: the checker, the parser and the converter.</summary>
    public sealed class ComponentElementAnalysisTests : IDisposable
    {
        private readonly string folder = Path.Combine(Path.GetTempPath(), "kscript-surfaces-" + Guid.NewGuid().ToString("N"));
        private readonly LanguageService service = new LanguageService();

        public ComponentElementAnalysisTests() => Directory.CreateDirectory(Path.Combine(folder, "components"));

        public void Dispose() => Directory.Delete(folder, recursive: true);

        private string Write(string name, string text)
        {
            string path = Path.Combine(folder, name);
            File.WriteAllText(path, text);
            return path;
        }

        private DocumentAnalysis Check(string name, string text) => service.Analyze(text, Write(name, text));

        private static string Messages(DocumentAnalysis a) => string.Join("\n", a.Diagnostics.Select(d => d.Code + " " + d.Message));

        private const string NoteCard = """
            import KScript.UI

            type NoteCard extends Component {
              construct({ title: string, body: string = '', tags: Tag[] })

              open: bool

              fn render() {
                Box(padding: 8) {
                  Text "{$title}"
                  Button(on_click: $open) "Show"
                  if $open {
                    Text "{$body}"
                    slot
                  }
                }
              }

              fn render(terminal) {
                echo "* {$title}: {$body}"
                slot
              }
            }

            """;

        private const string Window = """
            import KScript.UI
            import "components/note-card.kscript"

            Window(title: "Notes") {
              notes = ['Milk', 'Eggs']

              for n in $notes {
                NoteCard(title: $n, id: $n) {
                  Text "inside {$n}"
                }
              }
            }

            """;

        [Fact]
        public void A_window_and_a_script_draw_an_imported_component()
        {
            Write("components/note-card.kscript", NoteCard.Replace(", tags: Tag[]", ""));
            Assert.Equal(string.Empty, Messages(Check("components/note-card.kscript", NoteCard.Replace(", tags: Tag[]", ""))));
            Assert.Equal(string.Empty, Messages(Check("window.kscript", Window)));
            Assert.Equal(string.Empty, Messages(Check("terminal.kscript", "import \"components/note-card.kscript\"\n\nNoteCard(title: 'Milk') {\n  echo \"inside\"\n}\n")));
            // The import is used, and the name goes to the component.
            DocumentAnalysis window = Check("window.kscript", Window);
            Assert.Contains(window.References, r => r.Symbol.Name == "NoteCard" && Window.Substring(r.Span.Start, r.Span.Length) == "NoteCard");
        }

        [Fact]
        public void What_a_component_is_passed_is_checked()
        {
            Write("components/note-card.kscript", NoteCard.Replace("tags: Tag[]", "count: Size").Replace("type NoteCard", "enum Size { small, large }\n\ntype NoteCard"));
            string messages = Messages(Check("window.kscript", Window.Replace("id: $n", "bodi: 'x', open: true, body: $missing")));
            Assert.Contains("KS5007 NoteCard has no input 'bodi'. NoteCard takes: title, body, count.", messages);
            Assert.Contains("KS5007 'open' is the component's own state and can't be passed in. Its construct block lists what it takes.", messages);
            Assert.Contains("Pass the required 'count' to NoteCard.", messages);
            Assert.Contains("missing", messages);
            Write("components/empty.kscript", "import KScript.UI\n\ntype Gapper extends Component {\n  fn render() {\n    Spacer(size: 4)\n  }\n}\n");
            Assert.Contains("KS5007 Gapper has no input 'size'. Gapper takes nothing.",
                Messages(Check("rule.kscript", "import KScript.UI\nimport \"components/empty.kscript\"\n\nWindow(title: \"R\") {\n  Gapper(size: 2)\n}\n")));
        }

        private const string Parts = """
            import KScript.Document

            type Finding extends Component {
              construct({ title: string, count: int })

              fn render() => ```md
                # {$title}

                We saw **{$count}** of them, and \( x^{2} \) holds.
                ```
            }

            type Method extends Component {
              construct({ name: string })

              fn render() {
                note = "50% & more"
                return ```latex
                  \section{Method: {$name}}
                  We used \textbf{$name} with { $note } and $x^{2}$ {as written}, {$open and {@name(.
                  ```
              }
            }


            """;

        [Fact]
        public void A_render_that_returns_markdown_or_latex_draws_in_a_document()
        {
            const string document = "Document(into: doc) {\n  Finding(title: 'Gliders', count: 3)\n  Method(name: 'counting')\n}\necho $doc->markdown()\n";
            // The values in both blocks are read, so $note counts as used and nothing is unknown.
            Assert.Equal(string.Empty, Messages(Check("report.kscript", Parts + document)));
            Assert.Contains("KS3006 Finding has no render for the terminal.", Messages(Check("report.kscript", Parts + "Finding(title: 'Gliders', count: 3)\n")));

            string wrong = Messages(Check("report.kscript", Parts.Replace("{$count}", "{$cuont}").Replace("{ $note }", "{ $nite }") + document));
            Assert.Contains("$cuont is not defined", wrong);
            Assert.Contains("$nite is not defined", wrong);
            Assert.Contains("KS6003 The variable $note is never used.", wrong);

            // A block of another kind doesn't show where it draws.
            Assert.Contains("Finding's render doesn't show where it draws", Messages(Check("report.kscript", Parts.Replace("```md", "```yaml") + document)));
        }

        [Fact]
        public void Only_a_group_that_holds_one_value_is_a_value_in_latex()
        {
            var latex = DecodedText.Plain("\\textbf{$name} {  @today() } x^{2} {$a$} {$map[{a: 1}]} {$open {@", 10);
            var values = ComponentScript.LatexValues(latex).ToList();
            Assert.Equal(new[] { "$name", "  @today() ", "$map[{a: 1}]" }, values.Select(v => v.Value.Value));
            Assert.Equal(('{', '}'), (latex.Value[values[0].Open], latex.Value[values[0].Close]));
            Assert.Equal(10 + values[0].Open + 1, values[0].Value.SourceSpan.Start);
        }

        [Fact]
        public void A_component_drawn_where_it_has_no_render_is_reported()
        {
            const string only = "import KScript.UI\n\ntype Tag extends Component {\n  label: string\n\n  fn render() {\n    Text \"{$label}\"\n  }\n}\n\n";
            Assert.Contains("KS3006 Tag has no render for the terminal. Add one to the component: fn render(terminal) { }.", Messages(Check("tag.kscript", only + "Tag(label: 'x')\n")));
            Assert.Equal(string.Empty, Messages(Check("tag.kscript", only + "Window(title: \"T\") {\n  Tag(label: 'x')\n}\n")));
            // A function draws wherever it is called from, and a render draws where it says.
            Assert.Equal(string.Empty, Messages(Check("tag.kscript", only + "fn row() {\n  Tag(label: 'x')\n}\n\nWindow(title: \"T\") {\n  @row()\n}\n")));
            string nested = only + "type Tags extends Component {\n  fn render(terminal) {\n    Tag(label: 'x')\n  }\n}\n";
            Assert.Contains("Tag has no render for the terminal", Messages(Check("tag.kscript", nested)));
            Assert.Equal(string.Empty, Messages(Check("tag.kscript", nested.Replace("render(terminal)", "render(window)"))));
        }

        [Fact]
        public void A_render_shows_or_names_where_it_draws()
        {
            const string wrap = "type Wrap extends Component {\n  fn render() {\n    if true {\n      slot\n    }\n  }\n}\n";
            Assert.Contains("KS3006 Wrap's render doesn't show where it draws. Name it: fn render(window), fn render(terminal) or fn render(document).", Messages(Check("wrap.kscript", wrap)));
            Assert.Equal(string.Empty, Messages(Check("wrap.kscript", wrap.Replace("render()", "render(window)"))));
            Assert.Contains("KS4004 Wrap has two render methods for a window. Keep one.",
                Messages(Check("wrap.kscript", "import KScript.UI\n\n" + wrap.Replace("render()", "render(window)").Replace("}\n}\n", "}\n\n  fn render() {\n    Spacer(size: 1)\n  }\n}\n"))));
            // A page's render needs no return type: its html block shows what it is.
            const string page = "import KScript.Web\n\ntype Hello extends Component {\n  name: string\n\n  fn render() {\n    return ```html\n    <p>Hello, {$name} {$nope}</p>\n    ```\n  }\n\n  override fn style() => ```css\n  p { color: teal; }\n  ```\n}\n";
            Assert.Contains("nope", Messages(Check("hello.kscript", page)));
            Assert.Equal(string.Empty, Messages(Check("hello.kscript", page.Replace(" {$nope}", ""))));
            // A block that is neither a page nor a document doesn't show a place, and a component with no render at all still needs one.
            Assert.Contains("doesn't show where it draws", Messages(Check("yaml.kscript", "type Yaml extends Component {\n  fn render() => ```yaml\n  a: 1\n  ```\n}\n")));
            Assert.Contains("must implement fn render(): Component to extend Component", Messages(Check("none.kscript", "type None extends Component {\n  name: string\n}\n")));
        }

        [Fact]
        public void A_slot_belongs_in_a_render()
        {
            Assert.Equal("Card has no render for a document. Add one to the component: fn render(document) { }.", ComponentScript.NoRenderFor("Card", ComponentScript.Document));
            Assert.Contains("KS3007 slot only works in a component's render, where it draws what was written inside the component.", Messages(Check("slot.kscript", "slot\n")));
        }

        [Fact]
        public void The_converter_writes_surfaces_and_components_in_both_formats()
        {
            string source = NoteCard.Replace(", tags: Tag[]", "").ReplaceLineEndings("\n");
            string xml = ScriptConverter.Convert(source, "note-card.kscript").Text;
            Assert.Contains("    <fn name=\"render\" surface=\"terminal\">", xml);
            Assert.Contains("          <slot />", xml);
            string code = ScriptConverter.Convert(xml, "note-card.ks.xml").Text;
            Assert.Contains("  fn render(terminal) {\n    echo \"* {$title}: {$body}\"\n    slot\n  }\n", code);
            Assert.Contains("  fn render() {\n    Box(padding: 8) {\n", code);

            string window = ScriptConverter.Convert(Window.ReplaceLineEndings("\n"), "window.kscript").Text;
            Assert.Contains("    <NoteCard title=\"{$n}\" id=\"{$n}\">", window);
            Assert.Contains("    NoteCard(title: $n, id: $n) {\n", ScriptConverter.Convert(window, "window.ks.xml").Text);
            // Text, and a value that is more than one name, stay as they are written.
            string mixed = ScriptConverter.Convert("<kscript><Card title=\"Milk\" note=\"{$a} and {$b}\" count=\"{3}\" /></kscript>", "c.ks.xml").Text;
            Assert.Contains("Card(title: \"Milk\", note: \"{$a} and {$b}\", count: \"{3}\")", mixed);
            // A surface on a function that isn't a component's render is kept in the long form.
            Assert.DoesNotContain("fn other(window)", ScriptConverter.Convert("<kscript><fn name=\"other\" surface=\"window\"><echo>x</echo></fn></kscript>", "f.ks.xml").Text);
        }

        [Fact]
        public void A_parameter_of_another_method_is_not_a_surface()
        {
            var document = CodeParser.Parse("type T extends Component {\n  fn show(window) => $window\n\n  fn render(window) {\n    slot\n  }\n}\n\nfn render(terminal) => $terminal\n");
            var functions = document.DescendantElements().Where(e => e.Name == "fn").ToList();
            Assert.Equal("window", functions[0].GetAttributeValue("params"));
            Assert.Equal("window", functions[1].GetAttributeValue("surface"));
            Assert.Null(functions[1].GetAttribute("params"));
            Assert.Equal("terminal", functions[2].GetAttributeValue("params"));
        }
    }
}
