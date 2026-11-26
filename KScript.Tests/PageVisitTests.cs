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
          <input @bind('name')>
          <button @on-click(increment: $count)>Add one</button>
          <button aria-label="Reset" @on-click(set: $count, to: 0)>×</button>
          <button @on-click(call: $missing)>Break</button>
          <label><input type="checkbox" @on-change(toggle: $broken)> Broken</label>
          <p @class(on: $broken)>State</p>
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
    public void A_visit_says_what_went_wrong(string code, string message)
    {
        using var site = Site();
        Assert.Contains(message, Fails(site, "echo " + code));
    }
}
