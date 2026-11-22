using System.Net;
using System.Text.RegularExpressions;

using KScript.Pages;
using KScript.Runtime;

namespace KScript.Tests;

public sealed class RoutedLiveTests : AuthSiteFixture
{
    public RoutedLiveTests()
    {
        Write("routes/web.kscript", """
            import KScript.Http
            import KScript.Auth
            Routes {
              Get(path: "/sign-in") {
                @login({ id: 'one' })
                return 'ok'
              }
              Get(path: "/edit/{id}", view: "editor", middleware: [auth:session, cache_response:1m])
            }
            """);
        Write("views/editor.html", """<h1>Editor</h1><counter item="{$request.params.id}" />""");
        Write("_components/counter.html", """
            <live id="item-{$item}">
              <state name="count" value="0" /><state name="draft" value="''" />
              <input bind="draft"><p>{$count} {$draft}</p>
              <button on-click="count += 1">Add</button>
              <if test="false"><button on-click="count += 2">Hidden</button></if>
            </live>
            """);
    }

    private static string Attribute(PageResult page, string attribute) =>
        WebUtility.HtmlDecode(Regex.Match(page.Body, attribute + "=\"([^\"]*)\"").Groups[1].Value);

    private static PageResult Click(Browser browser, PageResult page, string path = "/edit/7", int handler = 0,
        string field = "draft", string args = null, string state = null) =>
        browser.Send("POST", path, headers: new() { ["x-kscript-live"] = Attribute(page, "data-ks-route") },
            json: Values.ToJson(new Dictionary<string, object>
            {
                ["block"] = "item-7", ["handler"] = (double)handler,
                ["state"] = state ?? Attribute(page, "data-ks-state"),
                ["args"] = args ?? Attribute(page, "data-ks-args-0"), ["binds"] = new Dictionary<string, object> { [field] = "bio" },
            }));

    [Fact]
    public void RedrawReentersTheGetRouteAndReturnsOnlyTheKeyedComponent()
    {
        var browser = new Browser(App());
        browser.Get("/sign-in");
        PageResult page = browser.Get("/edit/7");
        Assert.Equal("private, no-store", page.Headers["Cache-Control"]);
        Assert.Contains("data-ks-live=\"item-7\"", page.Body);
        PageResult saved = Click(browser, page);
        Assert.True(saved.Status == 200, saved.Body);
        Assert.Equal("1", saved.Headers["X-KScript-Fragment"]);
        Assert.Contains("<p>1 bio</p>", saved.Body);
        Assert.DoesNotContain("<h1>", saved.Body);
        Assert.Contains("<p>2 bio</p>", Click(browser, saved).Body);

        string component = Read("_components/counter.html");
        Write("_components/counter.html", component.Replace("<button on-click=\"count += 1\">Add</button>", "<if test=\"false\"><button on-click=\"count += 1\">Add</button></if>"));
        Assert.Equal(409, Click(browser, saved).Status);
        Write("_components/counter.html", component);

        // Change the access rule after the component was rendered.
        Write("routes/web.kscript", Read("routes/web.kscript").Replace("auth:session", "auth:token"));
        Assert.True(Click(browser, saved).Status is 401 or 302);
    }

    [Fact]
    public void ProofsBelongToTheRenderedSessionRouteActionAndEditableFields()
    {
        var app = App();
        var first = new Browser(app);
        var second = new Browser(app);
        first.Get("/sign-in");
        second.Get("/sign-in");
        PageResult page = first.Get("/edit/7");
        Assert.Equal(409, Click(second, page).Status);
        Assert.Equal(409, Click(first, page, path: "/edit/8").Status);
        Assert.Equal(409, Click(first, page, handler: 1).Status);
        Assert.Equal(409, Click(first, page, args: "").Status);
        Assert.Equal(409, Click(first, page, state: "").Status);
        Assert.Equal(409, Click(first, page, field: "count").Status);
    }
}
