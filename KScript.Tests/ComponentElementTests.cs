using KScript.Runtime;
using KScript.UI;

using static KScript.Tests.ScriptRunner;

namespace KScript.Tests;

/// <summary>Components written as elements in a script: drawn in a window or the terminal by the render that fits.</summary>
[Collection("KScript.UI")]
public class ComponentElementTests
{
    private const string NoteCard = """
        import KScript.UI

        type NoteCard extends Component {
          construct({ title: string, body: string = '' })

          open: bool
          clicks: int

          fn toggle() {
            open = not $open
            clicks += 1
          }

          fn render() {
            Box(padding: 8) {
              Text "{$title} ({$clicks})"
              Button(on_click: $toggle) "{ $open ? 'Hide' : 'Show' } {$title}"
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

    private static string Window(string code, Action<UISession> drive)
    {
        Exception failure = null;
        UIPackage.Host = s =>
        {
            try { drive(s); }
            catch (Exception ex) { failure = ex; }
        };
        try
        {
            string output = Run(code, path: Path.Combine(Path.GetTempPath(), "component-window.kscript"));
            if (failure != null) throw new Xunit.Sdk.XunitException(failure.ToString());
            return output;
        }
        finally
        {
            UIPackage.Host = null;
        }
    }

    private static string[] Texts(UISession s) => s.FindAll("Text").Select(t => t.Text).ToArray();

    [Fact]
    public void A_window_draws_a_component_and_each_one_keeps_its_own_state()
    {
        Window(NoteCard + """
            Window(title: "Notes") {
              notes = [{ t: 'Milk', b: 'Two pints' }, { t: 'Eggs', b: 'Six' }]
              suffix = '!'

              for n in $notes {
                NoteCard(title: $n.t + $suffix, body: $n.b) {
                  Text "from the list"
                }
              }
              Button(on_click: suffix = '?') "Rename"
              Button(on_click: notes = [$notes[1]]) "Drop first"
            }
            """, s =>
        {
            Assert.Equal(new[] { "Milk! (0)", "Eggs! (0)" }, Texts(s));
            s.Click("Show Eggs!");
            // Only the second card opened, and it draws what the window wrote inside it.
            Assert.Equal(new[] { "Milk! (0)", "Eggs! (1)", "Six", "from the list" }, Texts(s));
            // What the window passes follows the window; what the card keeps stays.
            s.Click("Rename");
            Assert.Equal(new[] { "Milk? (0)", "Eggs? (1)", "Six", "from the list" }, Texts(s));
            s.Click("Hide Eggs?");
            Assert.Equal(new[] { "Milk? (0)", "Eggs? (2)" }, Texts(s));
            // A card that is no longer drawn is gone: the one left is now the first, and starts fresh.
            s.Click("Drop first");
            Assert.Equal(new[] { "Eggs? (0)" }, Texts(s));
        });
    }

    [Fact]
    public void An_id_keeps_a_components_state_with_its_item()
    {
        Window(NoteCard + """
            Window(title: "Notes") {
              notes = ['Milk', 'Eggs']

              for n in $notes {
                NoteCard(title: $n, id: $n)
              }
              Button(on_click: notes = ['Eggs']) "Drop first"
              Button(on_click: notes = ['Eggs', 'Eggs']) "Twice"
            }
            """, s =>
        {
            s.Click("Show Eggs");
            s.Click("Drop first");
            Assert.Equal(new[] { "Eggs (1)", "" }, Texts(s));
            s.Click("Twice");
            Assert.Contains("Two NoteCard components here have the id Eggs. Give each its own.", s.Error);
        });
    }

    [Fact]
    public void The_terminal_draws_a_component_with_its_terminal_render()
    {
        Assert.Equal("* Milk: Two pints\nfrom the list\n* Eggs:\n",
            Run(NoteCard + "NoteCard(title: 'Milk', body: 'Two pints') {\n  echo \"from the list\"\n}\nNoteCard(title: 'Eggs')\n", path: Path.Combine(Path.GetTempPath(), "component-terminal.kscript")));
    }

    [Fact]
    public void A_component_with_no_render_for_the_surface_says_what_to_add()
    {
        const string only = "import KScript.UI\n\ntype Tag extends Component {\n  label: string\n\n  fn render() {\n    Text \"{$label}\"\n  }\n}\n\n";
        var error = Assert.Throws<KScriptException>(() => Run(only + "Tag(label: 'x')\n", path: Path.Combine(Path.GetTempPath(), "tag.kscript")));
        Assert.Contains("Tag has no render for the terminal. Add one to the component: fn render(terminal) { }.", error.Message);
    }

    [Fact]
    public void A_render_that_does_not_show_its_surface_must_name_it()
    {
        const string code = "import KScript.UI\n\ntype Wrap extends Component {\n  fn render() {\n    if true {\n      slot\n    }\n  }\n}\n";
        var error = Assert.Throws<KScriptException>(() => Run(code, path: Path.Combine(Path.GetTempPath(), "wrap.kscript")));
        Assert.Contains("Wrap's render doesn't show where it draws. Name it: fn render(window), fn render(terminal) or fn render(document).", error.Message);
        Window(code.Replace("fn render()", "fn render(window)") + "\nWindow(title: \"W\") {\n  Wrap {\n    Text \"inside\"\n  }\n}\n",
            s => Assert.Equal(new[] { "inside" }, Texts(s)));
    }

    [Fact]
    public void Two_renders_for_one_surface_and_other_mistakes_are_reported()
    {
        string Fails(string code) => Assert.Throws<KScriptException>(() => Run(code, path: Path.Combine(Path.GetTempPath(), "mistake.kscript"))).Message;
        Assert.Contains("Twice has two render methods for the terminal. Keep one.",
            Fails("type Twice extends Component {\n  fn render() {\n    echo 'a'\n  }\n\n  fn render(terminal) {\n    echo 'b'\n  }\n}\n"));
        // A capitalised element that isn't a component is still unknown, and so is a slot outside a render.
        Assert.Contains("Unknown", Fails("type Point {\n  x: int\n}\n\ntype C extends Component {\n  fn render() {\n    echo 'a'\n  }\n}\n\nPoint(x: 1) {\n}\nNope()\n"));
        Assert.Contains("slot only works in a component's render", Fails("slot\n"));
    }

    [Fact]
    public void A_script_draws_a_component_from_a_file_it_imports_and_inputs_bind()
    {
        using var site = new TempSite(
            ("components/name-box.kscript", """
                import KScript.UI

                type NameBox extends Component {
                  construct({ label: string })

                  name: string = 'Ada'

                  fn render() {
                    Text "{$label}: {$name}"
                    TextBox(bind: name)
                  }
                }
                """),
            ("app.kscript", """
                import KScript.UI
                import "components/name-box.kscript"

                Window(title: "Names") {
                  NameBox(label: 'First')
                  NameBox(label: 'Second')
                }
                """),
            ("app.ks.xml", """
                <kscript>
                  <import package="KScript.UI" />
                  <import path="components/name-box.kscript" />
                  <KScript.Window title="Names">
                    <NameBox label="Only" />
                  </KScript.Window>
                </kscript>
                """));
        Exception failure = null;
        var seen = new List<string>();
        UIPackage.Host = s =>
        {
            try
            {
                seen.Add(string.Join(" | ", Texts(s)));
                if (s.FindAll("TextBox").Count < 2) return;
                s.SetBound(s.FindAll("TextBox")[1], "Grace");
                seen.Add(string.Join(" | ", Texts(s)));
            }
            catch (Exception ex) { failure = ex; }
        };
        try
        {
            new Interpreter(KScriptHost.ForText(new StringWriter())).RunFile(Path.Combine(site.Root, "app.kscript"));
            new Interpreter(KScriptHost.ForText(new StringWriter())).RunFile(Path.Combine(site.Root, "app.ks.xml"));
        }
        finally
        {
            UIPackage.Host = null;
        }
        Assert.Null(failure);
        Assert.Equal(new[] { "First: Ada | Second: Ada", "First: Ada | Second: Grace", "Only: Ada" }, seen);
    }

    [Fact]
    public void A_page_draws_a_render_with_no_return_type_and_refuses_a_component_made_for_a_window()
    {
        using var site = new TempSite(
            ("index.html", "<body><hello-card name=\"Sam\" /></body>"),
            ("window.html", "<body><win-only /></body>"),
            ("_components/hello-card.kscript", "import KScript.Web\n\ntype HelloCard extends Component {\n  name: string\n\n  fn render() {\n    return ```html\n    <p>Hello, {$name}!</p>\n    ```\n  }\n\n  override fn style() => ```css\n  p { color: teal; }\n  ```\n}\n"),
            ("_components/win-only.kscript", "import KScript.UI\n\ntype WinOnly extends Component {\n  fn render() {\n    Spacer(size: 1)\n  }\n}\n"));
        string html = site.Render("index.html").Body;
        Assert.Contains("<p>Hello, Sam!</p>", html);
        Assert.Contains("color: teal", html);
        var error = Assert.ThrowsAny<Exception>(() => site.Render("window.html"));
        Assert.Contains("WinOnly has no render for a page. Add one to the component: fn render(page) { }.", error.Message);
    }
}
