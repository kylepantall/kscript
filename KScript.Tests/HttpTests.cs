using KScript.Http;
using KScript.Pages;
using KScript.Projects;
using KScript.Runtime;

namespace KScript.Tests;

public class RoutePatternTests
{
    [Theory]
    [InlineData("/people/{id}", "/people/42", "id=42")]
    [InlineData("/people/{id}", "/People/42/", "id=42")]
    [InlineData("/people/{id}/posts/{post}", "/people/1/posts/hello", "id=1,post=hello")]
    [InlineData("/search/{term?}", "/search", "term=")]
    [InlineData("/search/{term?}", "/search/cats", "term=cats")]
    [InlineData("/files/{path*}", "/files/a/b/c.txt", "path=a/b/c.txt")]
    [InlineData("/files/{path*}", "/files", "path=")]
    [InlineData("/", "/", "")]
    public void Matches(string pattern, string path, string expected)
    {
        Assert.True(new RoutePattern(pattern).TryMatch(path, out var values));
        Assert.Equal(expected, string.Join(",", values.Select(kv => kv.Key + "=" + kv.Value)));
    }

    [Theory]
    [InlineData("/people/{id}", "/people")]
    [InlineData("/people/{id}", "/people/1/2")]
    [InlineData("/people", "/peoples")]
    [InlineData("/", "/x")]
    public void DoesNotMatch(string pattern, string path) => Assert.False(new RoutePattern(pattern).TryMatch(path, out _));

    [Theory]
    [InlineData("id: int", "/p/12", true)]
    [InlineData("id: int", "/p/abc", false)]
    [InlineData("id: number", "/p/1.5", true)]
    [InlineData("id: slug", "/p/hello-world", true)]
    [InlineData("id: slug", "/p/Hello_World", false)]
    [InlineData("id: uuid", "/p/0f8fad5b-d9cb-469f-a165-70867728950e", true)]
    [InlineData("id: '[a-c]+'", "/p/abc", true)]
    [InlineData("id: '[a-c]+'", "/p/abd", false)]
    public void WhereConstrainsParameters(string where, string path, bool matches) =>
        Assert.Equal(matches, new RoutePattern("/p/{id}", where).TryMatch(path, out _));

    [Theory]
    [InlineData("/a/{id}/{id}", "twice")]
    [InlineData("/a/{rest*}/b", "must come last")]
    [InlineData("/a/{x?}/b", "optional")]
    [InlineData("/a/x{id}", "whole part")]
    [InlineData("/a/{x-y}", "isn't a parameter")]
    public void BadPatternsAreExplained(string pattern, string message)
    {
        var ex = Assert.Throws<KScriptException>(() => new RoutePattern(pattern));
        Assert.Contains(message, ex.Message);
    }

    [Fact]
    public void WhereMustNameAParameter()
    {
        var ex = Assert.Throws<KScriptException>(() => new RoutePattern("/a/{id}", "slug: slug"));
        Assert.Contains("has no {slug}", ex.Message);
    }

    [Fact]
    public void BuildsUrls()
    {
        var pattern = new RoutePattern("/people/{id}/{tab?}");
        Assert.Equal("/people/3", pattern.Url(new Dictionary<string, object> { ["id"] = 3.0 }));
        Assert.Equal("/people/3/posts?page=2", pattern.Url(new Dictionary<string, object> { ["id"] = 3.0, ["tab"] = "posts", ["page"] = 2.0 }));
        Assert.Equal("/people/a%20b", pattern.Url(new Dictionary<string, object> { ["id"] = "a b" }));
        Assert.Throws<KScriptException>(() => pattern.Url(new Dictionary<string, object>()));
    }

    [Theory]
    [InlineData("/admin", "/people", "/admin/people")]
    [InlineData("/admin/", "/", "/admin")]
    [InlineData("", "people", "/people")]
    [InlineData("/", "/", "/")]
    public void JoinsPrefixes(string prefix, string path, string expected) => Assert.Equal(expected, RoutePattern.Join(prefix, path));
}

public class HttpAppTests
{
    private const string People = """
        <kscript>
          <import package="KScript.Http" />
          <const name="people" value="[{ id: 1, name: 'Joan' }, { id: 2, name: 'Ellie' }]" />

          <fn name="index" params="query">
            <return value="$people |> filter(fn($p) => $query.name == null or $p.name == $query.name)" />
          </fn>
          <fn name="show" params="id">
            <return value="$people->find(fn($p) => $p.id == $id) ?? @abort(404, message: 'No such person.')" />
          </fn>
          <fn name="store" params="json">
            <return value="@response({ id: 3, name: $json.name }, status: 201, headers: { Location: @route('people.show', id: 3) })" />
          </fn>
          <fn name="update" params="id, input">
            <return value="{ id: $id, name: $input.name }" />
          </fn>
          <fn name="destroy" params="id">
            <return value="@response(null, status: 204)" />
          </fn>
          <fn name="page" params="id">
            <return value="@view('people/show', person: @show($id))" />
          </fn>
          <fn name="echo_request" params="request">
            <return value="{ method: $request.method, path: $request.path, ip: $request.ip, route: $request.route }" />
          </fn>
          <fn name="broken">
            <return value="1 / 'x'" />
          </fn>
        </kscript>
        """;

    private static TempSite Site(string routes, params (string Path, string Content)[] more)
    {
        var files = new List<(string, string)> { ("controllers/people.ks.xml", People), ("views/people/show.html", "<h1>{$person.name}</h1>") };
        if (routes != null) files.Add(("routes.ks.xml", routes));
        files.AddRange(more);
        return new TempSite(files.ToArray());
    }

    private static HttpApp App(TempSite site, string kscriptJson = null, StringWriter log = null)
    {
        ProjectConfig project = kscriptJson == null ? null : ProjectConfig.Parse(kscriptJson, Path.Combine(site.Root, "kscript.json"));
        return new HttpApp(site.Root, project, log) { Environment = name => name == "API_TOKEN" ? "s3cret" : null };
    }

    private static PageResult Send(HttpApp app, string method, string path, string body = null, Dictionary<string, object> headers = null, string query = null)
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
        headers ??= new Dictionary<string, object>();
        if (body != null) headers.TryAdd("content-type", "application/json");
        return app.Handle(new Dictionary<string, object> { ["method"] = method, ["path"] = path, ["body"] = body ?? string.Empty, ["headers"] = headers, ["query"] = q });
    }

    private const string Routes = """
        <KScript.Routes>
          <import src="controllers/people.ks.xml" as="people" />
          <Get path="/people" to="people.index" name="people.index" />
          <Get path="/people/{id}" to="people.page" name="people.show" where="id: int" />
          <Post path="/people" to="people.store" />
          <Get path="/whoami" to="people.echo_request" name="whoami" />
          <Get path="/broken" to="people.broken" />
          <Get path="/old" redirect="/people" status="301" />
          <Get path="/about" view="about" />
          <Get path="/ping"><return value="'pong'" /></Get>
          <Get path="/hello/{name}"><return value="'Hello, ' + $name" /></Get>
          <Any path="/anything"><return value="$request.method" /></Any>
          <Route methods="get, post" path="/form"><return value="{ method: $request.method }" /></Route>
        </KScript.Routes>
        """;

    [Fact]
    public void ControllersReturningMapsAndListsAnswerWithJson()
    {
        using var site = Site(Routes);
        var app = App(site);
        PageResult r = Send(app, "GET", "/people");
        Assert.Equal(200, r.Status);
        Assert.StartsWith("application/json", r.ContentType);
        Assert.Equal("[{\"id\":1,\"name\":\"Joan\"},{\"id\":2,\"name\":\"Ellie\"}]", r.Body);
        Assert.Equal("[{\"id\":2,\"name\":\"Ellie\"}]", Send(app, "GET", "/people", query: "name=Ellie").Body);
    }

    [Fact]
    public void ViewsRenderWithTheControllersData()
    {
        using var site = Site(Routes, ("views/about.html", "<p>About {$request.path}</p>"));
        var app = App(site);
        PageResult r = Send(app, "GET", "/people/2");
        Assert.Equal(200, r.Status);
        Assert.StartsWith("text/html", r.ContentType);
        Assert.Contains("<h1>Ellie</h1>", r.Body);
        Assert.Contains("<p>About /about</p>", Send(app, "GET", "/about").Body);
    }

    [Fact]
    public void AbortAnswersWithTheStatus()
    {
        using var site = Site(Routes);
        PageResult r = Send(App(site), "GET", "/people/9");
        Assert.Equal(404, r.Status);
        Assert.Contains("No such person.", r.Body);
        Assert.StartsWith("text/html", r.ContentType);
    }

    [Fact]
    public void WhereRulesAndUnknownPathsFallThroughToPages()
    {
        using var site = Site(Routes);
        var app = App(site);
        Assert.Null(Send(app, "GET", "/people/abc"));
        Assert.Null(Send(app, "GET", "/nothing-here"));
    }

    [Fact]
    public void JsonBodiesAndNamedRoutes()
    {
        using var site = Site(Routes);
        PageResult r = Send(App(site), "POST", "/people", "{\"name\": \"Kyle\"}");
        Assert.Equal(201, r.Status);
        Assert.Equal("/people/3", r.Headers["Location"]);
        Assert.Equal("{\"id\":3,\"name\":\"Kyle\"}", r.Body);
    }

    [Fact]
    public void InvalidJsonIsABadRequest()
    {
        using var site = Site(Routes);
        PageResult r = Send(App(site), "POST", "/people", "{nope");
        Assert.Equal(400, r.Status);
        Assert.Contains("valid JSON", r.Body);
    }

    [Fact]
    public void TheRequestIsAvailableToControllers()
    {
        using var site = Site(Routes);
        PageResult r = Send(App(site), "get", "/whoami");
        Assert.Equal("{\"method\":\"GET\",\"path\":\"/whoami\",\"ip\":\"127.0.0.1\",\"route\":\"whoami\"}", r.Body);
    }

    [Fact]
    public void InlineRoutesRedirectsAndMethods()
    {
        using var site = Site(Routes);
        var app = App(site);
        Assert.Equal("pong", Send(app, "GET", "/ping").Body);
        Assert.Equal("Hello, Kyle", Send(app, "GET", "/hello/Kyle").Body);
        PageResult moved = Send(app, "GET", "/old");
        Assert.Equal(301, moved.Status);
        Assert.Equal("/people", moved.Headers["Location"]);
        Assert.Equal("PUT", Send(app, "PUT", "/anything").Body);
        Assert.Equal("{\"method\":\"POST\"}", Send(app, "POST", "/form").Body);
        Assert.Equal(string.Empty, Send(app, "HEAD", "/ping").Body);
    }

    [Fact]
    public void WrongMethodIs405WithAllow()
    {
        using var site = Site(Routes);
        PageResult r = Send(App(site), "DELETE", "/people");
        Assert.Equal(405, r.Status);
        Assert.Equal("GET, POST, HEAD", r.Headers["Allow"]);
    }

    [Fact]
    public void ControllerErrorsAre500WithTheLocation()
    {
        using var site = Site(Routes);
        var log = new StringWriter();
        PageResult r = Send(App(site, log: log), "GET", "/broken");
        Assert.Equal(500, r.Status);
        Assert.Contains("people.ks.xml:", r.Body);
        Assert.Contains("people.ks.xml:", log.ToString());
    }

    [Fact]
    public void ApiFilesArePrefixedAndAnswerErrorsWithJson()
    {
        using var site = Site(null, ("api.ks.xml", """
            <KScript.Routes>
              <import src="controllers/people.ks.xml" as="people" />
              <Get path="/people/{id}" to="people.show" />
            </KScript.Routes>
            """));
        var app = App(site);
        Assert.Equal("{\"id\":1,\"name\":\"Joan\"}", Send(app, "GET", "/api/people/1").Body);
        PageResult missing = Send(app, "GET", "/api/people/7");
        Assert.Equal(404, missing.Status);
        Assert.Equal("{\"error\":\"No such person.\",\"status\":404}", missing.Body);
        PageResult noRoute = Send(app, "GET", "/api/nothing");
        Assert.Equal(404, noRoute.Status);
        Assert.StartsWith("application/json", noRoute.ContentType);
        Assert.Null(Send(app, "GET", "/people/1"));
    }

    [Fact]
    public void ResourcesMapToTheControllersFunctions()
    {
        using var site = Site("""
            <KScript.Routes>
              <import src="controllers/people.ks.xml" as="people" />
              <Resource path="/people" to="people" />
            </KScript.Routes>
            """);
        var app = App(site);
        Assert.Equal(new[] { "GET /people people.index", "POST /people people.store", "GET /people/{id} people.show", "PUT|PATCH /people/{id} people.update", "DELETE /people/{id} people.destroy" },
            app.LoadRoutes().Routes.Select(r => $"{r} {r.Name}"));
        Assert.Equal("{\"id\":\"2\",\"name\":\"Ann\"}", Send(app, "PATCH", "/people/2", "{\"name\": \"Ann\"}").Body);
        Assert.Equal(204, Send(app, "DELETE", "/people/2").Status);
    }

    [Fact]
    public void ResourceOnlyAndExcept()
    {
        using var site = Site("""
            <KScript.Routes>
              <import src="controllers/people.ks.xml" as="people" />
              <Resource path="/a" to="people" only="index, show" name="a" />
              <Resource path="/b" to="people" except="destroy, store, update" name="b" />
            </KScript.Routes>
            """);
        Assert.Equal(new[] { "a.index", "a.show", "b.index", "b.show" }, App(site).LoadRoutes().Routes.Select(r => r.Name));
    }

    private const string Middleware = """
        <kscript>
          <import package="KScript.Http" />
          <fn name="stamp" params="request, next">
            <return value="$next($request)->with_header('X-Stamp', 'yes')" />
          </fn>
          <fn name="admins_only" params="request, next">
            <if test="$request.query.admin != 'yes'">
              <return value="@response('No', status: 403)" />
            </if>
            <return value="$next($request)" />
          </fn>
          <fn name="forgetful" params="request, next">
            <do>$next($request)</do>
          </fn>
        </kscript>
        """;

    [Fact]
    public void MiddlewareWrapsAndCanStopRequests()
    {
        using var site = Site("""
            <KScript.Routes>
              <import src="middleware.ks.xml" />
              <Middleware use="stamp" />
              <Get path="/open"><return value="'open'" /></Get>
              <Group prefix="/admin" middleware="admins_only" name="admin.">
                <Get path="/" name="home"><return value="'secret'" /></Get>
              </Group>
              <Get path="/forgetful" middleware="forgetful"><return value="'x'" /></Get>
            </KScript.Routes>
            """, ("middleware.ks.xml", Middleware));
        var app = App(site);
        PageResult open = Send(app, "GET", "/open");
        Assert.Equal("yes", open.Headers["X-Stamp"]);

        PageResult denied = Send(app, "GET", "/admin");
        Assert.Equal(403, denied.Status);
        Assert.Equal("yes", denied.Headers["X-Stamp"]);

        Assert.Equal("secret", Send(app, "GET", "/admin", query: "admin=yes").Body);
        Assert.Equal("admin.home", app.LoadRoutes().Routes[1].Name);

        PageResult forgetful = Send(app, "GET", "/forgetful");
        Assert.Equal(500, forgetful.Status);
        Assert.Contains("returned nothing", forgetful.Body);
    }

    [Fact]
    public void AuthNeedsTheBearerToken()
    {
        using var site = Site("""
            <KScript.Routes middleware="auth">
              <Get path="/secret"><return value="{ ok: $request.authenticated }" /></Get>
              <Get path="/other" middleware="auth:OTHER_TOKEN"><return value="'x'" /></Get>
            </KScript.Routes>
            """);
        var app = App(site);
        // A browser is sent to sign in (KScript.Auth); a program that wants JSON gets a 401.
        PageResult browser = Send(app, "GET", "/secret");
        Assert.Equal(302, browser.Status);
        Assert.Equal("/login", browser.Headers["Location"]);
        PageResult none = Send(app, "GET", "/secret", headers: new() { ["accept"] = "application/json" });
        Assert.Equal(401, none.Status);
        Assert.Equal("Bearer", none.Headers["WWW-Authenticate"]);
        Assert.Equal(401, Send(app, "GET", "/secret", headers: new() { ["authorization"] = "Bearer wrong" }).Status);
        Assert.Equal("{\"ok\":true}", Send(app, "GET", "/secret", headers: new() { ["authorization"] = "Bearer s3cret" }).Body);

        PageResult unset = Send(app, "GET", "/other", headers: new() { ["authorization"] = "Bearer s3cret" });
        Assert.Equal(500, unset.Status);
        Assert.Contains("OTHER_TOKEN", unset.Body);
    }

    [Fact]
    public void CorsAnswersPreflightsAndAddsHeaders()
    {
        using var site = Site("""
            <KScript.Routes middleware="cors">
              <Post path="/items"><return value="{ ok: true }" /></Post>
            </KScript.Routes>
            """);
        var app = App(site, """{ "http": { "cors": { "origins": ["https://app.test"] } } }""");
        PageResult preflight = Send(app, "OPTIONS", "/items", headers: new() { ["origin"] = "https://app.test", ["access-control-request-method"] = "POST" });
        Assert.Equal(204, preflight.Status);
        Assert.Equal("https://app.test", preflight.Headers["Access-Control-Allow-Origin"]);
        Assert.Contains("POST", preflight.Headers["Access-Control-Allow-Methods"]);

        PageResult post = Send(app, "POST", "/items", headers: new() { ["origin"] = "https://app.test" });
        Assert.Equal("https://app.test", post.Headers["Access-Control-Allow-Origin"]);
        Assert.Equal("Origin", post.Headers["Vary"]);

        PageResult other = Send(app, "POST", "/items", headers: new() { ["origin"] = "https://evil.test" });
        Assert.False(other.Headers.ContainsKey("Access-Control-Allow-Origin"));
    }

    [Fact]
    public void ThrottleLimitsRequestsPerWindow()
    {
        using var site = Site("""
            <KScript.Routes>
              <Get path="/limited" middleware="throttle:2"><return value="'ok'" /></Get>
            </KScript.Routes>
            """);
        var now = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
        var app = App(site);
        app.Clock = () => now;
        Assert.Equal("1", Send(app, "GET", "/limited").Headers["X-RateLimit-Remaining"]);
        Assert.Equal(200, Send(app, "GET", "/limited").Status);
        PageResult blocked = Send(app, "GET", "/limited");
        Assert.Equal(429, blocked.Status);
        Assert.Equal("60", blocked.Headers["Retry-After"]);
        now = now.AddSeconds(61);
        Assert.Equal(200, Send(app, "GET", "/limited").Status);
    }

    [Fact]
    public void KscriptJsonGivesGlobalMiddlewareGroupsAndRouteFiles()
    {
        using var site = Site(null, ("web/main.ks.xml", """
            <KScript.Routes>
              <import src="../middleware.ks.xml" />
              <Get path="/x" middleware="guarded"><return value="'x'" /></Get>
            </KScript.Routes>
            """), ("middleware.ks.xml", Middleware));
        var log = new StringWriter();
        var app = App(site, """
            { "http": {
                "routes": [{ "file": "web/main.ks.xml", "prefix": "/v1" }],
                "middleware": ["log", "stamp"],
                "groups": { "guarded": ["admins_only"] }
            } }
            """, log);
        Assert.Equal(403, Send(app, "GET", "/v1/x").Status);
        PageResult ok = Send(app, "GET", "/v1/x", query: "admin=yes");
        Assert.Equal("x", ok.Body);
        Assert.Equal("yes", ok.Headers["X-Stamp"]);
        Assert.Contains("GET /v1/x -> inline: 403", log.ToString());
        Assert.Equal("log, stamp, admins_only", string.Join(", ", app.LoadRoutes().Routes[0].Middleware));
    }

    [Fact]
    public void CodeFormatRouteFilesWork()
    {
        using var site = Site(null, ("routes.kscript", """
            KScript.Routes {
              import "controllers/people.ks.xml" as people
              Get(path: "/people/{id}", to: people.show, where: "id: int")
              Group(prefix: "/v2") {
                Get(path: "/people", to: people.index)
              }
            }
            """));
        var app = App(site);
        Assert.Equal("{\"id\":2,\"name\":\"Ellie\"}", Send(app, "GET", "/people/2").Body);
        Assert.Equal(200, Send(app, "GET", "/v2/people").Status);
    }

    [Fact]
    public void ListsCanBeArrays()
    {
        // Kyle's case: middleware from an imported .kscript file, named in an array.
        using var site = Site(null,
            ("routes.kscript", """
                KScript.Routes {
                  import "middleware.kscript"
                  import "controllers/people.ks.xml" as people
                  Middleware(use: [stamp, log])
                  Resource(path: "/people", to: people, only: [index, show])
                  Route(methods: [get, post], path: "/both", redirect: "/people")
                  Group(prefix: "/admin", middleware: [admins_only]) {
                    Get(path: "/", redirect: "/")
                  }
                }
                """),
            ("middleware.kscript", """
                import KScript.Http
                fn stamp(request, next) => $next($request)->with_header('X-Stamp', 'yes')
                fn admins_only(request, next) {
                  if $request.query['admin'] != 'yes' {
                    return @abort(403)
                  }
                  return $next($request)
                }
                """));
        var app = App(site);
        PageResult people = Send(app, "GET", "/people");
        Assert.True(people.Status == 200, people.Body);
        Assert.Equal("yes", people.Headers["X-Stamp"]);
        Assert.Equal(405, Send(app, "DELETE", "/people/1").Status);
        Assert.Equal(302, Send(app, "POST", "/both").Status);
        Assert.Equal(403, Send(app, "GET", "/admin").Status);
    }

    [Theory]
    [InlineData("<kscript><echo>hi</echo></kscript>", "starts with <KScript.Routes>")]
    [InlineData("<KScript.Routes><Get path=\"/a\" /></KScript.Routes>", "needs something to answer with")]
    [InlineData("<KScript.Routes><Get path=\"/a\" view=\"x\" redirect=\"/b\" /></KScript.Routes>", "more than one answer")]
    [InlineData("<KScript.Routes><Get path=\"/a\" to=\"nope\" /></KScript.Routes>", "No function named 'nope'")]
    [InlineData("<KScript.Routes><Get path=\"/a\" to=\"people.show\" /></KScript.Routes>", "Nothing is imported as 'people'")]
    [InlineData("<KScript.Routes><import src=\"controllers/people.ks.xml\" as=\"people\" /><Get path=\"/a\" to=\"people.shwo\" /></KScript.Routes>", "has no function 'shwo'")]
    [InlineData("<KScript.Routes><Get path=\"/a\" name=\"x\" redirect=\"/\" /><Get path=\"/b\" name=\"x\" redirect=\"/\" /></KScript.Routes>", "Two routes are named x")]
    [InlineData("<KScript.Routes><Get path=\"/a/{id}/{id}\" redirect=\"/\" /></KScript.Routes>", "used twice")]
    [InlineData("<KScript.Routes><Route methods=\"get, fetch\" path=\"/a\" redirect=\"/\" /></KScript.Routes>", "'FETCH' isn't an HTTP method")]
    [InlineData("<KScript.Routes><Get path=\"/a\" redirect=\"/\" middleware=\"nope\" /></KScript.Routes>", "No function named 'nope'")]
    [InlineData("<KScript.Routes><Get path=\"/a\" redirect=\"/\" middleware=\"throttle:many\" /></KScript.Routes>", null)]
    public void RouteFileMistakesAreExplained(string routes, string message)
    {
        using var site = Site(routes);
        var app = App(site);
        if (message == null)
        {
            // Middleware arguments are checked when the middleware runs.
            PageResult r = Send(app, "GET", "/a");
            Assert.Equal(500, r.Status);
            Assert.Contains("throttle:many", r.Body);
            return;
        }
        var ex = Assert.Throws<KScriptException>(() => app.LoadRoutes());
        Assert.Contains(message, ex.Message);
        Assert.NotNull(ex.File);
    }

    [Fact]
    public void ARouteOutsideRoutesIsAnError()
    {
        var ex = Assert.Throws<KScriptException>(() => new Interpreter(KScriptHost.ForText(new StringWriter())).RunText("<kscript><import package=\"KScript.Http\" /><Get path=\"/\" redirect=\"/\" /></kscript>"));
        Assert.Contains("must be inside", ex.Message);
    }

    [Fact]
    public void ViewsAreNeverServedAsPages()
    {
        using var site = Site(Routes);
        var pages = new PageSite(site.Root);
        pages.HiddenFolders.Add("views");
        Assert.Equal(PageRoute.RouteKind.NotFound, pages.Resolve("/views/people/show").Kind);
    }

    [Fact]
    public void TheRouteListShowsEveryRoute()
    {
        using var site = Site(Routes);
        string text = HttpApp.Describe(App(site).LoadRoutes(), site.Root);
        Assert.Matches(@"GET +/people/\{id\} +people\.show +people\.page", text);
        Assert.Contains("routes.ks.xml:4", text);
        var output = new StringWriter();
        Assert.Equal(0, HttpServer.ListRoutes(site.Root, null, output, new StringWriter()));
        Assert.Contains("METHOD", output.ToString());
    }

    [Fact]
    public void ControllersCanWriteFilesInsideTheSite()
    {
        using var site = Site("""
            <KScript.Routes>
              <import package="KScript.IO" />
              <Post path="/save"><do>@write_text('data/out.txt', 'saved')</do><return value="'ok'" /></Post>
              <Post path="/escape"><do>@write_text('../out.txt', 'no')</do><return value="'ok'" /></Post>
            </KScript.Routes>
            """);
        var app = App(site);
        Assert.Equal("ok", Send(app, "POST", "/save").Body);
        Assert.Equal("saved", File.ReadAllText(Path.Combine(site.Root, "data", "out.txt")));
        Assert.Equal(500, Send(app, "POST", "/escape").Status);

        var readOnly = App(site, """{ "http": { "write_files": false } }""");
        Assert.Equal(500, Send(readOnly, "POST", "/save").Status);
    }

    [Fact]
    public void TheRouteTableIsRebuiltWhenARouteFileChanges()
    {
        using var site = Site("<KScript.Routes><Get path=\"/a\" redirect=\"/\" /></KScript.Routes>");
        var app = App(site);
        Assert.Single(app.Table().Routes);
        site.Write("routes.ks.xml", "<KScript.Routes><Get path=\"/a\" redirect=\"/\" /><Get path=\"/b\" redirect=\"/\" /></KScript.Routes>");
        File.SetLastWriteTimeUtc(Path.Combine(site.Root, "routes.ks.xml"), DateTime.UtcNow.AddMinutes(1));
        Assert.Equal(2, app.Table().Routes.Count);
    }

    [Fact]
    public void TypedParametersMapTheRequestOrAnswer422()
    {
        using var site = Site(null, ("api.ks.xml", """
            <KScript.Routes>
              <type name="NewPerson" fields="name: string, age?: int" />
              <fn name="store" params="json: NewPerson" value="{ name: $json.name, type: @type($json) }" />
              <fn name="show" params="id: int" value="{ id: $id }" />
              <fn name="search" params="query: Search" value="$query.page" />
              <type name="Search" fields="page: int" />
              <Post path="/people" to="store" />
              <Get path="/people/{id}" to="show" />
              <Get path="/search" to="search" />
            </KScript.Routes>
            """));
        var app = App(site);
        Assert.Equal("{\"name\":\"Kyle\",\"type\":\"NewPerson\"}", Send(app, "POST", "/api/people", "{\"name\": \"Kyle\", \"age\": 30}").Body);

        PageResult bad = Send(app, "POST", "/api/people", "{\"age\": \"old\"}");
        Assert.Equal(422, bad.Status);
        Assert.Contains("The request body doesn't fit NewPerson", bad.Body);
        Assert.Contains("\"problems\":[", bad.Body);
        Assert.Contains("$.name", bad.Body);
        Assert.Contains("$.age", bad.Body);

        Assert.Equal("{\"id\":5}", Send(app, "GET", "/api/people/5").Body);
        Assert.Equal(404, Send(app, "GET", "/api/people/abc").Status);
        Assert.Equal("2", Send(app, "GET", "/api/search", query: "page=2").Body);
        Assert.Equal(422, Send(app, "GET", "/api/search").Status);
    }

    [Fact]
    public void ResponsesCanBeChanged()
    {
        using var site = Site("""
            <KScript.Routes>
              <Get path="/a">
                <let name="r" value="@response('hi', type: 'text/plain')->with_status(202)->with_header('X-A', 1)" />
                <return value="$r" />
              </Get>
              <Get path="/b"><return value="@response('x')->status() + @response([1])->type()" /></Get>
              <Get path="/c"><return value="null" /></Get>
              <Get path="/d"><return value="fn() => 1" /></Get>
            </KScript.Routes>
            """);
        var app = App(site);
        PageResult a = Send(app, "GET", "/a");
        Assert.Equal(202, a.Status);
        Assert.Equal("text/plain", a.ContentType);
        Assert.Equal("1", a.Headers["X-A"]);
        Assert.Equal("200application/json; charset=utf-8", Send(app, "GET", "/b").Body);
        Assert.Equal(204, Send(app, "GET", "/c").Status);
        Assert.Contains("returned a function", Send(app, "GET", "/d").Body);
    }

    [Fact]
    public void SampleSiteRoutesLoad()
    {
        string sample = ScriptRunner.RepoFile("samples", "http-app");
        var app = new HttpApp(sample, ProjectConfig.Load(Path.Combine(sample, "kscript.json")));
        RouteTable table = app.LoadRoutes();
        Assert.Contains(table.Routes, r => r.Pattern.Path == "/api/people/{id}" && r.Methods.Contains("DELETE"));
        Assert.Contains("people/show", Directory.GetFiles(Path.Combine(sample, "views", "people")).Select(f => "people/" + Path.GetFileNameWithoutExtension(f)));
        Assert.Contains("<h1>Joan</h1>", app.Handle(new Dictionary<string, object> { ["method"] = "GET", ["path"] = "/people/2" }).Body);
    }
}

public class HttpSettingsTests
{
    [Fact]
    public void ReadsTheHttpSection()
    {
        ProjectConfig c = ProjectConfig.Parse("""
            { "http": {
                "routes": ["web.ks.xml", { "file": "api/v1.ks.xml", "prefix": "/api/v1", "api": true }],
                "middleware": ["log"], "groups": { "admin": ["auth", "log"] }, "views": "templates", "api_prefix": "/rest",
                "write_files": false, "cors": { "origins": ["https://a.test"], "methods": ["GET"], "headers": ["X-A"], "credentials": true },
                "auth": { "token_env": "MY_TOKEN" }, "throttle": { "limit": 10, "seconds": 30 }
            } }
            """, "/tmp/kscript.json");
        Assert.Empty(c.Warnings);
        Assert.Equal(new[] { "web.ks.xml", "api/v1.ks.xml" }, c.Http.Routes.Select(r => r.File));
        Assert.True(c.Http.Routes[1].Api);
        Assert.Equal("/api/v1", c.Http.Routes[1].Prefix);
        Assert.Equal(new[] { "auth", "log" }, c.Http.Groups["admin"]);
        Assert.Equal("templates", c.Http.Views);
        Assert.Equal("/rest", c.Http.ApiPrefix);
        Assert.False(c.Http.WriteFiles);
        Assert.True(c.Http.CorsCredentials);
        Assert.Equal(new[] { "GET" }, c.Http.CorsMethods);
        Assert.Equal(new[] { "X-A" }, c.Http.CorsHeaders);
        Assert.Equal("MY_TOKEN", c.Http.AuthTokenEnv);
        Assert.Equal(10, c.Http.ThrottleLimit);
        Assert.Equal(30, c.Http.ThrottleSeconds);
    }

    [Theory]
    [InlineData("""{ "http": { "nope": 1 } }""", "unknown setting 'http.nope'")]
    [InlineData("""{ "http": { "routes": "x" } }""", "http.routes should be a list")]
    [InlineData("""{ "http": { "routes": [{ "prefix": "/a" }] } }""", "has no \"file\"")]
    [InlineData("""{ "http": { "throttle": { "limit": 0 } } }""", "http.throttle.limit")]
    [InlineData("""{ "http": { "write_files": "yes" } }""", "http.write_files should be true or false")]
    [InlineData("""{ "http": { "routes": [3] } }""", "each entry is a file name")]
    [InlineData("""{ "http": { "routes": [{ "file": "a.ks.xml", "extra": 1 }] } }""", "http.routes[].extra")]
    [InlineData("""{ "http": { "cors": { "nope": 1 } } }""", "http.cors.nope")]
    [InlineData("""{ "http": { "auth": { "nope": 1 } } }""", "http.auth.nope")]
    [InlineData("""{ "http": { "throttle": { "seconds": 0 } } }""", "http.throttle.seconds")]
    [InlineData("""{ "http": { "throttle": { "nope": 1 } } }""", "http.throttle.nope")]
    public void WarnsAboutMistakes(string json, string warning) =>
        Assert.Contains(ProjectConfig.Parse(json, "/tmp/kscript.json").Warnings, w => w.Contains(warning));
}
