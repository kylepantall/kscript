using KScript.Runtime;

using static KScript.Tests.ScriptRunner;

namespace KScript.Tests;

/// <summary>A component that keeps the types, enums and errors only it uses inside its own block.</summary>
public class NestedDeclarationTests
{
    private const string Component = """
        import KScript.Web

        type TodoList extends Component {
          /// One thing to do.
          type Item {
            title: string
            state: State = 'open'
          }

          enum State { open, done }

          error Full(message: "The list is full")

          construct({ limit: int = 2, items: Item[] = [] })

          fn add(title: string) {
            if $items->count() >= $limit {
              throw Full()
            }
            items = $items->push(Item(title: $title))
          }

          fn render(): Component {
            return ```html
            <p>{$items->count()} of {$limit}, first is {Item(title: 'x').state}</p>
            ```
          }
        }

        """;

    [Fact]
    public void A_page_renders_a_component_that_uses_its_own_types()
    {
        using var site = new TempSite(("index.html", "<body><todo-list limit=\"{3}\" /></body>"), ("_components/todo-list.kscript", Component));
        Assert.Contains("<p>0 of 3, first is open</p>", site.Render("index.html").Body);
    }

    [Fact]
    public void The_rest_of_the_file_can_use_them_and_an_importing_file_cannot()
    {
        using var site = new TempSite(("todo-list.kscript", Component + """
            list = TodoList(limit: 0, items: [Item(title: 'a')])
            try {
              $list->add('c')
            } catch Full as err {
              echo "{$list.items->count()} {$list.items[0].state} {$err.message} {'done' in State}"
            }
            """), ("other.kscript", "import \"todo-list.kscript\"\necho TodoList({ }).limit\necho Item(title: 'x').title\n"));
        Assert.Equal("1 open The list is full true\n", RunFile(site, "todo-list.kscript"));
        var error = Assert.Throws<KScriptException>(() => RunFile(site, "other.kscript"));
        Assert.Contains("Item", error.Message);
    }

    [Fact]
    public void Only_a_component_holds_declarations()
    {
        var error = Assert.Throws<KScriptException>(() => Run("type Box {\n  enum Size { small, large }\n  size: Size\n}\n", path: Path.Combine(Path.GetTempPath(), "box.kscript")));
        Assert.Contains("Only a component can hold its own types, so enum Size can't sit inside type Box. Give it its own file.", error.Message);
    }

    private static string RunFile(TempSite site, string name)
    {
        var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture) { NewLine = "\n" };
        new Interpreter(KScriptHost.ForText(output)).RunFile(Path.Combine(site.Root, name));
        return output.ToString().Replace("\r\n", "\n");
    }
}
