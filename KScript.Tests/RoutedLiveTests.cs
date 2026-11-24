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
    public void SessionStatePersistsForOneBrowserAndLockedStateRejectsClientBindings()
    {
        Write("_components/counter.html", """
            <live id="item-{$item}">
              <state name="count" value="0" session="counter" locked />
              <state name="draft" value="''" />
              <input bind="draft">
              <button on-click="count += 1">Add</button>
              <p>{$count}</p>
            </live>
            """);
        var app = App();
        var first = new Browser(app);
        var second = new Browser(app);
        first.Get("/sign-in");
        second.Get("/sign-in");
        PageResult page = first.Get("/edit/7");
        Assert.Contains("<p>0</p>", page.Body);
        Assert.Equal(409, Click(first, page, field: "count").Status);
        Assert.Contains("<p>1</p>", Click(first, page).Body);
        Assert.Contains("<p>1</p>", first.Get("/edit/7").Body);
        Assert.Contains("<p>0</p>", second.Get("/edit/7").Body);
    }

    [Fact]
    public void ADownloadActionSuppliesTheFileAndRejectsNamesWithPaths()
    {
        Write("_components/counter.html", """
            <live id="item-{$item}">
              <state name="draft" value="''" /><input bind="draft">
              <button on-click="@live_download('report.txt', bytes: 'Hello'->to_base64())">Download</button>
              <button on-click="@live_download('../report.txt', bytes: 'Hello'->to_base64())">Invalid</button>
            </live>
            """);
        var browser = new Browser(App());
        browser.Get("/sign-in");
        var original = browser.Get("/edit/7");
        var file = Click(browser, original);
        Assert.True(file.Status == 200, file.Body);
        Assert.Equal("{\"name\":\"report.txt\",\"bytes\":\"SGVsbG8=\"}", Attribute(file, "data-ks-download"));
        var invalid = Click(browser, original, handler: 1, args: Attribute(original, "data-ks-args-1"));
        Assert.True(invalid.Status >= 400);
        Assert.DoesNotContain("data-ks-download", invalid.Body);
    }

    [Fact]
    public void ARenderlessActionReturnsUpdatedStateWithoutReplacingTheComponent()
    {
        Write("_components/counter.html", """
            <live id="item-{$item}">
              <state name="count" value="0" /><state name="draft" value="''" />
              <input bind="draft">
              <button on-click="count += 1; @skip_render()">Update quietly</button>
              <button on-click="count += 1">Refresh</button>
              <p>{$count} {$draft}</p>
            </live>
            """);
        var browser = new Browser(App());
        browser.Get("/sign-in");
        var original = browser.Get("/edit/7");
        var quiet = Click(browser, original);
        Assert.True(quiet.Status == 200, quiet.Body);
        Assert.Contains(" data-ks-live-preserve", quiet.Body);
        Assert.DoesNotContain("<p>", quiet.Body);
        var fresh = Click(browser, original, handler: 1,
            args: Attribute(original, "data-ks-args-1"), state: Attribute(quiet, "data-ks-state"));
        Assert.Contains("<p>2 bio</p>", fresh.Body);
    }

    [Fact]
    public void LazyBlocksOnlyAcceptTheMountRequestForTheirSignedPlaceholders()
    {
        Write("_components/counter.html", """
            <live id="item-{$item}" lazy>
              <placeholder><p>Loading</p></placeholder>
              <state name="name" value="'Ready'" />
              <p>{$name}</p>
            </live>
            """);
        var browser = new Browser(App());
        browser.Get("/sign-in");
        var waiting = browser.Get("/edit/7");
        Assert.Contains("data-ks-live-lazy=\"visible\"", waiting.Body);
        Assert.Contains("<p>Loading</p>", waiting.Body);
        Assert.DoesNotContain("<p>Ready</p>", waiting.Body);
        PageResult Mount(PageResult page, int handler) =>
            browser.Send("POST", "/edit/7", headers: new() { ["x-kscript-live"] = Attribute(page, "data-ks-route") },
                json: Values.ToJson(new Dictionary<string, object>
                {
                    ["block"] = "item-7", ["handler"] = (double)handler, ["state"] = Attribute(page, "data-ks-state"),
                }));
        Assert.Equal(409, Mount(waiting, -1).Status);
        var loaded = Mount(waiting, -2);
        Assert.Equal(200, loaded.Status);
        Assert.Contains("<p>Ready</p>", loaded.Body);
        Assert.DoesNotContain("<p>Loading</p>", loaded.Body);
        Assert.Equal(409, Mount(loaded, -2).Status);
    }

    [Fact]
    public void NestedActionsUseTheirOwnStateAndRequireSignedParentState()
    {
        Write("_components/counter.html", """
            <live id="item-{$item}">
              <state name="factor" value="1" />
              <button on-click="factor += 1">Increase factor</button>
              <live id="total-{$item}">
                <state name="total" value="0" />
                <button on-click="total += $factor">Add factor</button>
                <p>{$total}</p>
              </live>
            </live>
            """);
        var browser = new Browser(App());
        browser.Get("/sign-in");
        PageResult page = browser.Get("/edit/7");
        var parts = page.Body.Split("data-ks-live=\"total-7\"", 2);
        Assert.Equal(2, parts.Length);
        var firstChild = new PageResult { Body = parts[1] };

        PageResult Update(string block, PageResult component, object parents = null) =>
            browser.Send("POST", "/edit/7", headers: new() { ["x-kscript-live"] = Attribute(page, "data-ks-route") },
                json: Values.ToJson(new Dictionary<string, object>
                {
                    ["block"] = block, ["handler"] = 0d, ["state"] = Attribute(component, "data-ks-state"),
                    ["args"] = Attribute(component, "data-ks-args-0"), ["parents"] = parents,
                }));

        static List<object> Parents(PageResult parent) => new()
        {
            new Dictionary<string, object> { ["id"] = "item-7", ["state"] = Attribute(parent, "data-ks-state") },
        };

        PageResult child = Update("total-7", firstChild, Parents(page));
        Assert.Equal(200, child.Status);
        Assert.Contains("<p>1</p>", child.Body);
        PageResult parent = Update("item-7", page);
        Assert.Equal(200, parent.Status);
        Assert.Contains("<p>3</p>", Update("total-7", child, Parents(parent)).Body);
        Assert.Equal(409, Update("total-7", child).Status);
        Assert.Equal(409, Update("total-7", child, Parents(child)).Status);
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
