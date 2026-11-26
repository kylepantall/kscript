using KScript.Pages;
using KScript.Runtime;

using static KScript.Tests.ScriptRunner;

namespace KScript.Tests;

/// <summary>private fields and methods: only the type's own code can read, set, call or pass them.</summary>
public class PrivateMemberTests
{
    private const string Counter = """
        type Counter {
          construct({ start: int = 0 }) {
            count = $start
          }

          private count: int

          /// Twice the count.
          private fn tally() => $count * 2

          fn total() => $this->tally() + $this.count
          fn same(other: Counter) => $other.count == $count and $other->tally() == $this->tally()
          fn later() => [1]->map(fn(n) => $n + $this.count)
        }

        c = Counter(start: 2)

        """;

    private static string Code(string code) => Run(code, path: Path.Combine(Path.GetTempPath(), "private-members.kscript"));

    private static KScriptException Fails(string code) => Assert.Throws<KScriptException>(() => Code(Counter + code));

    [Fact]
    public void A_types_own_methods_use_its_private_members()
    {
        Assert.Equal("6\ntrue\n[3]\n", Code(Counter + "echo $c->total()\necho $c->same(Counter(start: 2))\necho $c->later()->to_json()\n"));
    }

    [Fact]
    public void Code_outside_the_type_cannot_read_set_or_call_them()
    {
        const string message = "is private to Counter. Only Counter's own methods can use it.";
        KScriptException read = Fails("echo $c.count");
        Assert.Equal("Context", read.Type);
        Assert.Contains("'count' " + message, read.Message);
        Assert.Contains("'count' " + message, Fails("echo $c['count']").Message);
        Assert.Contains("'count' " + message, Fails("echo $c?.count ?? 0").Message);
        Assert.Contains("'count' " + message, Fails("fn made() => $c\necho @made().count ?? 0").Message);
        Assert.Contains("'tally' " + message, Fails("echo $c->tally()").Message);
        Assert.Contains("'count' " + message, Fails("c.count = 5").Message);
        Assert.Contains("'count' " + message, Fails("c['count'] += 5").Message);
        // A key that isn't a field name is read as before.
        Assert.Equal("2\n", Code(Counter + "echo [1, 2][1]\n"));
    }

    [Fact]
    public void A_private_field_cannot_be_passed_in_and_is_left_out_of_json()
    {
        Assert.Contains("$.count: is private to Counter and can't be passed in.", Fails("x = Counter(count: 4)").Message);
        Assert.Equal("{\"start\":2}\n{\n  \"start\": 2\n}\n", Code(Counter + "echo $c->to_json()\necho $c\n"));
        Assert.Equal("{}\n", Code("type Secret {\n  private key: string = 'k'\n}\n\necho Secret()\n"));
        var passed = Assert.Throws<KScriptException>(() => Code("type Secret {\n  private key: string = 'k'\n  name: string\n}\n\nx = Secret(name: 'a', key: 'b')\n"));
        Assert.Contains("$.key: is private to Secret and can't be passed in.", passed.Message);
    }

    [Fact]
    public void A_joined_type_uses_the_private_members_it_was_given()
    {
        string output = Code("""
            type Base {
              private seed: int = 3
              private fn twice() => $seed * 2
            }

            type Child with [Base] {
              fn show() => $this->twice() + $this.seed
            }

            echo Child()->show()
            try {
              echo Child().seed
            } catch e {
              echo $e.message
            }
            """);
        Assert.Equal("9\n'seed' is private to Base. Only Base's own methods can use it.\n", output);
    }

    [Fact]
    public void The_xml_form_is_the_private_attribute()
    {
        var error = Assert.Throws<KScriptException>(() => Run("""
            <kscript>
              <type name="Box">
                <field name="key" type="string" default="'k'" private="yes" />
                <fn name="peek" value="$key" />
              </type>
              <echo>{ Box()->peek() }</echo>
              <echo>{ Box().key }</echo>
            </kscript>
            """));
        Assert.Contains("'key' is private to Box", error.Message);
    }

    private const string Card = """
        import KScript.Web

        type FlipCard extends Component {
          construct({ title: string = 'Card' })

          private open: bool
          private clicks: int

          private fn count() {
            clicks += 1
          }

          fn render() => ```html
            <p>{$title} {$open} {$clicks}</p>
            <button @on-click(toggle: $open)>Toggle</button>
            <button @on-click($count)>Count</button>
            <button @on-click(clicks += 10)>Ten</button>
            ```
        }
        """;

    private static string Attr(string html, string name) =>
        System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Match(html, name + "=\"([^\"]*)\"").Groups[1].Value);

    private static PageResult Event(TempSite site, int handler, string html) =>
        PageServer.HandleLive(new PageSite(site.Root), Values.ToJson(new Dictionary<string, object>
        {
            ["page"] = "/index",
            ["block"] = "c",
            ["handler"] = (double)handler,
            ["state"] = Attr(html, "data-ks-state"),
            ["args"] = Attr(html, "data-ks-args-" + handler) is { Length: > 0 } args ? args : null,
            ["binds"] = new Dictionary<string, object>(),
            ["event"] = new Dictionary<string, object>(),
        }), new Dictionary<string, object>());

    [Fact]
    public void A_live_component_keeps_private_state_between_updates()
    {
        using var site = new TempSite(("index.html", "<body><flip-card live-id=\"c\" /></body>"), ("_components/flip-card.kscript", Card));
        string html = site.Render("index.html").Body;
        Assert.Contains("<p>Card false 0</p>", html);
        PageResult flipped = Event(site, 0, html);
        Assert.Contains("<p>Card true 0</p>", flipped.Body);
        PageResult counted = Event(site, 1, flipped.Body);
        Assert.Contains("<p>Card true 1</p>", counted.Body);
        PageResult ten = Event(site, 2, counted.Body);
        Assert.Contains("<p>Card true 11</p>", ten.Body);

        // A page can't pass a private field to the component.
        using var passing = new TempSite(("index.html", "<body><flip-card live-id=\"c\" open=\"{true}\" /></body>"), ("_components/flip-card.kscript", Card));
        Assert.Contains("$.open: is private to FlipCard and can't be passed in.", Assert.Throws<PageException>(() => passing.Render("index.html")).Message);
    }
}
