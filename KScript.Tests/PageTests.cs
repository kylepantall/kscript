using KScript.Pages;

namespace KScript.Tests;

public sealed class TempSite : IDisposable
{
    public TempSite(params (string Path, string Content)[] files)
    {
        Root = Path.Combine(Path.GetTempPath(), "kscript-site-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        foreach (var (path, content) in files) Write(path, content);
    }

    public string Root { get; }

    public void Write(string path, string content)
    {
        string full = Path.Combine(Root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    public PageResult Render(string page, Dictionary<string, object> request = null, Dictionary<string, object> parameters = null) =>
        PageRenderer.RenderFile(new PageContext(Root, request), page, parameters);

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch (IOException) { }
    }
}

public class PageExpressionTests
{
    private static object Eval(string expr, params (string Name, object Value)[] vars)
    {
        var scope = new PageScope();
        foreach (var (name, value) in vars) scope.Declare(name, PageValues.From(value));
        return PageEvaluator.Evaluate(expr, scope, new PageContext(Path.GetTempPath()));
    }

    [Theory]
    [InlineData("1 + 2 * 3", 7d)]
    [InlineData("(1 + 2) * 3", 9d)]
    [InlineData("10 % 4", 2d)]
    [InlineData("-2 + 5", 3d)]
    [InlineData("'a' + 'b'", "ab")]
    [InlineData("'n=' + 3", "n=3")]
    public void Arithmetic(string expr, object expected) => Assert.Equal(expected, Eval(expr));

    [Theory]
    [InlineData("3 lt 4", true)]
    [InlineData("3 ge 4", false)]
    [InlineData("4 > 3", true)]
    [InlineData("'3' == 3", true)]
    [InlineData("'3' === 3", false)]
    [InlineData("3 === 3", true)]
    [InlineData("'3' !== 3", true)]
    [InlineData("'a' eq 'a'", true)]
    [InlineData("null == ''", false)]
    [InlineData("true and not false", true)]
    [InlineData("false or 1 gt 0", true)]
    [InlineData("2 in [1, 2, 3]", true)]
    [InlineData("'x' in 'abc'", false)]
    [InlineData("[1, 2] === [1, 2]", true)]
    public void Comparison_and_logic(string expr, bool expected) => Assert.Equal(expected, Eval(expr));

    [Fact]
    public void Null_handling_and_ternary()
    {
        Assert.Equal("guest", Eval("$missing ?? 'guest'"));
        Assert.Equal("guest", Eval("$user?.name ?? 'guest'", ("user", null)));
        Assert.Equal("Kyle", Eval("$user.name ?? 'guest'", ("user", new Dictionary<string, object> { ["name"] = "Kyle" })));
        Assert.Equal("items", Eval("$n eq 1 ? 'item' : 'items'", ("n", 3d)));
    }

    [Fact]
    public void Lists_maps_lambdas_and_pipes()
    {
        var people = PageValues.From(Newtonsoft.Json.Linq.JToken.Parse("[{\"name\":\"Joan\",\"age\":82},{\"name\":\"Ellie\",\"age\":12},{\"name\":\"Simon\",\"age\":42}]"));
        Assert.Equal("Simon, Joan", Eval("$people->filter(fn($p) => $p.age ge 18)->sort_by(fn($p) => $p.age)->map(fn($p) => $p.name)->join(', ')", ("people", people)));
        Assert.Equal("Joan", Eval("($people |> max_by(fn($p) => $p.age)).name", ("people", people)));
        Assert.Equal(3d, Eval("$people |> count()", ("people", people)));
        Assert.Equal(136d, Eval("$people->sum(fn($p) => $p.age)", ("people", people)));
        Assert.Equal("Ellie", Eval("$people[1].name", ("people", people)));
        Assert.Equal("Simon", Eval("$people[-1]['name']", ("people", people)));
        Assert.Equal(2d, Eval("{ a: 1, b: { c: 2 } }.b.c"));
        Assert.Equal("HELLO", Eval("'hello' |> fn($s) => $s->upper()"));
        Assert.Equal(8d, Eval("@double(4)", ("double", Eval("fn($x) => $x * 2"))));
    }

    [Fact]
    public void Strings_and_numbers_have_methods()
    {
        Assert.Equal("Hi there", Eval("'  hi there '->trim()->capitalize()"));
        Assert.Equal(3.14, Eval("3.14159->round(2)"));
        Assert.Equal(42d, Eval("'42'->to_number()"));
        Assert.Equal(0d, Eval("'abc'->to_number(0)"));
        Assert.Equal("a|b", Eval("'a,b'->split(',')->join('|')"));
        Assert.Equal("1,234.50", Eval("1234.5->format('N2')"));
    }

    [Theory]
    [InlineData("$x = 3", "can't be used here")]
    [InlineData("$a && $b", "'and'")]
    [InlineData("name", "$name")]
    [InlineData("1 +", "Unexpected")]
    public void Helpful_syntax_errors(string expr, string fragment)
    {
        var ex = Assert.Throws<PageException>(() => Eval(expr, ("x", 1d), ("a", true), ("b", true)));
        Assert.Contains(fragment, ex.Message);
    }

    [Fact]
    public void Undefined_variable_suggests_a_close_name()
    {
        var ex = Assert.Throws<PageException>(() => Eval("$totl + 1", ("total", 1d)));
        Assert.Contains("Did you mean $total?", ex.Message);
    }

    [Fact]
    public void Runaway_recursion_is_stopped()
    {
        var scope = new PageScope();
        var ctx = new PageContext(Path.GetTempPath());
        scope.Declare("f", PageEvaluator.Evaluate("fn($n) => @f($n + 1)", scope, ctx));
        var ex = Assert.Throws<PageException>(() => PageEvaluator.Evaluate("@f(0)", scope, ctx));
        Assert.Contains("recursion", ex.Message);
    }
}

public class PageRendererTests
{
    private static string Render(string template) => PageRenderer.RenderString(template).Body;

    [Fact]
    public void Interpolates_text_and_escapes_html()
    {
        Assert.Equal("<p>Hello, &lt;b&gt;Kyle&lt;/b&gt;!</p>", Render("<let name=\"who\" value=\"'<b>Kyle</b>'\" /><p>Hello, {$who}!</p>"));
        Assert.Equal("<p><b>raw</b></p>", Render("<p>{! '<b>raw</b>' }</p>"));
        Assert.Equal("<p>{ literal }</p>", Render("<p>{{ literal }}</p>"));
        Assert.Equal("<p>O&#39;Brien, it's fine</p>", Render("<let name=\"n\" value=\"'O\\'Brien'\" /><p>{$n}, it's fine</p>"));
    }

    [Fact]
    public void Attributes_interpolate_and_drop_false_booleans()
    {
        string html = Render("<let name=\"id\" value=\"7\" /><let name=\"done\" value=\"false\" /><a href=\"/people/{$id}\" class=\"{ $id gt 5 ? 'big' : '' }\">x</a><input type=\"checkbox\" checked=\"{$done}\" disabled=\"{true}\">");
        Assert.Equal("<a href=\"/people/7\" class=\"big\">x</a><input type=\"checkbox\" disabled>", html);
    }

    [Fact]
    public void If_elif_else()
    {
        const string page = "<if test=\"$n ge 90\">A</if><elif test=\"$n ge 75\">B</elif><else>C</else>";
        Assert.Equal("A", Render("<let name=\"n\" value=\"95\" />" + page));
        Assert.Equal("B", Render("<let name=\"n\" value=\"80\" />" + page));
        Assert.Equal("C", Render("<let name=\"n\" value=\"10\" />" + page));
        Assert.Throws<PageException>(() => Render("<else>x</else>"));
    }

    [Fact]
    public void For_loops_over_lists_maps_and_have_an_empty_else()
    {
        Assert.Equal("<li>a</li><li>b</li>", Render("<for each=\"x\" in=\"['a', 'b']\"><li>{$x}</li></for>"));
        Assert.Equal("0:a 1:b ", Render("<for each=\"x\" index=\"i\" in=\"['a', 'b']\">{$i}:{$x} </for>"));
        Assert.Equal("k=1 j=2 ", Render("<for each=\"key, value\" in=\"{ k: 1, j: 2 }\">{$key}={$value} </for>"));
        Assert.Equal("none", Render("<for each=\"x\" in=\"[]\">{$x}</for><else>none</else>"));
    }

    [Fact]
    public void Let_set_and_assignment_statements()
    {
        Assert.Equal("4", Render("<let>x = 3</let><set>x += 1</set>{$x}"));
        Assert.Equal("15", Render("<let name=\"total\" value=\"0\" /><for each=\"n\" in=\"[1, 2, 3, 4, 5]\"><do>$total += $n</do></for>{$total}"));
        Assert.Equal("6", Render("<let name=\"n\" value=\"5\" /><set name=\"n\" op=\"+=\" value=\"1\" />{$n}"));
        Assert.Equal("[1, 2]", Render("<let name=\"xs\">[1, 2]</let>[{$xs}]"));
        var ex = Assert.Throws<PageException>(() => Render("<set name=\"nope\" value=\"1\" />"));
        Assert.Contains("not defined", ex.Message);
    }

    [Fact]
    public void Let_inside_a_block_does_not_leak()
    {
        var ex = Assert.Throws<PageException>(() => Render("<if test=\"true\"><let name=\"inner\" value=\"1\" /></if>{$inner}"));
        Assert.Contains("$inner is not defined", ex.Message);
    }

    [Fact]
    public void Match_picks_the_first_matching_case()
    {
        const string page = "<match value=\"$c\"><case is=\"1\">one</case><case is=\"2, 3\">few</case><case when=\"$it gt 9\">big</case><default>other</default></match>";
        Assert.Equal("one", Render("<let name=\"c\" value=\"1\" />" + page));
        Assert.Equal("few", Render("<let name=\"c\" value=\"3\" />" + page));
        Assert.Equal("big", Render("<let name=\"c\" value=\"10\" />" + page));
        Assert.Equal("other", Render("<let name=\"c\" value=\"5\" />" + page));
    }

    [Fact]
    public void Markdown_element_renders_with_escaped_values()
    {
        string html = Render("<let name=\"name\" value=\"'*Kyle*'\" />\n<markdown>\n  ## Hi {$name}\n  - one\n</markdown>");
        Assert.Contains("<h2 id=", html);
        Assert.Contains("Hi *Kyle*</h2>", html);
        Assert.Contains("<li>one</li>", html);
    }

    [Fact]
    public void Control_elements_on_their_own_lines_leave_tidy_html()
    {
        const string page = "<ul>\n  <for each=\"x\" in=\"['a', 'b']\">\n    <li>{$x}</li>\n  </for>\n</ul>\n";
        Assert.Equal("<ul>\n  <li>a</li>\n  <li>b</li>\n</ul>\n", Render(page));
    }

    [Fact]
    public void Html_parsing_is_forgiving_and_keeps_script_and_style()
    {
        string html = Render("<p>a<br>b</p><script>if (a < b) { x(); }</script><style>p { color: red }</style>");
        Assert.Equal("<p>a<br>b</p><script>if (a < b) { x(); }</script><style>p { color: red }</style>", html);
    }

    [Fact]
    public void Errors_carry_the_file_and_line()
    {
        var ex = Assert.Throws<PageException>(() => PageRenderer.RenderString("<p>ok</p>\n<p>{$nope}</p>", fileName: "x.html"));
        Assert.Equal("x.html:2", ex.Location);
    }

    [Fact]
    public void Endless_while_is_stopped()
    {
        var ex = Assert.Throws<PageException>(() => Render("<while test=\"true\">x</while>"));
        Assert.Contains("100,000", ex.Message);
    }
}

public class PageSiteTests
{
    [Fact]
    public void Layouts_and_components_wrap_content()
    {
        using var site = new TempSite(
            ("_layout.html", "<html><title>{$title}</title><main>\n  <slot />\n</main></html>"),
            ("_components/user-card.html", "<div class=\"card\"><b>{$user.name}</b><slot /></div>"),
            ("index.html", "<let name=\"u\" value=\"{ name: 'Joan' }\" />\n<layout src=\"_layout.html\" title=\"Home\">\n  <user-card user=\"{$u}\"><i>hi</i></user-card>\n</layout>\n"));

        Assert.Equal("<html><title>Home</title><main>\n  <div class=\"card\"><b>Joan</b><i>hi</i></div>\n</main></html>", site.Render("index.html").Body);
    }

    [Fact]
    public void Components_do_not_see_page_variables()
    {
        using var site = new TempSite(
            ("_components/x-a.html", "{$secret}"),
            ("index.html", "<let name=\"secret\" value=\"1\" /><x-a />"));
        var ex = Assert.Throws<PageException>(() => site.Render("index.html"));
        Assert.Contains("$secret is not defined", ex.Message);
    }

    [Fact]
    public void Unknown_custom_elements_are_left_as_html()
    {
        using var site = new TempSite(("index.html", "<my-widget data-x=\"1\">hi</my-widget>"));
        Assert.Equal("<my-widget data-x=\"1\">hi</my-widget>", site.Render("index.html").Body);
    }

    [Fact]
    public void Markdown_pages_use_front_matter_and_layouts()
    {
        using var site = new TempSite(
            ("_layout.html", "<title>{$title}</title>\n<slot />"),
            ("data/people.json", "[{\"name\":\"Joan\"},{\"name\":\"Ellie\"}]"),
            ("about.md", "---\ntitle: About\nlayout: _layout.html\n---\n<let name=\"people\" value=\"@read_json('data/people.json')\" />\n\n# Team of {$people->count()}\n\n<for each=\"p\" in=\"$people\">\n- {$p.name}\n</for>\n"));

        string html = site.Render("about.md").Body;
        Assert.StartsWith("<title>About</title>", html);
        Assert.Contains("Team of 2</h1>", html);
        Assert.Contains("<li>Joan</li>", html);
        Assert.Contains("<li>Ellie</li>", html);
    }

    [Fact]
    public void Read_markdown_returns_html_and_meta()
    {
        using var site = new TempSite(
            ("posts/hello.md", "---\ntitle: Hello\ntags: [a, b]\n---\nSome *text*."),
            ("index.html", "<let name=\"post\" value=\"@read_markdown('posts/hello.md')\" /><h1>{$post.title}</h1>{! $post.html }{$post.meta.tags->join('+')}"));
        Assert.Equal("<h1>Hello</h1><p>Some <em>text</em>.</p>\na+b", site.Render("index.html").Body);
    }

    [Fact]
    public void Respond_and_redirect_set_the_response()
    {
        using var site = new TempSite(
            ("api.html", "<respond json=\"{ ok: true, n: 2 }\" />"),
            ("missing.html", "<p>before</p><respond status=\"404\">Not here</respond><p>after</p>"),
            ("go.html", "<redirect to=\"/thanks?n={1 + 1}\" />"));

        var api = site.Render("api.html");
        Assert.Equal("application/json; charset=utf-8", api.ContentType);
        Assert.Contains("\"ok\": true", api.Body);

        var missing = site.Render("missing.html");
        Assert.Equal(404, missing.Status);
        Assert.Equal("Not here", missing.Body);

        var go = site.Render("go.html");
        Assert.Equal(302, go.Status);
        Assert.Equal("/thanks?n=2", go.Headers["Location"]);
    }

    [Fact]
    public void Request_values_are_available()
    {
        using var site = new TempSite(("p.html", "{$request.query.q} {$request.params.id} {$request.method}"));
        var request = new Dictionary<string, object> { ["query"] = new Dictionary<string, object> { ["q"] = "cats" }, ["method"] = "GET" };
        Assert.Equal("cats 42 GET", site.Render("p.html", request, new Dictionary<string, object> { ["id"] = "42" }).Body);
    }

    [Fact]
    public void Files_outside_the_site_cannot_be_read()
    {
        using var site = new TempSite(("index.html", "{ @read_text('../secret.txt') }"));
        var ex = Assert.Throws<PageException>(() => site.Render("index.html"));
        Assert.Contains("outside the site folder", ex.Message);
    }

    [Fact]
    public void Routing_maps_urls_to_files()
    {
        using var site = new TempSite(
            ("index.html", ""),
            ("people.html", ""),
            ("about.md", ""),
            ("people/[id].html", ""),
            ("blog/index.md", ""),
            ("style.css", ""),
            ("_layout.html", ""),
            ("script.xml", ""));
        var routes = new PageSite(site.Root);

        Assert.Equal("index.html", routes.Resolve("/").File);
        Assert.Equal("people.html", routes.Resolve("/people").File);
        Assert.Equal("about.md", routes.Resolve("/about").File);
        Assert.Equal("blog/index.md", routes.Resolve("/blog").File);

        var person = routes.Resolve("/people/42");
        Assert.Equal("people/[id].html", person.File);
        Assert.Equal("42", person.Params["id"]);

        Assert.Equal(PageRoute.RouteKind.Static, routes.Resolve("/style.css").Kind);
        Assert.Equal(PageRoute.RouteKind.NotFound, routes.Resolve("/_layout.html").Kind);
        Assert.Equal(PageRoute.RouteKind.NotFound, routes.Resolve("/_layout").Kind);
        Assert.Equal(PageRoute.RouteKind.NotFound, routes.Resolve("/script.xml").Kind);
        Assert.Equal(PageRoute.RouteKind.NotFound, routes.Resolve("/../etc/passwd").Kind);
        Assert.Equal(PageRoute.RouteKind.NotFound, routes.Resolve("/nope").Kind);
    }

    [Fact]
    public async Task Server_answers_http_requests()
    {
        using var site = new TempSite(("index.html", "<p>{$request.query.name ?? 'world'}</p>"));
        int port = FreePort();
        var server = Task.Run(() => PageServer.RunAsync(site.Root, port, "localhost", null));

        using var http = new HttpClient();
        string body = null;
        for (int i = 0; i < 50 && body == null; i++)
        {
            try { body = await http.GetStringAsync($"http://localhost:{port}/?name=Kyle"); }
            catch (HttpRequestException) { await Task.Delay(100); }
        }
        Assert.Equal("<p>Kyle</p>", body);

        var missing = await http.GetAsync($"http://localhost:{port}/missing");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, missing.StatusCode);
    }

    private static int FreePort()
    {
        var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        int port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}
