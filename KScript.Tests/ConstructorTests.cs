using KScript.Pages;
using KScript.Runtime;

using static KScript.Tests.ScriptRunner;

namespace KScript.Tests;

/// <summary>construct({ ... }) { ... }: what a caller may pass, the value's own state, and the code that runs once.</summary>
public class ConstructorTests
{
    private static string Code(string code) => Run(code, path: Path.Combine(Path.GetTempPath(), "construct.kscript"));

    private const string Point = """
        type Point {
          construct({
            x: number,
            y: number = 2,
          }) {
            sum = $x + $y
            label = "({$x}, {$y})"
          }

          sum: number
          label: string
          tags: string[]
          note?: string
          extra: map
          items: list
          on: bool
          kind: Kind
          scale: int = 3

          fn describe() => "{$label} sums to {$sum}"
        }

        enum Kind { near, far }

        """;

    [Fact]
    public void The_block_runs_once_with_the_passed_fields_and_sets_the_values_own_state()
    {
        Assert.Equal("(3, 4) sums to 7\n(1, 2)\n", Code(Point + "echo Point(x: 3, y: 4)->describe()\necho Point(x: 1).label"));
    }

    [Fact]
    public void Own_state_starts_with_its_default_or_empty()
    {
        Assert.Equal("0 true true false true 3\n", Code(Point
            + "p = Point({ x: 1 })\necho \"{$p.tags->count()} {$p.note == null} {$p.extra->keys()->count() + $p.items->count() == 0} {$p.on} {$p.kind == null} {$p.scale}\""));
    }

    [Fact]
    public void Passing_own_state_is_an_error_that_names_what_construct_takes()
    {
        var error = Assert.Throws<KScriptException>(() => Code(Point + "echo Point(x: 1, sum: 9)"));
        Assert.Contains("$.sum: is Point's own state and can't be passed in. construct takes: x, y.", error.Message);
    }

    [Fact]
    public void A_missing_or_misfit_input_is_reported_before_the_block_runs()
    {
        var error = Assert.Throws<KScriptException>(() => Code(Point + "echo Point(y: 1)"));
        Assert.Contains("$.x", error.Message);
        Assert.DoesNotContain("sum", error.Message);
    }

    [Fact]
    public void A_constructor_needs_no_block_and_a_joined_type_runs_its_bases_first()
    {
        const string code = """
            type Named {
              construct({ name: string })
            }
            type Logged {
              construct({ id: int }) {
                trail = $trail->push('logged')
              }
              trail: string[]
            }
            type Entry with [Named, Logged] {
              construct({ note: string = 'none' }) {
                trail = $trail->push('entry')
              }
            }
            e = Entry(name: 'a', id: 1)
            echo "{$e.name} {$e.id} {$e.note} {$e.trail->join(',')}"
            """;
        Assert.Equal("a 1 none logged,entry\n", Code(code));
    }

    [Theory]
    [InlineData("type T {\n  construct({ a: int })\n  construct({ b: int })\n}", "two construct blocks")]
    [InlineData("type T {\n  construct({ a: int, a: int })\n}", "'a' twice")]
    [InlineData("type T {\n  construct({ a: int => 1 })\n}", "can't be worked out with =>")]
    public void A_mistake_in_a_constructor_says_what_to_change(string code, string message)
    {
        Assert.Contains(message, Assert.Throws<KScriptException>(() => Code(code + "\necho 1")).Message);
    }

    [Fact]
    public void The_xml_form_holds_the_fields_then_the_statements()
    {
        const string xml = """
            <kscript>
              <type name="Point">
                <construct>
                  <field name="x" type="number" />
                  <set name="doubled" value="$x * 2" />
                </construct>
                <field name="doubled" type="number" />
              </type>
              <echo>{ Point(x: 4).doubled }</echo>
              <construct />
            </kscript>
            """;
        Assert.Contains("<construct> must be", Assert.Throws<KScriptException>(() => Run(xml)).Message);
        Assert.Equal("8\n", Run(xml.Replace("<construct />", string.Empty)));
    }

    private const string Card = """
        import KScript.Web

        type NoteCard extends Component {
          construct({
            title: string,
            open: bool = false,
          }) {
            heading = $title->upper()
          }

          heading: string
          clicks: int

          fn render(): Component {
            return ```html
            <p>{$heading} {$open} {$clicks}</p>
            <button @on-click(clicks += 1)>Add</button>
            ```
          }
        }
        """;

    [Fact]
    public void A_component_takes_only_what_its_constructor_lists()
    {
        using var site = new TempSite(("index.html", "<note-card title=\"tea\" open=\"{true}\" />"), ("bad.html", "<note-card title=\"tea\" clicks=\"{5}\" />"),
            ("_components/note-card.kscript", Card));
        Assert.Contains("<p>TEA true 0</p>", site.Render("index.html").Body);
        Assert.Contains("is NoteCard's own state and can't be passed in", Assert.ThrowsAny<Exception>(() => site.Render("bad.html")).Message);
    }
}
