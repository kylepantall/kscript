using KScript.Pages;
using KScript.Runtime;
using Microsoft.AspNetCore.Http;

namespace KScript.Tests;

/// <summary>The less common corners of kscript serve: routing, the request map, errors and template parsing.</summary>
public class WebCoverageTests
{
    private static DefaultHttpContext Request(string method, string url, string body = null, string contentType = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        int q = url.IndexOf('?');
        context.Request.Path = q < 0 ? url : url.Substring(0, q);
        if (q >= 0) context.Request.QueryString = new QueryString(url.Substring(q));
        if (body != null)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(body);
            context.Request.Body = new MemoryStream(bytes);
            context.Request.ContentLength = bytes.Length;
        }
        if (contentType != null) context.Request.ContentType = contentType;
        return context;
    }

    private static Task<PageResult> Handle(TempSite site, string method, string url, string body = null, string contentType = null) =>
        PageServer.HandleAsync(new PageSite(site.Root), Request(method, url, body, contentType).Request);

    private static string Render(string template) => PageRenderer.RenderString(template).Body;

    private static string RenderFails(string template) => Assert.ThrowsAny<Exception>(() => Render(template)).Message;

    [Fact]
    public void Dynamic_folders_and_folders_without_an_index()
    {
        using var site = new TempSite(
            ("docs/guide/index.html", "guide"),
            ("people/[id]/edit.html", "edit {$params.id}"),
            ("people/[id]/index.html", "person {$params.id}"));
        var routes = new PageSite(site.Root);

        Assert.Equal(PageRoute.RouteKind.NotFound, routes.Resolve("/docs").Kind);
        PageRoute edit = routes.Resolve("/people/7/edit");
        Assert.Equal("people/[id]/edit.html", edit.File);
        Assert.Equal("7", edit.Params["id"]);
        Assert.Equal("people/[id]/index.html", routes.Resolve("/people/7").File);
        Assert.Equal(PageRoute.RouteKind.NotFound, routes.Resolve("/people/7/delete").Kind);
    }

    [Fact]
    public async Task Requests_carry_their_form_body_and_static_files()
    {
        using var site = new TempSite(
            ("echo.html", "<p>{$request.method} {$request.form.name ?? '-'} {$request.body}</p>"),
            ("logo.png", "png"),
            ("data.unknownext", "?"));

        PageResult form = await Handle(site, "POST", "/echo", "name=Ann", "application/x-www-form-urlencoded");
        Assert.Equal("<p>POST Ann name=Ann</p>", form.Body);
        PageResult json = await Handle(site, "POST", "/echo", "{\"a\":1}", "application/json");
        Assert.Contains("{&quot;a&quot;:1}", json.Body);

        PageResult png = await Handle(site, "GET", "/logo.png");
        Assert.Equal("image/png", png.ContentType);
        Assert.EndsWith("logo.png", png.FilePath);
        Assert.Equal("application/octet-stream", (await Handle(site, "GET", "/data.unknownext")).ContentType);
    }

    [Fact]
    public async Task The_live_runtime_and_its_endpoint()
    {
        using var site = new TempSite(("index.html", "<p>hi</p>"), ("broken.html", "<live id=\"b\"><state name=\"n\" value=\"0\" /><button on-click=\"n = @nope()\">x</button></live>"));

        PageResult script = await Handle(site, "GET", "/_ks/live.js");
        Assert.StartsWith("text/javascript", script.ContentType);
        Assert.Contains("data-ks-live", script.Body);

        Assert.Equal(405, (await Handle(site, "GET", "/_ks/live")).Status);
        Assert.Equal(409, (await Handle(site, "POST", "/_ks/live", "{\"page\":\"/\",\"block\":\"c\",\"handler\":0,\"state\":\"abc.%%%\"}", "application/json")).Status);
        Assert.Contains("Invalid live request", Assert.Throws<PageException>(() => PageServer.HandleLive(new PageSite(site.Root), "{ nope", new Dictionary<string, object>())).Message);
        Assert.Equal(404, PageServer.HandleLive(new PageSite(site.Root), "{\"page\":\"/missing\",\"block\":\"c\",\"handler\":0}", new Dictionary<string, object>()).Status);
    }

    [Fact]
    public async Task Passkey_buttons_share_the_browser_runtime_and_keep_their_own_status()
    {
        using var site = new TempSite(("index.html", """
            <body><button passkey="register">Add</button>
            <button type="button" passkey="login" passkey-redirect="/account">Sign in</button></body>
            """));
        var page = await Handle(site, "GET", "/");
        Assert.Contains("<button type=\"button\" data-ks-passkey=\"register\">Add</button><span role=\"status\" data-ks-passkey-status></span>", page.Body);
        Assert.Contains("data-ks-passkey=\"login\" data-ks-passkey-redirect=\"/account\"", page.Body);
        Assert.Equal(2, page.Body.Split("/_ks/live.js").Length);
        Assert.Contains("navigator.credentials.create", (await Handle(site, "GET", "/_ks/live.js")).Body);
        Assert.DoesNotContain("/_ks/live.js", Render("<button>Save</button>"));
    }

    [Theory]
    [InlineData("<a passkey='login'>Sign in</a>")]
    [InlineData("<button passkey='register' type='submit'>Add</button>")]
    [InlineData("<button passkey='update'>Update</button>")]
    public void Invalid_passkey_buttons_have_a_template_error(string template)
        => Assert.Contains("passkey", RenderFails(template));

    [Fact]
    public async Task Not_found_pages_and_error_pages()
    {
        using var site = new TempSite(("_404.html", "<h1>Lost: {$request.path}</h1>"), ("bad.html", "<p>{ @nope() }</p>"));
        PageResult missing = await Handle(site, "GET", "/where");
        Assert.Equal(404, missing.Status);
        Assert.Equal("<h1>Lost: /where</h1>", missing.Body);

        PageResult located = PageServer.ErrorPage(new PageException("it broke") { Location = "bad.html" });
        Assert.Equal(500, located.Status);
        Assert.Contains("bad.html: Page: it broke", located.Body);
        // A .NET exception shows as a KScript error; its stack trace only with --debug.
        string odd = PageServer.ErrorPage(new InvalidOperationException("odd")).Body;
        Assert.Contains("Error: odd", odd);
        Assert.DoesNotContain("System.InvalidOperationException", odd);
        Assert.DoesNotContain("   at ", odd);
        KScript.Runtime.ErrorReport.Debug = true;
        try
        {
            Assert.Contains("System.InvalidOperationException: odd", PageServer.ErrorPage(new InvalidOperationException("odd")).Body);
        }
        finally
        {
            KScript.Runtime.ErrorReport.Debug = false;
        }
    }

    [Fact]
    public async Task The_server_sends_files_and_error_pages_and_stops()
    {
        using var site = new TempSite(("index.html", "<p>{ @nope() }</p>"), ("site.txt", "plain text"),
            ("custom.html", """<respond header-server="my-app">custom</respond>"""));
        int port;
        {
            var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            l.Start();
            port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
        }
        using var stop = new CancellationTokenSource();
        var log = new StringWriter();
        Task<int> server = Task.Run(() => PageServer.RunAsync(site.Root, port, "127.0.0.1", TextWriter.Synchronized(log), stop.Token));

        using var http = new HttpClient();
        string text = null;
        for (int i = 0; i < 50 && text == null; i++)
        {
            try { text = await http.GetStringAsync($"http://127.0.0.1:{port}/site.txt"); }
            catch (HttpRequestException) { await Task.Delay(100); }
        }
        Assert.Equal("plain text", text);
        using var custom = await http.GetAsync($"http://127.0.0.1:{port}/custom");
        Assert.Equal("my-app", Assert.Single(custom.Headers.GetValues("Server")));
        using var broken = await http.GetAsync($"http://127.0.0.1:{port}/");
        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, broken.StatusCode);
        Assert.Equal("kscript", Assert.Single(broken.Headers.GetValues("Server")));
        Assert.Contains("KScript error", await broken.Content.ReadAsStringAsync());

        stop.Cancel();
        Assert.Equal(0, await server);
        Assert.Contains("Serving", log.ToString());
    }

    [Fact]
    public void Templates_parse_comments_bare_attributes_and_stray_brackets()
    {
        Assert.Equal("<!-- note {$x} --><p>1</p>", Render("<!-- note {$x} --><p>{1}</p>"));
        Assert.Equal("<!-- never closed", Render("<!-- never closed"));
        Assert.Equal("<input type=\"text\" value=\"5\">", Render("<input type=text value={5}>"));
        Assert.Equal("a <b", Render("a <b"));
        Assert.Equal("1 < 2", Render("1 < 2"));
        Assert.Equal("<select><optgroup label=\"a\"><option>1</option></optgroup><optgroup label=\"b\"><option>2</option></optgroup></select>",
            Render("<select><optgroup label=\"a\"><option>1<optgroup label=\"b\"><option>2</select>"));
        Assert.Equal("<table><thead><tr><th>h</th></tr></thead><tbody><tr><td>1</td></tr></tbody></table>",
            Render("<table><thead><tr><th>h<tbody><tr><td>1</table>"));
        Assert.Equal("a\nb", Render("{ 'a\nb' }"));
        Assert.Contains("never closed", RenderFails("{ [1] "));
        Assert.Equal("2", Render("{ { a: 2 }.a }"));
        Assert.Contains("never closed", RenderFails("{ a <b>c</b>"));
    }

    [Fact]
    public void Control_elements_after_raw_text_keep_their_line()
    {
        string html = Render("<!-- c --><if test=\"true\">yes</if>\n<p>after</p>");
        Assert.Equal("<!-- c -->yes\n<p>after</p>", html);
    }

    [Fact]
    public void Elements_explain_where_they_belong()
    {
        Assert.Contains("<case> must be inside <match>", RenderFails("<case is=\"1\">x</case>"));
        Assert.Contains("<default> must be inside <match>", RenderFails("<default>x</default>"));
        Assert.Contains("<case> needs an is or when attribute", RenderFails("<match value=\"1\"><case>x</case></match>"));
        Assert.Contains("Only <case> and <default> can go inside <match>, not <p>", RenderFails("<match value=\"1\"><p>x</p></match>"));
        Assert.Contains("<include> needs a path attribute", RenderFails("<include />"));
        Assert.Contains("<for> can't loop over", RenderFails("<for each=\"x\" in=\"true\">{$x}</for>"));
        Assert.Contains("<import> needs a package or path", RenderFails("<import as=\"m\" />"));
    }

    [Fact]
    public void Loops_run_over_nothing_numbers_and_text()
    {
        Assert.Equal("", Render("<for each=\"x\" in=\"null\">{$x}</for>"));
        Assert.Equal("012", Render("<for each=\"x\" in=\"3\">{$x}</for>"));
        Assert.Equal("a-b-", Render("<for each=\"c\" in=\"'ab'\">{$c}-</for>"));
    }

    [Fact]
    public void Includes_slots_responses_and_bound_controls()
    {
        using var site = new TempSite(
            ("index.html", "<div>\n  <include path=\"_part.html\" who=\"Ann\" />\n</div>"),
            ("_part.html", "<b>{$who}</b>"),
            ("empty.html", "<layout path=\"_layout.html\"></layout>"),
            ("_layout.html", "<main>\n  <slot />\n</main>"),
            ("api.html", "<respond type=\"text/plain\" header-x-kind=\"demo\">ok</respond>"),
            ("form.html", "<live id=\"f\"><state name=\"on\" value=\"true\" /><state name=\"note\" value=\"'hi'\" /><input type=\"checkbox\" bind=\"$on\" /><textarea bind=\"$note\"></textarea><span hidden>x</span></live>"),
            ("tick.html", "<live id=\"t\"><state name=\"n\" value=\"0\" /><span on-tick=\"n += 1\" every=\"{ 5ms }\">{$n}</span></live>"));

        Assert.Equal("<div>\n<b>Ann</b></div>", site.Render("index.html").Body);
        Assert.Equal("<main>\n</main>", site.Render("empty.html").Body);

        PageResult api = site.Render("api.html");
        Assert.Equal("ok", api.Body);
        Assert.Equal("text/plain", api.ContentType);
        Assert.Equal("demo", api.Headers["x-kind"]);

        string form = site.Render("form.html").Body;
        Assert.Contains("data-ks-bind=\"on\" checked name=\"on\"", form);
        Assert.Contains("name=\"note\">hi</textarea>", form);
        Assert.Contains("<span hidden>x</span>", form);
        Assert.Contains("data-ks-every=\"16\"", site.Render("tick.html").Body);
    }

    [Fact]
    public void Live_blocks_keep_bare_attributes_and_report_handler_errors()
    {
        using var site = new TempSite(
            ("index.html", "<live id=\"b\" hidden><state name=\"n\" value=\"0\" /><button on-click=\"n = @nope()\">{$n}</button></live>"),
            ("index2.html", "<live id=\"b\"><state name=\"n\" value=\"0\" /><button on-click=\"n += 1\">{$n}</button></live>"));

        string html = site.Render("index.html").Body;
        Assert.Contains("data-ks-live=\"b\"", html);
        Assert.Contains(" hidden id=\"b\"", html);

        var m = System.Text.RegularExpressions.Regex.Match(html, "data-ks-state=\"([^\"]*)\"");
        string state = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value);
        string json = Values.ToJson(new Dictionary<string, object> { ["page"] = "/index", ["block"] = "b", ["handler"] = 0d, ["state"] = state });
        PageResult failed = PageServer.HandleLive(new PageSite(site.Root), json, new Dictionary<string, object>());
        Assert.Equal(500, failed.Status);
        Assert.Contains("nope", failed.Body);
    }

    [Fact]
    public void Front_matter_reads_quoted_values_and_lists()
    {
        var (meta, _, _) = PageMarkdown.SplitFrontMatter("---\ntitle: \"Quoted: yes\"\nother: 'single'\ntags: [a, 2, ~]\n---\nbody");
        Assert.Equal("Quoted: yes", meta["title"]);
        Assert.Equal("single", meta["other"]);
        Assert.Equal(new List<object> { "a", 2d }, meta["tags"]);
    }

    [Fact]
    public void Rendering_from_text_handles_responses_markdown_and_odd_names()
    {
        PageResult respond = PageRenderer.RenderString("<respond status=\"201\">made</respond>");
        Assert.Equal(201, respond.Status);
        Assert.Equal("made", respond.Body);
        Assert.Equal("<h1 id=\"hi-2\">Hi 2</h1>", PageRenderer.RenderString("# Hi {1 + 1}", markdown: true).Body.Trim());
        Assert.Equal("<p>x</p>", PageRenderer.RenderString("<p>x</p>", fileName: "../outside.html").Body);
        Assert.Contains("unclosed \" in attribute 'class' of <p>", Assert.Throws<PageException>(() => Render("<p class=\"x>")).Message);
    }

    [Fact]
    public void While_loops_slots_markdown_components_and_attribute_braces()
    {
        Assert.Equal("12", Render("<let name=\"i\" value=\"0\" /><while test=\"$i < 2\"><do>i += 1</do>{$i}</while>"));
        Assert.Equal("<main>fallback</main>", Render("<main><slot>fallback</slot></main>"));
        Assert.Equal("<p title=\"2\">x</p>", Render("<p title=\"{ { a: 2 }.a }\">x</p>"));
        Assert.Contains("never closed", RenderFails("<p title=\"{ 'a' \">x</p>"));

        using var site = new TempSite(("index.html", "<site-note text=\"hi\" />"), ("_components/site-note.md", "**{$text}**"));
        Assert.Contains("<strong>hi</strong>", site.Render("index.html").Body);
    }

    [Fact]
    public void Live_blocks_copy_their_attributes_and_reject_forged_arguments()
    {
        using var site = new TempSite(("index.html", "<live id=\"l\" class=\"box {1}\"><state name=\"items\" value=\"['a']\" /><for each=\"x\" in=\"$items\"><i on-click=\"items = []\">{$x}</i></for></live>"));
        string html = site.Render("index.html").Body;
        Assert.Contains(" class=\"box 1\" id=\"l\"", html);

        string state = System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Match(html, "data-ks-state=\"([^\"]*)\"").Groups[1].Value);
        string json = Values.ToJson(new Dictionary<string, object> { ["page"] = "/index", ["block"] = "l", ["handler"] = 0d, ["state"] = state, ["args"] = "e30=.forged" });
        Assert.Equal(409, PageServer.HandleLive(new PageSite(site.Root), json, new Dictionary<string, object>()).Status);
        string stale = Values.ToJson(new Dictionary<string, object> { ["page"] = "/index", ["block"] = "l", ["handler"] = 99d, ["state"] = state });
        Assert.Equal(409, PageServer.HandleLive(new PageSite(site.Root), stale, new Dictionary<string, object>()).Status);
    }
}
