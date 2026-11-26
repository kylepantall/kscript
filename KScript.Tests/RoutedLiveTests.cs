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
              <input bind="$draft"><p>{$count} {$draft}</p>
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RedrawReentersTheGetRouteAndReturnsOnlyTheKeyedComponent(bool automatic)
    {
        if (automatic)
        {
            Write("views/editor.html", """<h1>Editor</h1><counter live-id="item-{$request.params.id}" />""");
            Write("_components/counter.html", Read("_components/counter.html")
                .Replace("""<live id="item-{$item}">""", "").Replace("</live>", ""));
        }
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

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void FileWritesFollowTheControllerPermissionOnlyDuringTheAction(bool controller, bool permission, bool succeeds)
    {
        Write("kscript.json", Read("kscript.json").Replace("\"auth\":", $"\"write_files\": {permission.ToString().ToLowerInvariant()}, \"auth\":"));
        if (controller)
            Write("routes/web.kscript", Read("routes/web.kscript").Replace(
                """Get(path: "/edit/{id}", view: "editor", middleware: [auth:session, cache_response:1m])""",
                """Get(path: "/edit/{id}", middleware: [auth:session]) { return @view('editor') }"""));
        Write("_components/counter.html", "<import package='KScript.IO' />" + Read("_components/counter.html")
            .Replace("count += 1", "@write_text('saved.txt', 'saved')"));
        var browser = new Browser(App());
        browser.Get("/sign-in");
        var page = browser.Get("/edit/7");
        Assert.Equal(200, page.Status);
        string saved = Path.Combine(Dir, "saved.txt");
        Assert.False(File.Exists(saved));
        var action = Click(browser, page);
        if (succeeds)
        {
            Assert.True(action.Status == 200, action.Body);
            Assert.Equal("saved", File.ReadAllText(saved));
        }
        else
        {
            Assert.Equal(500, action.Status);
            Assert.Contains("files are read-only", action.Body);
            Assert.False(File.Exists(saved));
        }
    }

    [Theory]
    [InlineData("@live-action('move-note')")]
    [InlineData("live-action=\"move-note\"")]
    public void NamedActionInFencedComponentDispatchesItsSignedHandler(string action)
    {
        Write("views/editor.html", """<board-actions live-id="item-7" />""");
        Write("_components/board-actions.kscript", $$"""
            import KScript.Web
            type BoardActions extends Component {
              fn render(): Component {
                return ```html
                <state name="moves" value="0" />
                <state name="draft" value="''" />
                <input bind="$draft">
                <span hidden {{action}} on-action="moves += 1"></span>
                <p>{$moves}</p>
                ```
              }
            }
            """);
        var browser = new Browser(App());
        browser.Get("/sign-in");
        var page = browser.Get("/edit/7");
        Assert.Contains("data-ks-live-action=\"move-note\"", page.Body);
        Assert.DoesNotContain("@live-action", page.Body);
        Assert.Equal("0", Attribute(page, "data-ks-on-action"));
        var saved = Click(browser, page);
        Assert.Equal(200, saved.Status);
        Assert.Contains("<p>1</p>", saved.Body);
        Assert.Equal(409, Click(browser, page, args: "invalid").Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ClassStateRestoresTypedFieldsAndRefreshesServerProps(bool explicitRoot)
    {
        const string controls = """
            <input bind="$count"><input bind="$mode">
            <select bind="$numbers" multiple><option value="2">Two</option></select>
            <button on-click="count += 1">Add</button>
            <button on-click="$this->increment()">Add via the class</button>
            <button on-click="numbers[0] = 'bad'">Invalid item</button>
            <p>{$caption}|{$count}|{$this.count}|{$.count}|{$mode}|{$numbers->first()}</p>
            """;
        string template = explicitRoot ? "<live id='board'>\n" + controls + "\n</live>" : controls;
        Write("_components/counter.kscript", """
            import KScript.Web
            enum DisplayMode { compact, expanded }
            type Counter extends Component {
              caption: string
              private_data: map
              count: int = 0
              mode: DisplayMode = 'compact'
              numbers: int[] = [1]
              fn increment() { count += 1 }
              fn render(): Component {
                return ```html
                CONTROLS
                ```
              }
            }
            """.Replace("CONTROLS", template.Replace("\n", "\n    ")));
        string view = "<counter caption='fresh' private-data=\"{ { token: 'server only' } }\"" + (explicitRoot ? " />" : " live-id='board' />");
        Write("views/editor.html", view);
        var browser = new Browser(App());
        browser.Get("/sign-in");
        var page = browser.Get("/edit/7");
        Assert.True(page.Status == 200, page.Body);
        Assert.Contains("<p>fresh|0|0|0|compact|1</p>", page.Body);
        Assert.Single(Regex.Matches(page.Body, "data-ks-live="));
        string StatePayload(PageResult result) => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(Attribute(result, "data-ks-state").Split('.')[0]));
        Assert.DoesNotContain("server only", StatePayload(page));
        Assert.DoesNotContain("caption", StatePayload(page));

        PageResult Action(PageResult prior, int handler, object binds) => browser.Send("POST", "/edit/7",
            headers: new() { ["x-kscript-live"] = Attribute(prior, "data-ks-route") },
            json: System.Text.Json.JsonSerializer.Serialize(new
            {
                block = "board", handler, state = Attribute(prior, "data-ks-state"), args = Attribute(prior, "data-ks-args-" + handler), binds,
            }));

        Write("views/editor.html", view.Replace("fresh", "changed"));
        var updated = Action(page, 0, new { count = "3", mode = "expanded", numbers = new[] { "2" } });
        Assert.True(updated.Status == 200, updated.Body);
        Assert.Contains("<p>changed|4|4|4|expanded|2</p>", updated.Body);
        Assert.DoesNotContain("server only", StatePayload(updated));
        Assert.DoesNotContain("caption", StatePayload(updated));
        var second = Action(updated, 1, new { });
        Assert.True(second.Status == 200, second.Body);
        Assert.Contains("<p>changed|5|5|5|expanded|2</p>", second.Body);
        Assert.Equal(422, Action(second, 0, new { count = "no" }).Status);
        Assert.Equal(422, Action(second, 0, new { mode = "unknown" }).Status);
        Assert.Equal(422, Action(second, 0, new { numbers = new[] { "invalid" } }).Status);
        Assert.Equal(409, Action(second, 0, new { caption = "client value" }).Status);
        Assert.True(Action(second, 2, new { }).Status >= 400);
        Assert.Contains("<p>changed|6|6|6|expanded|2</p>", Action(second, 0, new { }).Body);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_inferred_child_keeps_its_identity_during_the_parent_probe_and_redraw(bool classFields)
    {
        Write("_components/counter.html", """
            <state name="count" value="0" /><state name="draft" value="''" />
            <input bind="$draft" /><button on-click="count += 1">Add</button>
            <mini-counter />
            """);
        Write("_components/mini-counter.html", """
            <state name="count" value="0" />
            <p>{$count}</p><button on-click="count += 1">Add</button>
            """);
        Write("views/editor.html", """<counter live-id="item-{$request.params.id}" />""");
        if (classFields)
        {
            Write("_components/counter.kscript", """
                import KScript.Web
                type Counter extends Component {
                  count: int = 0
                  draft: string = ''
                  fn render(): Component {
                    return ```html
                    <input bind="$draft"><button on-click="count += 1">Add</button>
                    <mini-counter parent-count="{$count}" />
                    ```
                  }
                }
                """);
            Write("_components/mini-counter.kscript", """
                import KScript.Web
                type MiniCounter extends Component {
                  parent_count: int
                  count: int = 0
                  fn render(): Component {
                    return ```html
                    <p>{$count}</p><small>Parent {$parent_count}</small><button on-click="count += 1">Add</button>
                    ```
                  }
                }
                """);
        }

        var browser = new Browser(App());
        browser.Get("/sign-in");
        var page = browser.Get("/edit/7");
        var children = Regex.Matches(page.Body, "data-ks-live=\"([^\"]+)\"");
        Assert.Equal(2, children.Count);
        string childId = children[1].Groups[1].Value;
        var parent = Click(browser, page);
        Assert.Equal(200, parent.Status);
        string marker = $"data-ks-live=\"{childId}\"";
        var child = new PageResult { Body = parent.Body.Split(marker, 2)[1] };
        var update = browser.Send("POST", "/edit/7", headers: new() { ["x-kscript-live"] = Attribute(parent, "data-ks-route") },
            json: System.Text.Json.JsonSerializer.Serialize(new
            {
                block = childId, handler = 0d, state = Attribute(child, "data-ks-state"),
                args = Attribute(child, "data-ks-args-0"),
                parents = new[] { new { id = "item-7", state = Attribute(parent, "data-ks-state") } },
            }));
        Assert.Equal(200, update.Status);
        Assert.Contains("<p>1</p>", update.Body);
        if (classFields) Assert.Contains("<small>Parent 1</small>", update.Body);
    }

    [Fact]
    public void SessionStatePersistsForOneBrowserAndLockedStateRejectsClientBindings()
    {
        Write("_components/counter.html", """
            <live id="item-{$item}">
              <state name="count" value="0" session="counter" locked />
              <state name="draft" value="''" />
              <input bind="$draft">
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
              <state name="draft" value="''" /><input bind="$draft">
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
              <input bind="$draft">
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
