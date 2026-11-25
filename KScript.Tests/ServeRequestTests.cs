using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;

using KScript.Http;
using KScript.Pages;
using KScript.Projects;
using KScript.Runtime;
using Microsoft.AspNetCore.Http;

namespace KScript.Tests;

/// <summary>
/// What <c>kscript serve</c> answers when a request or a response is out of the ordinary: a status or header that can't
/// be sent, a body that can't be read, a name sent twice, an error in production, a live block on a page with no GET.
/// </summary>
public sealed class ServeRequestTests
{
    /// <summary>Serves the site on a free port and gives the test a client for it (which follows no redirects).</summary>
    private static async Task Serve(TempSite site, Func<HttpClient, Task> test, bool production = false, StringWriter log = null, ProjectConfig project = null)
    {
        using var stop = new CancellationTokenSource();
        var listening = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = HttpServer.RunAsync(site.Root, project, TestServer.FreePort(), "127.0.0.1", log, stop.Token,
            started: url => listening.SetResult(url), production: production);
        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var http = new HttpClient(handler) { BaseAddress = new Uri(await listening.Task.WaitAsync(TimeSpan.FromSeconds(10))) };
            await test(http);
        }
        finally
        {
            stop.Cancel();
            await server;
        }
    }

    private static string Attribute(string html, string name) => WebUtility.HtmlDecode(Regex.Match(html, name + "=\"([^\"]+)\"").Groups[1].Value);

    private const string Counter = """<live id="counter"><state name="n" value="0"/><b>{$n}</b><button on-click="n += 1">Add</button></live>""";

    #region Live blocks and the page's GET

    [Fact]
    public async Task A_live_block_on_the_not_found_page_calls_back_to_the_missing_address()
    {
        using var site = new TempSite(("index.html", "<h1>Home</h1>"), ("_404.html", "<h1>Not here</h1>" + Counter));
        await Serve(site, async http =>
        {
            using var page = await http.GetAsync("/nowhere");
            Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);
            string html = await page.Content.ReadAsStringAsync();
            Assert.Contains("Not here", html);
            string proof = Attribute(html, "data-ks-route");
            Assert.NotEmpty(proof);

            var click = new { block = "counter", handler = 0, state = Attribute(html, "data-ks-state"), args = Attribute(html, "data-ks-args-0") };
            http.DefaultRequestHeaders.Add("X-KScript-Live", proof);
            using var clicked = await http.PostAsJsonAsync("/nowhere", click);
            Assert.Equal(HttpStatusCode.OK, clicked.StatusCode);
            Assert.Contains("<b>1</b>", await clicked.Content.ReadAsStringAsync());
            Assert.True(clicked.Headers.Contains("X-KScript-Fragment"));

            // The proof is for that address only, and a file that is not a page still has nothing to call back to.
            using var elsewhere = await http.PostAsJsonAsync("/somewhere-else", click);
            Assert.Equal(HttpStatusCode.Conflict, elsewhere.StatusCode);
        });
    }

    [Fact]
    public void A_view_with_a_live_block_answered_for_a_post_needs_a_get_to_call_back_to()
    {
        using var site = new TempSite(
            ("views/counter.html", Counter),
            ("routes.kscript", """
                import KScript.Http
                Routes {
                  Post(path: /only-post) { return @view('counter') }
                  Get(path: /both) { return @view('counter') }
                  Post(path: /both) { return @view('counter') }
                }
                """));
        var app = new HttpApp(site.Root);
        PageResult Send(string method, string path) =>
            app.Handle(new Dictionary<string, object> { ["method"] = method, ["path"] = path, ["url"] = path });

        // Nothing could render /only-post again for a click, so the page says so instead of reloading for ever.
        PageResult alone = Send("POST", "/only-post");
        Assert.Equal(500, alone.Status);
        Assert.Contains("GET /only-post, which has no route", alone.Body);
        Assert.Contains("add a GET route for /only-post", alone.Body);

        // With a GET at the same address the POST's page works like the GET's: its blocks call the GET.
        PageResult posted = Send("POST", "/both");
        Assert.Equal(200, posted.Status);
        Assert.NotEmpty(Attribute(posted.Body, "data-ks-route"));
        Assert.NotEmpty(Attribute(Send("GET", "/both").Body, "data-ks-route"));
        Assert.Equal(200, Send("HEAD", "/both").Status);
    }

    #endregion

    #region What a live block takes from the browser

    [Fact]
    public async Task A_live_block_takes_only_what_a_form_control_sends_for_untyped_state()
    {
        using var site = new TempSite(("index.html", """
            <live id="search"><state name="q" value="''" /><input bind="q"><p>q={$q}</p></live>
            """));
        await Serve(site, async http =>
        {
            string html = await http.GetStringAsync("/");
            http.DefaultRequestHeaders.Add("X-KScript-Live", Attribute(html, "data-ks-route"));
            string state = Attribute(html, "data-ks-state");

            using var typed = await http.PostAsJsonAsync("/", new { block = "search", handler = -1, state, binds = new { q = "tea" } });
            Assert.Contains("q=tea", await typed.Content.ReadAsStringAsync());

            using var map = await http.PostAsJsonAsync("/", new { block = "search", handler = -1, state, binds = new { q = new { admin = true } } });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, map.StatusCode);
            Assert.Contains("isn't one a form control sends", await map.Content.ReadAsStringAsync());

            using var nested = await http.PostAsJsonAsync("/", new { block = "search", handler = -1, state, binds = new { q = new object[] { "a", new { b = 1 } } } });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, nested.StatusCode);
        });
    }

    #endregion

    #region Statuses and headers that can't be sent

    [Fact]
    public async Task A_status_or_header_that_cannot_be_sent_is_explained()
    {
        using var site = new TempSite(
            ("status.html", """<respond status="$request.query.s->to_number()">s</respond>"""),
            ("header.html", """<respond status="200" type="text/plain" header-x-echo="{$request.query.v ?? ''}">ok</respond>"""),
            ("go.html", """<respond redirect="{$request.query.next ?? '/'}" />"""));
        var log = new StringWriter();
        await Serve(site, async http =>
        {
            using var teapot = await http.GetAsync("/status?s=418");
            Assert.Equal(418, (int)teapot.StatusCode);

            foreach (string status in new[] { "0", "99", "1000", "99999" })
            {
                using var wrong = await http.GetAsync("/status?s=" + status);
                Assert.Equal(HttpStatusCode.InternalServerError, wrong.StatusCode);
                Assert.Contains($"{status} isn&#39;t an HTTP status", await wrong.Content.ReadAsStringAsync());
            }

            using var plain = await http.GetAsync("/header?v=fine");
            Assert.Equal("fine", plain.Headers.GetValues("X-Echo").Single());

            // A line break would start a header of the visitor's choosing; other characters outside ASCII can't be sent.
            using var split = await http.GetAsync("/header?v=a%0d%0aSet-Cookie:%20x=1");
            Assert.Equal(HttpStatusCode.InternalServerError, split.StatusCode);
            Assert.False(split.Headers.Contains("Set-Cookie"));
            Assert.Contains("line break or control character", await split.Content.ReadAsStringAsync());
            using var accent = await http.GetAsync("/header?v=caf%C3%A9");
            Assert.Equal(HttpStatusCode.InternalServerError, accent.StatusCode);
            Assert.Contains("outside ASCII", await accent.Content.ReadAsStringAsync());

            // A redirect to an address with such characters is sent encoded, as a browser would write it.
            using var moved = await http.GetAsync("/go?next=/caf%C3%A9%20menu");
            Assert.Equal(HttpStatusCode.Redirect, moved.StatusCode);
            Assert.Equal("/caf%C3%A9%20menu", moved.Headers.GetValues("Location").Single());
            using var broken = await http.GetAsync("/go?next=/a%0d%0aX-Injected:%201");
            Assert.Equal(HttpStatusCode.InternalServerError, broken.StatusCode);
            Assert.False(broken.Headers.Contains("X-Injected"));
        }, log: log);
        Assert.Contains("99999 isn't an HTTP status", log.ToString());

        // In production the visitor is told nothing about the page's mistake; the log still has it.
        var production = new StringWriter();
        await Serve(site, async http =>
        {
            using var wrong = await http.GetAsync("/status?s=99999");
            Assert.Equal(HttpStatusCode.InternalServerError, wrong.StatusCode);
            string body = await wrong.Content.ReadAsStringAsync();
            Assert.Contains(PageServer.HiddenErrorMessage, body);
            Assert.DoesNotContain("99999", body);
        }, production: true, log: production);
        Assert.Contains("99999 isn't an HTTP status", production.ToString());
    }

    #endregion

    #region Bodies that can't be read

    [Fact]
    public async Task A_body_that_cannot_be_read_is_the_clients_mistake()
    {
        using var site = new TempSite(
            ("form.html", "<p>name={$request.form.name ?? 'none'}</p>"),
            ("api.kscript", "import KScript.Http\nRoutes {\n  Post(path: /items) { return { ok: true } }\n}\n"),
            ("routes.kscript", "import KScript.Http\nRoutes {\n  Post(path: /save) { return 'saved' }\n}\n"));
        await Serve(site, async http =>
        {
            // More fields than a form may have: 400 with a short page, not a 500 with the server's own error.
            string many = string.Join("&", Enumerable.Range(0, 3000).Select(i => $"f{i}=1"));
            using var page = await http.PostAsync("/form", new StringContent(many, Encoding.UTF8, "application/x-www-form-urlencoded"));
            string pageBody = await page.Content.ReadAsStringAsync();
            Assert.True((int)page.StatusCode is 400 or 413, pageBody);
            Assert.DoesNotContain("InvalidDataException", pageBody);
            Assert.DoesNotContain("KScript error", pageBody);

            using var route = await http.PostAsync("/save", new StringContent(many, Encoding.UTF8, "application/x-www-form-urlencoded"));
            Assert.True((int)route.StatusCode is 400 or 413);

            using var api = await http.PostAsync("/api/items", new StringContent(many, Encoding.UTF8, "application/x-www-form-urlencoded"));
            Assert.True((int)api.StatusCode is 400 or 413);
            Assert.Equal("application/json", api.Content.Headers.ContentType?.MediaType);
            using var json = System.Text.Json.JsonDocument.Parse(await api.Content.ReadAsStringAsync());
            Assert.Equal((int)api.StatusCode, json.RootElement.GetProperty("status").GetInt32());

            using var fine = await http.PostAsync("/form", new StringContent("name=Ada", Encoding.UTF8, "application/x-www-form-urlencoded"));
            Assert.Contains("name=Ada", await fine.Content.ReadAsStringAsync());
        });
    }

    [Theory]
    [InlineData(typeof(InvalidDataException), "Form value count limit 1024 exceeded.", 413)]
    [InlineData(typeof(InvalidDataException), "Multipart body is malformed.", 400)]
    [InlineData(typeof(IOException), "Unexpected end of request content.", 400)]
    [InlineData(typeof(InvalidOperationException), "A bug of our own.", null)]
    public void Only_errors_from_reading_the_body_are_the_clients(Type error, string message, int? status) =>
        Assert.Equal(status, PageServer.BodyProblem((Exception)Activator.CreateInstance(error, message)));

    #endregion

    #region Names sent more than once, and the body of a form

    [Fact]
    public async Task A_name_sent_twice_is_a_list_and_a_form_keeps_its_body()
    {
        using var site = new TempSite(
            ("show.html", """<respond json="{ query: $request.query, form: $request.form, body: $request.body }" />"""),
            ("controllers/c.kscript", """
                import KScript.Http

                type Search {
                  q: string
                  tags?: string[]
                }

                fn search(query: Search) => $query
                fn save(form: Search) => $form
                """),
            ("routes.kscript", """
                import KScript.Http
                import "controllers/c.kscript" as c

                Routes {
                  Get(path: /api/search, to: @c.search)
                  Post(path: /api/save, to: @c.save)
                }
                """));
        await Serve(site, async http =>
        {
            using var shown = System.Text.Json.JsonDocument.Parse(await http.GetStringAsync("/show?tag=a&tag=b&one=1"));
            var query = shown.RootElement.GetProperty("query");
            Assert.Equal(new[] { "a", "b" }, query.GetProperty("tag").EnumerateArray().Select(t => t.GetString()));
            Assert.Equal("1", query.GetProperty("one").GetString());

            using var posted = await http.PostAsync("/show", new StringContent("tag=a&tag=b&name=Ada+L", Encoding.UTF8, "application/x-www-form-urlencoded"));
            using var form = System.Text.Json.JsonDocument.Parse(await posted.Content.ReadAsStringAsync());
            Assert.Equal(new[] { "a", "b" }, form.RootElement.GetProperty("form").GetProperty("tag").EnumerateArray().Select(t => t.GetString()));
            Assert.Equal("Ada L", form.RootElement.GetProperty("form").GetProperty("name").GetString());
            Assert.Equal("tag=a&tag=b&name=Ada+L", form.RootElement.GetProperty("body").GetString());

            // A list field takes the name sent once or many times.
            Assert.Equal("{\"q\":\"x\",\"tags\":[\"a\",\"b\"]}", await http.GetStringAsync("/api/search?q=x&tags=a&tags=b"));
            Assert.Equal("{\"q\":\"x\",\"tags\":[\"a\"]}", await http.GetStringAsync("/api/search?q=x&tags=a"));
            using var saved = await http.PostAsync("/api/save", new StringContent("q=x&tags=a&tags=b", Encoding.UTF8, "application/x-www-form-urlencoded"));
            Assert.Equal("{\"q\":\"x\",\"tags\":[\"a\",\"b\"]}", await saved.Content.ReadAsStringAsync());

            // Text can't be a list: the second q is a mistake, said as one.
            using var twice = await http.GetAsync("/api/search?q=x&q=y");
            Assert.Equal(HttpStatusCode.UnprocessableEntity, twice.StatusCode);
        });
    }

    #endregion

    #region Numbers a request sends

    [Fact]
    public void A_number_sent_for_an_int_has_to_be_a_whole_number()
    {
        using var site = new TempSite(
            ("controllers/c.kscript", """
                import KScript.Http

                type Item {
                  name: string
                  qty: int
                  price?: number
                }

                type Paging {
                  page?: int
                }

                fn item(json: Item) => $json
                fn list(query: Paging) => $query
                fn show(n: int) => { n: $n }
                """),
            ("api.kscript", """
                import KScript.Http
                import "controllers/c.kscript" as c

                Routes {
                  Post(path: /items, to: @c.item)
                  Get(path: /items, to: @c.list)
                  Get(path: /items/{n}, to: @c.show)
                }
                """));
        var app = new HttpApp(site.Root);
        PageResult Get(string path, string query = null) => app.Handle(new Dictionary<string, object>
        {
            ["method"] = "GET", ["path"] = path,
            ["query"] = query == null ? new Dictionary<string, object>() : new Dictionary<string, object> { ["page"] = query },
        });
        PageResult Post(string body) => app.Handle(new Dictionary<string, object>
        {
            ["method"] = "POST", ["path"] = "/api/items", ["body"] = body,
            ["headers"] = new Dictionary<string, object> { ["content-type"] = "application/json" },
        });

        Assert.Equal("{\"name\":\"tea\",\"qty\":2,\"price\":1.5}", Post("{\"name\":\"tea\",\"qty\":2,\"price\":1.5}").Body);
        Assert.Equal("{\"name\":\"tea\",\"qty\":2,\"price\":null}", Post("{\"name\":\"tea\",\"qty\":\"2.0\"}").Body);

        // 1.9 was once cut down to 1 without a word.
        PageResult part = Post("{\"name\":\"tea\",\"qty\":1.9}");
        Assert.Equal(422, part.Status);
        Assert.Contains("needs a whole number, but got a number (1.9)", part.Body);
        PageResult huge = Post("{\"name\":\"tea\",\"qty\":1,\"price\":\"1e999\"}");
        Assert.Equal(422, huge.Status);
        Assert.Contains("too large", huge.Body);

        Assert.Equal("{\"page\":3}", Get("/api/items", "3").Body);
        Assert.Equal(422, Get("/api/items", "3.5").Status);
        Assert.Equal(422, Get("/api/items", "1e999").Status);

        Assert.Equal("{\"n\":7}", Get("/api/items/7").Body);
        Assert.Equal(404, Get("/api/items/7.5").Status);
        Assert.Equal(404, Get("/api/items/1e999").Status);
    }

    #endregion

    #region An answer's own status

    [Fact]
    public void Wrapping_an_answer_keeps_its_status_unless_one_is_given()
    {
        using var site = new TempSite(("routes.kscript", """
            import KScript.Http
            Routes {
              Get(path: /wrapped) { return @response(@response('gone', status: 404)) }
              Get(path: /moved) { return @response(@redirect('/new')) }
              Get(path: /restated) { return @response(@response('gone', status: 404), status: 410) }
              Get(path: /plain) { return @response('hi') }
            }
            """));
        var app = new HttpApp(site.Root);
        PageResult Get(string path) => app.Handle(new Dictionary<string, object> { ["method"] = "GET", ["path"] = path });
        Assert.Equal(404, Get("/wrapped").Status);
        Assert.Equal((302, "/new"), (Get("/moved").Status, Get("/moved").Headers["Location"]));
        Assert.Equal(410, Get("/restated").Status);
        Assert.Equal(200, Get("/plain").Status);
    }

    #endregion

    #region Errors in production

    [Fact]
    public async Task In_production_an_error_is_a_plain_500_and_the_log_has_the_details()
    {
        using var site = new TempSite(
            ("broken.html", "<p>{ $nope.x }</p>"),
            ("routes.kscript", """
                import KScript.Http
                Routes {
                  Get(path: /boom) {
                    x = [1, 2]
                    return $x[5].y
                  }
                }
                """),
            ("api.kscript", """
                import KScript.Http
                Routes {
                  Get(path: /boom) {
                    x = [1, 2]
                    return $x[5].y
                  }
                  Get(path: /missing) {
                    throw NotFound "No such thing."
                  }
                }
                """));

        var log = new StringWriter();
        await Serve(site, async http =>
        {
            foreach (string path in new[] { "/broken", "/boom" })
            {
                using var answer = await http.GetAsync(path);
                string body = await answer.Content.ReadAsStringAsync();
                Assert.Equal(HttpStatusCode.InternalServerError, answer.StatusCode);
                Assert.Contains(PageServer.HiddenErrorMessage, body);
                Assert.DoesNotContain("nope", body);
                Assert.DoesNotContain(".html", body);
                Assert.DoesNotContain("routes.kscript", body);
            }
            using var api = await http.GetAsync("/api/boom");
            Assert.Equal(HttpStatusCode.InternalServerError, api.StatusCode);
            using var json = System.Text.Json.JsonDocument.Parse(await api.Content.ReadAsStringAsync());
            Assert.Equal(PageServer.HiddenErrorMessage, json.RootElement.GetProperty("error").GetString());

            // An error the site means for the visitor (a status of its own) still says what it says.
            using var missing = await http.GetAsync("/api/missing");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.Contains("No such thing.", await missing.Content.ReadAsStringAsync());
        }, production: true, log: log);
        Assert.Contains("nope", log.ToString());
        Assert.Contains("routes.kscript", log.ToString());

        // While working on the site the error page still shows everything.
        await Serve(site, async http =>
        {
            using var answer = await http.GetAsync("/broken");
            Assert.Contains("nope", await answer.Content.ReadAsStringAsync());
            using var route = await http.GetAsync("/boom");
            Assert.Contains("routes.kscript", await route.Content.ReadAsStringAsync());
        });
    }

    #endregion

    #region Locale prefixes and throttle counts

    [Fact]
    public void A_locale_prefix_is_read_once()
    {
        using var site = new TempSite(("about.html", "<h1>About</h1>"), ("locales/fr.json", "{}"), ("locales/en.json", "{}"));
        var pages = new PageSite(site.Root);
        PageRoute french = pages.Resolve("/fr/about");
        Assert.Equal(PageRoute.RouteKind.Page, french.Kind);
        Assert.Equal("fr", french.Locale);
        Assert.Equal(PageRoute.RouteKind.NotFound, pages.Resolve("/fr/fr/about").Kind);
        Assert.Equal(PageRoute.RouteKind.NotFound, pages.Resolve("/fr/en/about").Kind);
    }

    [Fact]
    public void Throttle_counts_are_dropped_once_their_window_has_passed()
    {
        using var site = new TempSite(("routes.kscript", "import KScript.Http\nRoutes {\n Get(path: /hi) { return 'hi' }\n}"));
        DateTime now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var app = new HttpApp(site.Root) { Clock = () => now };
        for (int i = 0; i < 50; i++) Assert.True(app.Throttle("visitor " + i, 2, 30, out _, out _));
        Assert.Equal(50, app.ThrottleEntries);

        // Inside the window a count still limits its visitor.
        now = now.AddSeconds(20);
        Assert.True(app.Throttle("visitor 0", 2, 30, out _, out _));
        Assert.False(app.Throttle("visitor 0", 2, 30, out _, out double wait));
        Assert.Equal(10, wait);

        now = now.AddSeconds(120);
        Assert.True(app.Throttle("someone new", 2, 30, out int left, out _));
        Assert.Equal(1, left);
        Assert.Equal(1, app.ThrottleEntries);
    }

    #endregion
}
