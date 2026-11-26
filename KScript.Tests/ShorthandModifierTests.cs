using KScript.Pages;
using KScript.Runtime;

namespace KScript.Tests;

/// <summary>reset:, cycle: and the modifiers (key:, confirm:, debounce:) in a page handler, @bind options, @show and @live-disable(while:).</summary>
public class ShorthandModifierTests
{
    private const string Component = """
        import KScript.Web

        type TagBoard extends Component {
          draft: string = 'red'
          view: string = 'list'
          goal: int = 3
          name: string = ''
          tips: bool = true

          fn render() => ```html
          <p>{$draft}|{$view}|{$goal}|{$name}|{$tips}</p>
          <button @on-click(reset: $draft)>0</button>
          <button @on-click(cycle: $view, through: ['list', 'grid', 'table'])>1</button>
          <input @on-keydown(reset: $draft, key: escape)>
          <button @on-click(toggle: $tips, confirm: 'Sure?', once: true, self: true, disable: true)>3</button>
          <input @on-input(set: $view, to: 'x', debounce: 300ms, key: [ctrl, s])>
          <input @bind($goal, as: int, on: blur)>
          <input @bind($name, trim: true, debounce: 250ms)>
          <em @show($tips)>tip</em>
          <button @live-disable(while: $name == '')>Save</button>
          ```
        }
        """;

    private static TempSite Site() => new(("index.html", "<body><tag-board live-id=\"b\" /></body>"), ("_components/tag-board.kscript", Component));

    private static string Attr(string html, string name) =>
        System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Match(html, name + "=\"([^\"]*)\"").Groups[1].Value);

    private static PageResult Event(TempSite site, int handler, string html, Dictionary<string, object> ev = null, Dictionary<string, object> binds = null) =>
        PageServer.HandleLive(new PageSite(site.Root), Values.ToJson(new Dictionary<string, object>
        {
            ["page"] = "/index",
            ["block"] = "b",
            ["handler"] = (double)handler,
            ["state"] = Attr(html, "data-ks-state"),
            ["args"] = Attr(html, "data-ks-args-" + handler) is { Length: > 0 } args ? args : null,
            ["binds"] = binds ?? new Dictionary<string, object>(),
            ["event"] = ev ?? new Dictionary<string, object>(),
        }), new Dictionary<string, object>());

    private static string Line(string html) => System.Text.RegularExpressions.Regex.Match(html, "<p>([^<]*)</p>").Groups[1].Value;

    [Fact]
    public void Reset_and_cycle_change_their_fields()
    {
        using var site = Site();
        string html = site.Render("index.html").Body;
        Assert.Equal("red|list|3||true", Line(html));
        html = Event(site, 1, html).Body;
        html = Event(site, 1, html).Body;
        Assert.Equal("red|table|3||true", Line(html));
        html = Event(site, 1, html).Body;
        Assert.Equal("red|list|3||true", Line(html));
    }

    [Fact]
    public void Key_runs_the_handler_only_for_that_key()
    {
        using var site = Site();
        string html = site.Render("index.html").Body;
        html = Event(site, 4, html, ev: new() { ["key"] = "a" }).Body;
        Assert.Equal("red|list|3||true", Line(html));
        html = Event(site, 4, html, ev: new() { ["key"] = "s", ["ctrl"] = true }).Body;
        Assert.Equal("red|x|3||true", Line(html));
        html = Event(site, 2, html, ev: new() { ["key"] = "Enter" }).Body;
        Assert.Equal("red|x|3||true", Line(html));
    }

    [Fact]
    public void Modifiers_and_binding_options_reach_the_browser()
    {
        using var site = Site();
        string html = site.Render("index.html").Body;
        Assert.Contains("\"confirm\":\"Sure?\"", Attr(html, "data-ks-mods-click"));
        Assert.Contains("\"once\":true", Attr(html, "data-ks-mods-click"));
        Assert.Contains("\"self\":true", Attr(html, "data-ks-mods-click"));
        Assert.Contains("data-ks-live-disable", html);
        Assert.Contains("\"debounce\":300", Attr(html, "data-ks-mods-input"));
        Assert.Contains("data-ks-bind-on=\"blur\"", html);
        Assert.Contains("data-ks-bind-debounce=\"250\"", html);
        Assert.Matches("<button[^>]*data-ks-live-disable[^>]* disabled", html);
        Assert.DoesNotMatch("<em[^>]* hidden", html);
    }

    [Fact]
    public void Bound_values_are_trimmed_and_read_as_their_type()
    {
        using var site = Site();
        string html = site.Render("index.html").Body;
        html = Event(site, 3, html, binds: new() { ["name"] = "  Ada  ", ["goal"] = "7" }).Body;
        Assert.Equal("red|list|7|Ada|false", Line(html));
        Assert.Matches("<em[^>]* hidden", html);
        Assert.DoesNotMatch("<button[^>]*data-ks-live-disable[^>]* disabled", html);
        PageResult refused = Event(site, 3, html, binds: new() { ["goal"] = "lots" });
        Assert.Equal(422, refused.Status);
    }
}
