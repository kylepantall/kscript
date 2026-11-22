using KScript.Http;
using KScript.Pages;
using KScript.Projects;
using KScript.Runtime;
using DefaultHttpContext = Microsoft.AspNetCore.Http.DefaultHttpContext;

namespace KScript.Tests;

/// <summary>The less common paths through KScript.Http: odd controllers, response methods, mistakes and the server.</summary>
public class HttpCoverageTests
{
    private static HttpApp App(TempSite site, string kscriptJson = null, StringWriter log = null)
    {
        ProjectConfig project = kscriptJson == null ? null : ProjectConfig.Parse(kscriptJson, Path.Combine(site.Root, "kscript.json"));
        return new HttpApp(site.Root, project, log);
    }

    private static PageResult Send(HttpApp app, string method, string path, string body = null, string query = null)
    {
        var q = new Dictionary<string, object>();
        if (query != null)
        {
            foreach (string pair in query.Split('&'))
            {
                var kv = pair.Split('=');
                q[kv[0]] = kv[1];
            }
        }
        var headers = new Dictionary<string, object>();
        if (body != null) headers["content-type"] = "application/json";
        return app.Handle(new Dictionary<string, object> { ["method"] = method, ["path"] = path, ["body"] = body ?? string.Empty, ["headers"] = headers, ["query"] = q });
    }

    private const string Controllers = """
        <kscript>
          <fn name="index" value="[1]" />
        </kscript>
        """;

    [Fact]
    public void ControllersCanBeLambdasCommandsAndInlineBlocks()
    {
        using var site = new TempSite(("routes.ks.xml", """
            <KScript.Routes>
              <import package="KScript.Math" as="m" />
              <let name="hello" value="fn($request) => 'Hello ' + $request.path" />
              <Get path="/lambda" to="hello" />
              <Get path="/command" to="response" />
              <Get path="/package" to="m.round" />
              <Get path="/silent"><let name="x" value="1" /></Get>
              <Get path="/object"><return value="$log" /></Get>
              <Get path="/redirect"><return value="@redirect('/x', status: 307)" /></Get>
              <Get path="/view"><return value="@view('page', { who: 'map' })" /></Get>
              <Get path="/bad-view"><return value="@view('../secret')" /></Get>
              <Get path="/broken-view"><return value="@view('broken')" /></Get>
              <Get path="/no-route"><return value="@route('nope')" /></Get>
              <Get path="/only-get"><return value="'x'" /></Get>
            </KScript.Routes>
            """), ("views/page.html", "<p>{$who}</p>"), ("views/broken.html", "<p>{ 1 / 'x' }</p>"));
        HttpApp app = App(site);

        Assert.Equal("Hello /lambda", Send(app, "GET", "/lambda").Body);
        Assert.Contains("\"path\":\"/command\"", Send(app, "GET", "/command").Body);
        Assert.Equal(500, Send(app, "GET", "/package").Status);
        Assert.Equal(204, Send(app, "GET", "/silent").Status);
        Assert.Equal(200, Send(app, "GET", "/object").Status);
        PageResult redirect = Send(app, "GET", "/redirect");
        Assert.Equal(307, redirect.Status);
        Assert.Equal("/x", redirect.Headers["Location"]);
        Assert.Contains("<p>map</p>", Send(app, "GET", "/view").Body);
        Assert.Contains("a view name", Send(app, "GET", "/bad-view").Body);
        Assert.Equal(500, Send(app, "GET", "/broken-view").Status);
        Assert.Contains("no route named", Send(app, "GET", "/no-route").Body);

        PageResult options = Send(app, "OPTIONS", "/only-get");
        Assert.Equal(204, options.Status);
        Assert.Equal("GET, HEAD, OPTIONS", options.Headers["Allow"]);
    }

    [Fact]
    public void ImportedValuesThatAreNotFunctionsAreExplained()
    {
        using var site = new TempSite(("routes.ks.xml", """
            <KScript.Routes>
              <import package="KScript.Math" as="m" />
              <Get path="/pi" to="m.pi" />
            </KScript.Routes>
            """));
        Assert.Contains("not a function", Send(App(site), "GET", "/pi").Body);
    }

    [Theory]
    [InlineData("""<Resource path="/x" to="nope" />""", "names a controller script imported")]
    [InlineData("""<import path="controllers/people.ks.xml" as="people" /><Resource path="/x" to="people" only="bogus" />""", "a resource action")]
    [InlineData("""<Get path="/p/{id}" where="id: '[a'" to="people.index" />""", "a valid pattern")]
    [InlineData("""<abort status="403" />""", "")]
    public void RouteFileMistakesAnswerWithAnError(string routes, string message)
    {
        using var site = new TempSite(("controllers/people.ks.xml", Controllers), ("routes.ks.xml", $"<KScript.Routes>{routes}<Get path=\"/\" to=\"people.index\" /></KScript.Routes>"));
        PageResult r = Send(App(site), "GET", "/");
        Assert.True(r.Status >= 400, r.Body);
        Assert.Contains(message, r.Body);
    }

    [Fact]
    public void AnAbortWhileTheRoutesLoadAnswersWithItsStatus()
    {
        using var site = new TempSite(("routes.ks.xml", """
            <KScript.Routes>
              <do>@abort(403, message: 'Closed.')</do>
              <Get path="/"><return value="'open'" /></Get>
            </KScript.Routes>
            """));
        PageResult r = Send(App(site), "GET", "/");
        Assert.Equal(403, r.Status);
        Assert.Contains("Closed.", r.Body);
    }

    [Fact]
    public void TypedRequestPartsAreNamedInTheirErrors()
    {
        using var site = new TempSite(("api.ks.xml", """
            <KScript.Routes>
              <type name="Need" fields="page: int" />
              <fn name="f" params="form: Need" value="1" />
              <fn name="i" params="input: Need" value="1" />
              <fn name="h" params="headers: Need" value="1" />
              <Get path="/f" to="f" />
              <Get path="/i" to="i" />
              <Get path="/h" to="h" />
            </KScript.Routes>
            """));
        HttpApp app = App(site);
        Assert.Contains("The form", Send(app, "GET", "/api/f").Body);
        Assert.Contains("The request's input", Send(app, "GET", "/api/i").Body);
        Assert.Contains("The request's headers", Send(app, "GET", "/api/h").Body);
    }

    [Fact]
    public void ResponsesHaveMembersAndMethods()
    {
        using var site = new TempSite(("routes.ks.xml", """
            <KScript.Routes>
              <Get path="/a">
                <let name="r" value="@response({ a: 1 }, headers: { 'X-A': 'b' })" />
                <return value="[$r.status, $r.body, $r.type, $r.headers, $r.is_ok, $r.body, $r.body, $r.headers, $r.is_ok, $r->header('X-A'), $r->json().a, $r->with_type('text/csv').type]" />
              </Get>
              <Get path="/b"><return value="@response('x')->nope()" /></Get>
              <Get path="/c"><return value="@response('x').nope" /></Get>
            </KScript.Routes>
            """));
        HttpApp app = App(site);
        Assert.Equal("[200,\"{\\\"a\\\":1}\",\"application/json; charset=utf-8\",{\"X-A\":\"b\"},true,\"{\\\"a\\\":1}\",\"{\\\"a\\\":1}\",{\"X-A\":\"b\"},true,\"b\",1,\"text/csv\"]", Send(app, "GET", "/a").Body);
        Assert.Equal(500, Send(app, "GET", "/b").Status);
        Assert.Equal(500, Send(app, "GET", "/c").Status);

        var response = new HttpResponse(201, "body", "text/plain");
        Assert.Equal("http_response", response.TypeName);
        Assert.Equal("body", response.ToText());
        Assert.Equal("201 text/plain", response.ToString());
        Assert.Equal("text/plain; charset=utf-8", HttpResponse.Text("hi").ContentType);
    }

    [Fact]
    public void ViewsMustStayInsideTheSite()
    {
        using var site = new TempSite(("routes.ks.xml", "<KScript.Routes />"));
        Assert.Throws<KScriptException>(() => App(site).FindView(" "));
        Assert.Equal("/a/{b}", new RoutePattern("/a/{b}").ToString());
    }

    [Fact]
    public async Task RequestsFromTheServerGoToTheRoutes()
    {
        static DefaultHttpContext Request(string method, string path)
        {
            var context = new DefaultHttpContext();
            context.Request.Method = method;
            context.Request.Path = path;
            return context;
        }

        using var none = new TempSite(("index.html", "<h1>Hi</h1>"));
        Assert.Null(await App(none).HandleAsync(Request("GET", "/").Request));

        using var site = new TempSite(("routes.ks.xml", """<KScript.Routes><Get path="/hi"><return value="'hi ' + $request.ip" /></Get></KScript.Routes>"""));
        HttpApp app = App(site);
        Assert.Null(await app.HandleAsync(Request("GET", "/_ks/live").Request));
        Assert.Null(await app.HandleAsync(Request("GET", "/other").Request));
        Assert.Equal("hi 127.0.0.1", (await app.HandleAsync(Request("GET", "/hi").Request)).Body);

        site.Write("routes.ks.xml", "<KScript.Routes><Get path=\"/hi\" to=\"missing\" /></KScript.Routes>");
        PageResult broken = await App(site).HandleAsync(Request("GET", "/hi").Request);
        Assert.Contains("No function named", broken.Body);
    }

    [Fact]
    public async Task TheServerReportsItsRoutesWhenItStarts()
    {
        using var site = new TempSite(("routes.ks.xml", """<KScript.Routes><Get path="/hi"><return value="'hi'" /></Get></KScript.Routes>"""));
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        async Task Run(StringWriter log)
        {
            try
            {
                await HttpServer.RunAsync(site.Root, null, 0, "127.0.0.1", log, stop.Token);
            }
            catch (OperationCanceledException)
            {
                // Stopped before it started, which is all this needs.
            }
        }

        var log = new StringWriter();
        await Run(log);
        Assert.Contains("Routes: 1 from routes.ks.xml", log.ToString());

        site.Write("routes.ks.xml", "<KScript.Routes><Get path=\"/hi\" to=\"missing\" /></KScript.Routes>");
        log = new StringWriter();
        await Run(log);
        Assert.Contains("ERROR  The routes have an error", log.ToString());
    }

    [Fact]
    public void KscriptRoutesExplainsWhatIsMissing()
    {
        var o = new StringWriter();
        var e = new StringWriter();
        using var empty = new TempSite(("index.html", "x"));
        Assert.Equal(1, HttpServer.ListRoutes(empty.Root, null, o, e));
        Assert.Contains("There are no route files", e.ToString());

        using var missing = new TempSite(("index.html", "x"));
        ProjectConfig project = ProjectConfig.Parse("""{ "http": { "routes": ["nope.ks.xml"] } }""", Path.Combine(missing.Root, "kscript.json"));
        e = new StringWriter();
        Assert.Equal(1, HttpServer.ListRoutes(missing.Root, project, o, e));
        Assert.Contains("there is no route file nope.ks.xml", e.ToString());

        using var broken = new TempSite(("routes.ks.xml", "<KScript.Routes><Get path=\"/hi\" to=\"missing\" /></KScript.Routes>"));
        e = new StringWriter();
        Assert.Equal(1, HttpServer.ListRoutes(broken.Root, null, o, e));
        Assert.Contains("No function named 'missing'", e.ToString());
    }

    [Fact]
    public void BuiltInMiddlewareNamesAreChecked()
    {
        using var site = new TempSite(("routes.ks.xml", "<KScript.Routes />"));
        var ctx = new RequestContext(App(site), new Dictionary<string, object>());
        var ex = Assert.Throws<KScriptException>(() => BuiltInMiddleware.Run(new MiddlewareRef("nope", null, null), ctx, new Dictionary<string, object>(), () => null));
        Assert.Contains("no built-in middleware 'nope'", ex.Message);
    }
}
