using KScript.Pages;
using KScript.Runtime;

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
        var scope = new Scope();
        foreach (var (name, value) in vars) scope.Declare(name, Values.From(value));
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
        var people = Values.ParseJson("[{\"name\":\"Joan\",\"age\":82},{\"name\":\"Ellie\",\"age\":12},{\"name\":\"Simon\",\"age\":42}]");
        Assert.Equal("Simon, Joan", Eval("$people->filter(fn(p) => $p.age ge 18)->sort_by(fn(p) => $p.age)->map(fn(p) => $p.name)->join(', ')", ("people", people)));
        Assert.Equal("Joan", Eval("($people |> max_by(fn(p) => $p.age)).name", ("people", people)));
        Assert.Equal(3d, Eval("$people |> count()", ("people", people)));
        Assert.Equal(136d, Eval("$people->sum(fn(p) => $p.age)", ("people", people)));
        Assert.Equal("Ellie", Eval("$people[1].name", ("people", people)));
        Assert.Equal("Simon", Eval("$people[-1]['name']", ("people", people)));
        Assert.Equal(2d, Eval("{ a: 1, b: { c: 2 } }.b.c"));
        Assert.Equal("HELLO", Eval("'hello' |> fn(s) => $s->upper()"));
        Assert.Equal(8d, Eval("@double(4)", ("double", Eval("fn(x) => $x * 2"))));
    }

    [Fact]
    public void Strings_and_numbers_have_methods()
    {
        Assert.Equal("Hi there", Eval("'  hi there '->trim()->capitalize()"));
        Assert.Equal(3.14, Eval("3.14159->round(digits: 2)"));
        Assert.Equal(42d, Eval("'42'->to_number()"));
        Assert.Equal(0d, Eval("'abc'->to_number(default: 0)"));
        Assert.Equal("a|b", Eval("'a,b'->split(',')->join('|')"));
        Assert.Equal("1,234.50", Eval("1234.5->format(pattern: 'N2')"));
    }

    [Theory]
    [InlineData("$x = 3", "Assignment isn't allowed here")]
    [InlineData("$a && $b", "'and'")]
    [InlineData("name", "$name")]
    [InlineData("1 +", "Expected an expression")]
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
        var scope = new Scope();
        var ctx = new PageContext(Path.GetTempPath());
        scope.Declare("f", PageEvaluator.Evaluate("fn(n) => @f($n + 1)", scope, ctx));
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
        Assert.Equal("<p>O&#39;Brien, it's fine</p>", Render("<let name=\"n\">\"O'Brien\"</let><p>{$n}, it's fine</p>"));
    }

    [Fact]
    public void Attributes_interpolate_and_drop_false_booleans()
    {
        string html = Render("<let name=\"id\" value=\"7\" /><let name=\"done\" value=\"false\" /><a href=\"/people/{$id}\" class=\"{ $id gt 5 ? 'big' : '' }\">x</a><input type=\"checkbox\" checked=\"{$done}\" disabled=\"{true}\">");
        Assert.Equal("<a href=\"/people/7\" class=\"big\">x</a><input type=\"checkbox\" disabled>", html);
    }

    [Fact]
    public void Html_keeps_the_case_of_svg_names()
    {
        Assert.Equal("<svg viewBox=\"0 0 10 10\"><linearGradient id=\"g\" gradientUnits=\"userSpaceOnUse\"></linearGradient><path d=\"M0 0\"></path></svg>",
            Render("<svg viewBox=\"0 0 10 10\"><linearGradient id=\"g\" gradientUnits=\"userSpaceOnUse\"></linearGradient><path d=\"M0 0\"/></svg>"));
        Assert.Equal("<DIV Class=\"a\">x</DIV>", Render("<DIV Class=\"a\">x</div>"));
    }

    [Theory]
    [InlineData("<ul><li>one<li>two</ul>", "<ul><li>one</li><li>two</li></ul>")]
    [InlineData("<p>one<p>two", "<p>one</p><p>two</p>")]
    [InlineData("<p>text<div>block</div>", "<p>text</p><div>block</div>")]
    [InlineData("<table><tr><td>a<td>b<tr><td>c</table>", "<table><tr><td>a</td><td>b</td></tr><tr><td>c</td></tr></table>")]
    [InlineData("<dl><dt>term<dd>def<dt>next</dl>", "<dl><dt>term</dt><dd>def</dd><dt>next</dt></dl>")]
    [InlineData("<select><option>a<option>b</select>", "<select><option>a</option><option>b</option></select>")]
    [InlineData("<ul><for each=\"x\" in=\"['a', 'b']\"><li>{$x}</for></ul>", "<ul><li>a</li><li>b</li></ul>")]
    public void Html_end_tags_that_can_be_left_out_are_closed(string page, string html)
    {
        Assert.Equal(html, Render(page));
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
        Assert.Equal("0:a 1:b ", Render("<for each=\"x\" at=\"i\" in=\"['a', 'b']\">{$i}:{$x} </for>"));
        Assert.Equal("k=1 j=2 ", Render("<for each=\"key, value\" in=\"{ k: 1, j: 2 }\">{$key}={$value} </for>"));
        Assert.Equal("none", Render("<for each=\"x\" in=\"[]\">{$x}</for><else>none</else>"));
    }

    [Fact]
    public void Let_set_and_assignment_statements()
    {
        Assert.Equal("4", Render("<let>x = 3</let><set>x += 1</set>{$x}"));
        Assert.Equal("15", Render("<let name=\"total\" value=\"0\" /><for each=\"n\" in=\"[1, 2, 3, 4, 5]\"><do>total += $n</do></for>{$total}"));
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
            ("index.html", "<let name=\"u\" value=\"{ name: 'Joan' }\" />\n<layout path=\"_layout.html\" title=\"Home\">\n  <user-card user=\"{$u}\"><i>hi</i></user-card>\n</layout>\n"));

        Assert.Equal("<html><title>Home</title><main>\n  <div class=\"card\"><b>Joan</b><i>hi</i></div>\n</main></html>", site.Render("index.html").Body);
    }

    [Fact]
    public void Component_companion_loads_data_and_calls_the_parent_callback()
    {
        using var site = new TempSite(
            ("_components/note.html", """
                <prop name="title" required />
                <article>{@heading($data.text)} <slot /></article>
                """),
            ("_components/note.kscript", """
                fn load(props: map) => { text: $props.title->trim() }
                fn heading(title: string) => $title->upper()
                """),
            ("callbacks.kscript", "fn mark(status: map, data: map) { status.text = $data.text }"),
            ("index.html", """
                <import path="callbacks.kscript" />
                <let name="status" value="{}" />
                <Note title="  first  " onLoad={fn(data) => @mark($status, $data)}><b>child</b></Note>
                <p>{$status.text}</p>
                """));

        string html = site.Render("index.html").Body;
        Assert.Contains("<article>FIRST <b>child</b></article>", html);
        Assert.Contains("<p>first</p>", html);
        Assert.DoesNotContain("<prop", html);
        Assert.DoesNotContain("onLoad", html);
    }

    [Fact]
    public void Required_props_are_checked_before_rendering_a_layout_or_component()
    {
        using var site = new TempSite(
            ("_layout.html", "<prop name=\"page-title\" required /><prop name=\"subtitle\" /><h1>{$page_title}</h1><slot />"),
            ("_components/note.html", "<prop name=\"title\" required /><p>{$title}</p>"),
            ("index.html", "<layout path=\"_layout.html\"><p>body</p></layout>"),
            ("null.html", "<Note title={null} />"),
            ("valid.html", "<layout path=\"_layout.html\" page-title=\"Home\"><Note title=\"a\" /></layout>"),
            ("valid.md", "---\nlayout: _layout.html\npage-title: About\n---\nPresent"));

        Assert.Contains("required 'page-title'", Assert.Throws<PageException>(() => site.Render("index.html")).Message);
        Assert.Contains("required 'title'", Assert.Throws<PageException>(() => site.Render("null.html")).Message);
        Assert.Contains("<h1>Home</h1><p>a</p>", site.Render("valid.html").Body);
        Assert.Contains("<h1>About</h1>", site.Render("valid.md").Body);
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
    public void Respond_sets_the_response_or_redirects()
    {
        using var site = new TempSite(
            ("api.html", "<respond json=\"{ ok: true, n: 2 }\" />"),
            ("missing.html", "<p>before</p><respond status=\"404\">Not here</respond><p>after</p>"),
            ("go.html", "<respond redirect=\"/thanks?n={1 + 1}\" />"),
            ("moved.html", "<respond redirect=\"/new\" status=\"301\" />"),
            ("old.html", "<redirect to=\"/thanks\" />"));

        var api = site.Render("api.html");
        Assert.Equal("application/json; charset=utf-8", api.ContentType);
        Assert.Contains("\"ok\": true", api.Body);

        var missing = site.Render("missing.html");
        Assert.Equal(404, missing.Status);
        Assert.Equal("Not here", missing.Body);

        var go = site.Render("go.html");
        Assert.Equal(302, go.Status);
        Assert.Equal("/thanks?n=2", go.Headers["Location"]);
        Assert.Equal(301, site.Render("moved.html").Status);
        Assert.Contains("<respond redirect=\"/thanks\" />", Assert.ThrowsAny<Exception>(() => site.Render("old.html")).Message);
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

public class PageLiveTests
{
    private const string Counter = "<live id=\"c\"><state name=\"count\" value=\"0\" /><p>{$count}</p><button on-click=\"count += 1\">+</button><button on-click=\"count = 0\">reset</button></live>";

    private static string Attr(string html, string name)
    {
        var m = System.Text.RegularExpressions.Regex.Match(html, name + "=\"([^\"]*)\"");
        return System.Net.WebUtility.HtmlDecode(m.Groups[1].Value);
    }

    private static PageResult Event(TempSite site, string page, string block, int handler, string state, string args = null, Dictionary<string, object> binds = null)
    {
        var json = Values.ToJson(new Dictionary<string, object>
        {
            ["page"] = "/" + Path.GetFileNameWithoutExtension(page),
            ["block"] = block,
            ["handler"] = (double)handler,
            ["state"] = state,
            ["args"] = args,
            ["binds"] = binds ?? new Dictionary<string, object>(),
            ["event"] = new Dictionary<string, object>(),
        });
        return PageServer.HandleLive(new PageSite(site.Root), json, new Dictionary<string, object>());
    }

    [Fact]
    public void Live_blocks_render_state_and_handlers_and_add_the_runtime()
    {
        using var site = new TempSite(("index.html", "<body>" + Counter + "</body>"));
        string html = site.Render("index.html").Body;
        Assert.Contains("data-ks-live=\"c\"", html);
        Assert.Contains("<p>0</p>", html);
        Assert.Contains("<button data-ks-on-click=\"0\">+</button>", html);
        Assert.Contains("<script src=\"/_ks/live.js\" defer></script>\n</body>", html);
        Assert.DoesNotContain("count += 1", html);
    }

    [Fact]
    public void Events_run_the_handler_and_return_only_the_block()
    {
        using var site = new TempSite(("index.html", "<h1>Title</h1>" + Counter));
        string state = Attr(site.Render("index.html").Body, "data-ks-state");

        var once = Event(site, "index.html", "c", 0, state);
        Assert.Equal(200, once.Status);
        Assert.StartsWith("<div data-ks-live=\"c\"", once.Body);
        Assert.Contains("<p>1</p>", once.Body);
        Assert.DoesNotContain("Title", once.Body);

        var twice = Event(site, "index.html", "c", 0, Attr(once.Body, "data-ks-state"));
        Assert.Contains("<p>2</p>", twice.Body);

        var reset = Event(site, "index.html", "c", 1, Attr(twice.Body, "data-ks-state"));
        Assert.Contains("<p>0</p>", reset.Body);
    }

    [Fact]
    public void Live_event_fields_describe_the_page_url()
    {
        using var site = new TempSite(("index.html", Counter));
        string state = Attr(site.Render("index.html").Body, "data-ks-state");
        string json = Values.ToJson(new Dictionary<string, object>
        {
            ["page"] = "/index?page=2", ["block"] = "c", ["handler"] = 0d, ["state"] = state,
        });
        var request = new Dictionary<string, object> { ["scheme"] = "https", ["path"] = "/_ks/live", ["url"] = "/_ks/live" };
        Assert.Equal(200, PageServer.HandleLive(new PageSite(site.Root), json, request).Status);
        Assert.Equal("https", request["scheme"]);
        Assert.Equal("/index", request["path"]);
        Assert.Equal("/index?page=2", request["url"]);
        Assert.Equal("2", ((Dictionary<string, object>)request["query"])["page"]);
    }

    [Fact]
    public void Tampered_state_is_rejected()
    {
        using var site = new TempSite(("index.html", Counter));
        string state = Attr(site.Render("index.html").Body, "data-ks-state");
        string forged = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("{\"count\":999}")) + state.Substring(state.LastIndexOf('.'));
        Assert.Equal(409, Event(site, "index.html", "c", 0, forged).Status);
    }

    [Fact]
    public void Loop_variables_are_passed_to_handlers_and_binds_update_state()
    {
        const string page = "<live id=\"t\"><state name=\"items\" value=\"['a', 'b', 'c']\" /><state name=\"draft\" value=\"''\" />" +
                            "<input bind=\"draft\"><button on-click=\"items = $items->push($draft); draft = ''\">add</button>" +
                            "<for each=\"x\" in=\"$items\"><i on-click=\"items = $items->reject(fn(i) => $i == $x)\">{$x}</i></for></live>";
        using var site = new TempSite(("index.html", page));
        string html = site.Render("index.html").Body;
        Assert.Contains("data-ks-bind=\"draft\" value=\"\" name=\"draft\"", html);

        // Remove "b": the second <i>, whose signed args hold x = 'b'.
        var args = System.Text.RegularExpressions.Regex.Matches(html, "data-ks-args=\"([^\"]*)\"")[1].Groups[1].Value;
        var removed = Event(site, "index.html", "t", 1, Attr(html, "data-ks-state"), System.Net.WebUtility.HtmlDecode(args));
        Assert.Contains("<i data-ks-on-click=\"1\"", removed.Body);
        Assert.DoesNotContain(">b</i>", removed.Body);
        Assert.Contains(">c</i>", removed.Body);

        var added = Event(site, "index.html", "t", 0, Attr(removed.Body, "data-ks-state"), binds: new Dictionary<string, object> { ["draft"] = "z" });
        Assert.Contains(">z</i>", added.Body);
        Assert.Contains("value=\"\"", added.Body);
    }

    private const string Lib = "<kscript>\n<fn name=\"double\" params=\"n\" value=\"$n * 2\" />\n<fn name=\"apply\" params=\"f, x\" value=\"$f($x)\" />\n" +
                               "<fn name=\"boom\" params=\"\"><throw type=\"Oops\" message=\"no good\" /></fn>\n<let name=\"limit\" value=\"3\" const=\"yes\" />\n<let name=\"hidden\" value=\"1\" />\n</kscript>";

    [Fact]
    public void Live_handlers_can_call_imported_functions()
    {
        using var site = new TempSite(("lib/math.ks.xml", Lib),
            ("index.html", "<import path=\"lib/math.ks.xml\" /><live id=\"d\"><state name=\"n\" value=\"@double(1)\" /><p>{$n}</p><button on-click=\"n = @double($n)\">x2</button></live>"));
        string html = site.Render("index.html").Body;
        Assert.Contains("<p>2</p>", html);
        var doubled = Event(site, "index.html", "d", 0, Attr(html, "data-ks-state"));
        Assert.Contains("<p>4</p>", doubled.Body);
    }

    [Fact]
    public void On_tick_handlers_carry_their_interval()
    {
        string html = PageRenderer.RenderString("<live id=\"t\"><state name=\"n\" value=\"0\" /><state name=\"gap\" value=\"'250ms'\" /><span on-tick=\"n += 1\" every=\"{$gap}\"></span></live>").Body;
        Assert.Contains("<span data-ks-on-tick=\"0\" data-ks-every=\"250\"></span>", html);
    }

    [Fact]
    public void Handlers_outside_live_blocks_are_an_error()
    {
        var ex = Assert.Throws<PageException>(() => PageRenderer.RenderString("<button on-click=\"x = 1\">x</button>"));
        Assert.Contains("only works inside a <live> block", ex.Message);
    }

}

/// <summary>Pages run on the same interpreter as scripts: packages and scripts can be imported, and files are sandboxed.</summary>
public class PageRuntimeTests
{
    [Fact]
    public void Pages_can_import_packages()
    {
        using var site = new TempSite(("index.html", "<import package=\"KScript.Math\" as=\"math\" /><p>{ @math.sqrt(16) } { @math.round(2.567, 1) }</p>"));
        Assert.Equal("<p>4 2.6</p>", site.Render("index.html").Body);
    }

    [Fact]
    public void Pages_and_scripts_share_functions()
    {
        using var site = new TempSite(
            ("lib/text.ks.xml", "<kscript>\n  <fn name=\"shout\" params=\"s\" value=\"$s->upper() + '!'\" />\n  <let name=\"greeting\" value=\"'hello'\" const=\"yes\" />\n</kscript>"),
            ("index.html", "<import path=\"lib/text.ks.xml\" /><p>{ @shout($greeting) }</p>"),
            ("named.html", "<import path=\"/lib/text.ks.xml\" as=\"text\" /><p>{ @text.shout('hi') }</p>"),
            ("_components/loud.html", "<import path=\"../lib/text.ks.xml\" names=\"shout\" /><b>{ @shout($word) }</b>"),
            ("component.html", "<loud word=\"hey\" />"));
        Assert.Equal("<p>HELLO!</p>", site.Render("index.html").Body);
        Assert.Equal("<p>HI!</p>", site.Render("named.html").Body);
        Assert.Equal("<b>HEY!</b>", site.Render("component.html").Body);
    }

    [Fact]
    public void Imports_belong_at_the_top_of_a_page()
    {
        using var site = new TempSite(("index.html", "<div><import package=\"KScript.Math\" /></div>"));
        var ex = Assert.Throws<PageException>(() => site.Render("index.html"));
        Assert.Contains("must be at the top level", ex.Message);
        Assert.Equal("index.html:1", ex.Location);
    }

    [Fact]
    public void Unknown_packages_are_explained()
    {
        using var site = new TempSite(("index.html", "<import package=\"KScript.Maths\" />"));
        var ex = Assert.Throws<PageException>(() => site.Render("index.html"));
        Assert.Contains("Did you mean KScript.Math?", ex.Message);
    }

    [Fact]
    public void Pages_cannot_write_files_or_import_scripts_outside_the_site()
    {
        using var site = new TempSite(("write.html", "{ @write_text('x.txt', text: 'hi') }"), ("import.html", "<import path=\"../lib.ks.xml\" />"), ("file.html", "{ @file('.')->join('..', 'x')->read_text() }"));
        Assert.Contains("read-only", Assert.Throws<PageException>(() => site.Render("write.html")).Message);
        Assert.False(File.Exists(Path.Combine(site.Root, "x.txt")));
        Assert.Contains("outside the site folder", Assert.Throws<PageException>(() => site.Render("import.html")).Message);
        Assert.Contains("outside the site folder", Assert.Throws<PageException>(() => site.Render("file.html")).Message);
    }

    [Fact]
    public void Files_are_read_from_the_site_folder()
    {
        using var site = new TempSite(("posts/a.md", "# A"), ("posts/b.md", "# B"), ("posts/index.html", "{ @files('posts', pattern: '*.md')->map(fn(f) => $f.name)->join(', ') } { @exists('posts/a.md') }"));
        Assert.Equal("a.md, b.md true", site.Render("posts/index.html").Body);
    }

    [Fact]
    public void Errors_in_imported_scripts_say_where_they_are()
    {
        using var site = new TempSite(
            ("lib.ks.xml", "<kscript>\n  <fn name=\"bad\" value=\"$nope + 1\" />\n</kscript>"),
            ("index.html", "<import path=\"lib.ks.xml\" />\n<p>{ @bad() }</p>"));
        var ex = Assert.Throws<PageException>(() => site.Render("index.html"));
        Assert.Contains("lib.ks.xml:2", ex.Message);
        Assert.Equal("index.html:2", ex.Location);
    }

    [Fact]
    public void Endless_loops_are_stopped()
    {
        using var site = new TempSite(("index.html", "<let name=\"i\" value=\"0\" /><while test=\"true\"><set name=\"i\" op=\"+=\" value=\"1\" /></while>"));
        var ex = Assert.Throws<PageException>(() => PageRenderer.RenderFile(new PageContext(site.Root) { MaxSteps = 10_000 }, "index.html"));
        Assert.Contains("endless loop", ex.Message);
    }
}
