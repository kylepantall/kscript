using KScript.Pages;
using KScript.Runtime;
using KScript.UI;

using static KScript.Tests.ScriptRunner;

namespace KScript.Tests;

/// <summary>A handler that is one value: a bool flips and a function is called, on pages and in windows.</summary>
[Collection("KScript.UI")]
public class ValueHandlerTests
{
    private const string Component = """
        import KScript.Web

        type FlipCard extends Component {
          open: bool = false
          count: int = 0
          label: string = 'x'

          fn add() {
            count += 2
          }

          fn render(): Component {
            return ```html
            <p>{$open} {$count}</p>
            <button @on-click($open)>Toggle</button>
            <button on-click="$add">Add</button>
            <button on-click="$label">Bad</button>
            <button on-click="$missing">Missing</button>
            ```
          }
        }
        """;

    private static TempSite Site() => new(("index.html", "<body><flip-card live-id=\"c\" /></body>"), ("_components/flip-card.kscript", Component));

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
    public void A_page_handler_flips_a_bool_and_calls_a_method()
    {
        using var site = Site();
        string html = site.Render("index.html").Body;
        Assert.Contains("<p>false 0</p>", html);
        PageResult flipped = Event(site, 0, html);
        Assert.Contains("<p>true 0</p>", flipped.Body);
        PageResult back = Event(site, 0, flipped.Body);
        Assert.Contains("<p>false 0</p>", back.Body);
        PageResult added = Event(site, 1, back.Body);
        Assert.Contains("<p>false 2</p>", added.Body);
    }

    [Fact]
    public void A_page_handler_that_is_another_kind_of_value_says_what_to_write()
    {
        using var site = Site();
        PageResult failed = Event(site, 2, site.Render("index.html").Body);
        Assert.Contains("flips a bool or calls a function", failed.Body);
        Assert.Contains("missing", Event(site, 3, site.Render("index.html").Body).Body);
    }

    [Fact]
    public void A_window_handler_flips_a_bool_and_calls_a_function()
    {
        const string code = """
            import KScript.UI

            Window(title: "Flip") {
              open = false
              count = 0
              fn add() {
                count += 2
              }

              Text "{$open} {$count}"
              Button(on_click: $open) "Toggle"
              Button(on_click: $add) "Add"
            }
            """;
        Exception failure = null;
        UIPackage.Host = s =>
        {
            try
            {
                Assert.Equal("false 0", s.FindAll("Text")[0].Text);
                s.Click("Toggle");
                Assert.Equal("true 0", s.FindAll("Text")[0].Text);
                s.Click("Add");
                Assert.Equal("true 2", s.FindAll("Text")[0].Text);
            }
            catch (Exception ex) { failure = ex; }
        };
        try
        {
            Run(code, path: Path.Combine(Path.GetTempPath(), "flip.kscript"));
        }
        finally
        {
            UIPackage.Host = null;
        }
        Assert.Null(failure);
    }
}
