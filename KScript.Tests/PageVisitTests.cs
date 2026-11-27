using System.Globalization;
using KScript.Runtime;

namespace KScript.Tests;

/// <summary>@visit('/') renders a page of the project in a test, and its clicks run the live handlers.</summary>
public class PageVisitTests
{
    private const string Counter = """
        import KScript.Web

        type ClickCounter extends Component {
          count: int = 0
          name: string = ''
          broken: bool = false

          fn render() => ```html
          <p class="total big">Count {$count}</p>
          <p>Name {$name}</p>
          <input @bind($name)>
          <button @on-click(increment: $count)>Add one</button>
          <button aria-label="Reset" @on-click(set: $count, to: 0)>×</button>
          <button @on-click(call: $missing)>Break</button>
          <label><input type="checkbox" @on-change(toggle: $broken)> Broken</label>
          <p @class(on: $broken)>State</p>
          <input placeholder="Search" @on-keydown(set: $name, to: 'Esc', key: escape)>
          <input placeholder="Save" @on-keydown(set: $name, to: 'Saved', key: [ctrl, s])>
          ```
        }
        """;

    private static TempSite Site() => new(
        ("kscript.json", "{ \"serve\": { \"root\": \"site\" } }"),
        ("site/index.html", "<body><click-counter live-id=\"c\" /></body>"),
        ("site/plain.html", "<body><button on-click=\"x = 1\">Loose</button><p>Plain</p></body>"),
        ("site/_components/click-counter.kscript", Counter));

    private static string Run(TempSite site, string code)
    {
        File.WriteAllText(Path.Combine(site.Root, "main.kscript"), "import KScript.Web\n" + code);
        var output = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        var host = KScriptHost.ForText(output);
        host.WorkingDirectory = site.Root;
        new Interpreter(host).RunFile(Path.Combine(site.Root, "main.kscript"));
        return output.ToString().Trim();
    }

    private static string Fails(TempSite site, string code) => Assert.Throws<KScriptException>(() => Run(site, code)).Message;

    [Fact]
    public void A_visit_clicks_fills_and_reads_the_page()
    {
        using var site = Site();
        Assert.Equal("/index 200 page /index", Run(site, "p = @visit('index')\necho \"{$p.path} {$p.status} {$p}\""));
        Assert.Contains("Count 2", Run(site, "echo @visit('/')->click('Add one')->click('Add one').text"));
        Assert.Contains("Count 0", Run(site, "echo @visit('/')->click('Add one')->click('Reset').text"));
        Assert.Contains("Name Joan", Run(site, "echo @visit('/')->fill('$name', 'Joan')->click('Add one').text"));
        Assert.Equal("total,big", Run(site, "echo @visit('/')->classes('Count 0')->join(',')"));
        Assert.Equal("on", Run(site, "echo @visit('/')->change('Broken')->classes('State')->join(',')"));
        Assert.Equal("", Run(site, "echo @visit('/')->classes('State')->join(',')"));
        Assert.Contains("<button", Run(site, "echo @visit('/').html"));
        Assert.Contains("Name Esc", Run(site, "echo @visit('/')->press('escape', in: 'Search').text"));
        Assert.Contains("Name Add one", Run(site, "echo @visit('/')->press('enter', in: 'Search').text"));
        Assert.Contains("Name Saved", Run(site, "echo @visit('/')->press(['ctrl', 's'], in: 'Save').text"));
        Assert.Contains("Name Add one", Run(site, "echo @visit('/')->press('s', in: 'Save').text"));
    }

    [Theory]
    [InlineData("@visit('  ')", "needs the address of a page")]
    [InlineData("@visit('/nowhere')", "the site has no page at /nowhere")]
    [InlineData("@visit('/')->click('Nothing')", "nothing on the page with the text 'Nothing' has on-click")]
    [InlineData("@visit('/')->click('  ')", "needs the text of a button or link")]
    [InlineData("@visit('/')->submit()", "the page has no form with on-submit")]
    [InlineData("@visit('/')->fill('age', 3)", "no input bound to age")]
    [InlineData("@visit('/')->classes('Missing')", "nothing on the page has the text 'Missing'")]
    [InlineData("@visit('/')->click('Break')", "failed (500)")]
    [InlineData("@visit('/plain')", "the page didn't render.")]
    [InlineData("@visit('/')->press(['ctrl'])", "->press() needs a key")]
    public void A_visit_says_what_went_wrong(string code, string message)
    {
        using var site = Site();
        Assert.Contains(message, Fails(site, "echo " + code));
    }

    /// <summary>A project whose pages come from routes: a view: route, a controller, a redirect and a file page (#198).</summary>
    private static TempSite RoutedSite() => new(
        ("kscript.json", "{ \"name\": \"routed\" }"),
        ("routes/web.kscript", """
            import KScript.Http
            import "../controllers/board.kscript" as board

            Routes {
              Get(path: /hello, view: 'hello')
              Get(path: /, to: @board.show)
              Get(path: /old, redirect: '/hello')
              Get(path: /gone, to: @board.gone)
              Get(path: /loop, redirect: '/loop')
            }
            """),
        ("controllers/board.kscript", """
            import KScript.Http
            import KScript.Web

            /// @public
            fn show(request) => @view('board', owner: $request.cookies.guest ?? 'guest')

            /// @public
            fn gone(request) => @response('Gone', status: 404)
            """),
        ("views/hello.html", "<p>Hello from a view</p>"),
        ("views/board.html", "<body><h1>Board of {$owner}</h1><click-counter live-id=\"c\" /></body>"),
        ("about.html", "<p>A file page</p>"),
        ("_components/click-counter.kscript", Counter));

    [Fact]
    public void A_visit_renders_pages_that_routes_serve()
    {
        using var site = RoutedSite();
        Assert.Equal("Hello from a view 200", Run(site, "p = @visit('/hello')\necho \"{$p.text} {$p.status}\""));
        Assert.Contains("Board of guest", Run(site, "echo @visit('/').text"));
        // The live handler runs against the route, as it does in the browser.
        Assert.Contains("Count 2", Run(site, "echo @visit('/')->click('Add one')->click('Add one').text"));
        Assert.Contains("Name Joan", Run(site, "echo @visit('/')->fill('$name', 'Joan')->click('Add one').text"));
        Assert.Equal("/hello Hello from a view", Run(site, "p = @visit('/old')\necho \"{$p.path} {$p.text}\""));
        Assert.Contains("Board of ann", Run(site, "echo @visit('/', { headers: { Cookie: 'guest=ann' } }).text"));
        // A path no route answers is a file page.
        Assert.Equal("A file page", Run(site, "echo @visit('/about').text"));
    }

    [Theory]
    [InlineData("@visit('/gone')", "its route answered 404")]
    [InlineData("@visit('/loop')", "redirected more than 5 times")]
    [InlineData("@visit('/nowhere')", "the site has no page at /nowhere")]
    [InlineData("@visit('/')->click('Break')", "failed (500)")]
    public void A_visit_to_a_route_says_what_went_wrong(string code, string message)
    {
        using var site = RoutedSite();
        Assert.Contains(message, Fails(site, "echo " + code));
    }
}
