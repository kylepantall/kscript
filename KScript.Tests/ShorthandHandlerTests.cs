using KScript.Pages;
using KScript.Runtime;

namespace KScript.Tests;

/// <summary>A page handler's optional named shorthands run left to right and check the fields they change.</summary>
public class ShorthandHandlerTests
{
    private const string Component = """
        import KScript.Web

        type TagBoard extends Component {
          open: bool = false
          count: int = 0
          tab: string = 'all'
          draft: string = 'red'
          tags: string[] = []
          said: string = ''

          fn say(word: string) {
            said = $word
          }

          fn render() => ```html
          <p>{$open}|{$count}|{$tab}|{$draft}|{$tags->join(',')}|{$said}</p>
          <button @on-click(toggle: $open, increment: $count)>0</button>
          <button @on-click(increment: $count, by: 5)>1</button>
          <button @on-click(decrement: $count, by: 2)>2</button>
          <button @on-click(set: $tab, to: 'notes')>3</button>
          <button @on-click(add: $draft, to: $tags, set: $draft, to: 'blue')>4</button>
          <button @on-click(remove: 'red', from: $tags)>5</button>
          <button @on-click(call: $say, with: ['hi'])>6</button>
          <button @on-click(toggle: $tab)>7</button>
          <button @on-click(set: $tab)>8</button>
          <button @on-click(call: $nope)>9</button>
          <button @on-click(call: $tab)>10</button>
          <button @on-click(toggle: $open, count += 1)>11</button>
          ```
        }
        """;

    private static TempSite Site() => new(("index.html", "<body><tag-board live-id=\"b\" /></body>"), ("_components/tag-board.kscript", Component));

    private static string Attr(string html, string name) =>
        System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Match(html, name + "=\"([^\"]*)\"").Groups[1].Value);

    private static PageResult Event(TempSite site, int handler, string html) =>
        PageServer.HandleLive(new PageSite(site.Root), Values.ToJson(new Dictionary<string, object>
        {
            ["page"] = "/index",
            ["block"] = "b",
            ["handler"] = (double)handler,
            ["state"] = Attr(html, "data-ks-state"),
            ["args"] = Attr(html, "data-ks-args-" + handler) is { Length: > 0 } args ? args : null,
            ["binds"] = new Dictionary<string, object>(),
            ["event"] = new Dictionary<string, object>(),
        }), new Dictionary<string, object>());

    private static string Line(string html) => System.Text.RegularExpressions.Regex.Match(html, "<p>([^<]*)</p>").Groups[1].Value;

    [Fact]
    public void Each_action_changes_its_field()
    {
        using var site = Site();
        string html = site.Render("index.html").Body;
        Assert.Equal("false|0|all|red||", Line(html));
        html = Event(site, 0, html).Body;
        Assert.Equal("true|1|all|red||", Line(html));
        html = Event(site, 1, html).Body;
        Assert.Equal("true|6|all|red||", Line(html));
        html = Event(site, 2, html).Body;
        Assert.Equal("true|4|all|red||", Line(html));
        html = Event(site, 3, html).Body;
        Assert.Equal("true|4|notes|red||", Line(html));
        html = Event(site, 4, html).Body;
        Assert.Equal("true|4|notes|blue|red|", Line(html));
        html = Event(site, 4, html).Body;
        Assert.Equal("true|4|notes|blue|red,blue|", Line(html));
        html = Event(site, 5, html).Body;
        Assert.Equal("true|4|notes|blue|blue|", Line(html));
        html = Event(site, 6, html).Body;
        Assert.Equal("true|4|notes|blue|blue|hi", Line(html));
    }

    [Theory]
    [InlineData(7, "toggle: works on a bool field, and $tab is")]
    [InlineData(8, "set: needs to: as well.")]
    [InlineData(9, "call: needs a function or one of the component&#39;s methods, and $nope is not defined.")]
    [InlineData(10, "A handler that is one value calls a function, and $tab is")]
    [InlineData(11, "Shorthands and statements can&#39;t be mixed in one handler.")]
    public void A_mistake_says_what_to_write(int handler, string message)
    {
        using var site = Site();
        string body = Event(site, handler, site.Render("index.html").Body).Body;
        Assert.Contains(System.Net.WebUtility.HtmlDecode(message), System.Net.WebUtility.HtmlDecode(body));
    }
}
