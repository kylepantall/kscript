using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;

using KScript.Data;
using KScript.Http;
using KScript.Pages;
using KScript.Projects;

namespace KScript.Tests;

/// <summary>@inject(): an input filled with the record the route holds.</summary>
public sealed class InjectionTests : DataFixture
{
    private HttpApp Blog(string card = null)
    {
        Database.CloseAll();
        if (Server != null) DatabaseServer.Reset(Server);
        FileOf("kscript.json", "{ \"name\": \"blog\" }");
        FileOf("models/post.kscript", "import KScript.Data\n\nmodel Post {\n  title: string\n}\n");
        FileOf("models/tag.kscript", "import KScript.Data\n\nmodel Tag {\n  name: string\n}\n");
        FileOf("_components/post-card.kscript", card ?? """
            import KScript.Web
            import "../models/post.kscript"

            type PostCard extends Component {
              construct({ post: Post = @inject() })

              fn render() => ```html
                <h2>{$post.title}</h2>
                ```
            }
            """);
        FileOf("_components/article-card.kscript", """
            import KScript.Web
            import "../models/post.kscript"

            type ArticleCard extends Component {
              construct({ post: Post = @inject(from: article) })

              fn render() => ```html
                <h3>{$post.title}</h3>
                ```
            }
            """);
        FileOf("_components/like-button.kscript", """
            import KScript.Web
            import "../models/post.kscript"

            type LikeButton extends Component {
              construct({ post: Post = @inject() })

              likes: int

              fn render() => ```html
                <p>{$post.title}: {$likes}</p>
                <button @on-click(likes += 1)>Like</button>
                ```
            }
            """);
        FileOf("views/liked.html", "<like-button live-id=\"c\" />");
        FileOf("views/show.html", "<post-card />");
        FileOf("views/twice.html", "<post-card /><post-card />");
        FileOf("views/article.html", "<article-card />");
        FileOf("views/passed.html", "<post-card post=\"{$other}\" />");
        FileOf("controllers/posts.kscript", """
            import KScript.Http
            import KScript.Data
            import "../models/post.kscript"
            import "../models/tag.kscript"

            type Summary {
              construct({ post: Post = @inject() }) {
                length = $post.title->count()
              }

              length: int
            }

            fn show(post: Post) => @view('show')
            fn article(article: Post) => @view('show')
            fn passed(post: Post) => @view('passed', other: Post->find(2))
            fn by_id(id: int) => @view('show')
            fn two(first: Post, second: Post) => @view('show')
            fn summary() => { length: Summary().length }
            fn alone(article: Post) => { title: @inject(Post).title, same: @inject(Post, from: article).title }
            fn wrong(tag: Tag) => { title: @inject(Post, from: tag).title }
            fn missing(article: Post) => { title: @inject(Post, from: missing).title }
            fn none() => { title: @inject(Post).title }
            """);
        FileOf("routes/web.kscript", """
            import KScript.Http
            import "../controllers/posts.kscript" as posts

            Routes {
              Get(path: "/posts/{post}", to: @posts.show)
              Get(path: "/viewed/{post}", view: 'show')
              Get(path: "/liked/{post}", view: 'liked')
              Get(path: "/twice/{post}", view: 'twice')
              Get(path: "/articles/{article}", to: @posts.article)
              Get(path: "/named/{article}", view: 'article')
              Get(path: "/passed/{post}", to: @posts.passed)
              Get(path: "/ids/{id}", to: @posts.by_id)
              Get(path: "/two/{first}/{second}", to: @posts.two)
              Get(path: "/summary/{post}", to: @posts.summary)
              Get(path: "/alone/{article}", to: @posts.alone)
              Get(path: "/wrong/{tag}", to: @posts.wrong)
              Get(path: "/none", to: @posts.none)
              Get(path: "/missing/{article}", to: @posts.missing)
            }
            """);
        var output = new StringWriter(CultureInfo.InvariantCulture);
        FileOf("keep.kscript", "import KScript.Data\nimport \"models/post.kscript\"\nimport \"models/tag.kscript\"\n$db->create_table(Post)\n$db->create_table(Tag)\nPost->create(title: 'Hello')\nPost->create(title: 'Second')\nTag->create(name: 'news')\n");
        new KScript.Runtime.Interpreter(KScript.Runtime.KScriptHost.ForText(output)).RunFile(Path.Combine(Project, "keep.kscript"));
        Database.CloseAll();
        return new HttpApp(Project, ProjectConfig.FindAndLoad(Project), null);
    }

    private static readonly KScript.Runtime.Injection.Source RequestContextSource = NoSource;

    private static bool NoSource(KScript.Runtime.InjectRequest request, out object record)
    {
        record = null;
        return false;
    }

    private static string Text(HttpApp app, string path) => WebUtility.HtmlDecode(Get(app, path).Body);

    private static PageResult Get(HttpApp app, string path) =>
        app.Handle(new Dictionary<string, object> { ["method"] = "GET", ["path"] = path, ["body"] = string.Empty, ["headers"] = new Dictionary<string, object>(), ["query"] = new Dictionary<string, object>() });

    [Fact]
    public void AnInputTakesTheRecordTheRouteNames()
    {
        HttpApp app = Blog();
        // The controller found it, and the component is handed the same record.
        Assert.Contains("<h2>Hello</h2>", Get(app, "/posts/1").Body);
        // A route that answers with a view has only the key, so the record is found for the input.
        Assert.Contains("<h2>Second</h2>", Get(app, "/viewed/2").Body);
        Assert.Equal(404, Get(app, "/viewed/99").Status);
        // Two components on one page share the record found for the first.
        Assert.Contains("<h2>Second</h2><h2>Second</h2>", Get(app, "/twice/2").Body.Replace("\n", string.Empty).Replace(" ", string.Empty));
        // The path names it differently: the route's one Post, or the parameter from: names.
        Assert.Contains("<h2>Hello</h2>", Get(app, "/articles/1").Body);
        Assert.Contains("<h3>Second</h3>", Get(app, "/named/2").Body);
        // What the caller passes wins.
        Assert.Contains("<h2>Second</h2>", Get(app, "/passed/1").Body);
    }

    [Fact]
    public void ItWorksInAnyTypeAndOnItsOwn()
    {
        HttpApp app = Blog();
        Assert.Equal("{\"length\":5}", Get(app, "/summary/1").Body);
        Assert.Equal("{\"title\":\"Second\",\"same\":\"Second\"}", Get(app, "/alone/2").Body);
    }

    [Fact]
    public void ARouteWithoutTheRecordSaysSo()
    {
        HttpApp app = Blog();
        Assert.Contains("The route /ids/{id} holds no Post: it has no {post}, and no parameter typed Post. Pass one, such as Post->find($id).", Text(app, "/ids/1"));
        Assert.Contains("holds more than one Post (first, second). Say which with @inject(from: first).", Text(app, "/two/1/2"));
        Assert.Contains("The route's {tag} is a Tag, not a Post.", Text(app, "/wrong/1"));
        Assert.Contains("The route /none holds no Post", Text(app, "/none"));
        Assert.Contains("The route /missing/{article} has no {missing} to take a Post from.", Text(app, "/missing/1"));
    }

    [Fact]
    public void OutsideARouteItIsAnError()
    {
        const string types = """
            import KScript.Data

            model Post {
              title: string
            }

            type Card {
              construct({ post: Post = @inject() })
            }

            type Plain {
              name: string
            }

            type Wrong {
              construct({ count: int = @inject() })
            }

            type NotAModel {
              construct({ plain: Plain = @inject() })
            }

            """;
        Assert.Contains("Card needs 'post': nothing was passed, and there is no route to take a Post from.", Fails(types + "x = Card()").Message);
        Assert.Contains("There is no route to take a Post from.", Fails(types + "x = @inject(Post)").Message);
        Assert.Contains("@inject() on its own needs the model to find: @inject(Post).", Fails(types + "x = @inject()").Message);
        Assert.Contains("@inject() takes a model, such as @inject(Post), but got", Fails(types + "x = @inject(3)").Message);
        Assert.Contains("@inject() takes a model, and there is no model named Nope here.", Fails(types + "x = @inject(Nope)").Message);
        KScript.Runtime.Injection.AddSource(RequestContextSource);
        KScript.Runtime.Injection.AddSource(RequestContextSource);
        Assert.Contains("@inject() fills an input whose type is a model, and 'count' is int.", Fails(types + "x = Wrong()").Message);
        Assert.Contains("@inject() finds the record of a model, and Plain isn't a model.", Fails(types + "x = NotAModel()").Message);
        Assert.Contains("There is no route to take a Post from.", Fails(types + "x = @inject(Post, from: 'a')").Message);
    }

    [Fact]
    public async Task ALiveComponentFindsTheRecordAgainOnEachUpdate()
    {
        Blog();
        using var stop = new CancellationTokenSource();
        var listening = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = HttpServer.RunAsync(Project, ProjectConfig.FindAndLoad(Project), TestServer.FreePort(), "127.0.0.1", null, stop.Token, started: url => listening.SetResult(url));
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(await listening.Task.WaitAsync(TimeSpan.FromSeconds(10))) };
            string html = await http.GetStringAsync("/liked/2");
            Assert.Contains("<p>Second: 0</p>", html);
            // The record stays on the server: the page holds the count and nothing of the post.
            string Attr(string name) => WebUtility.HtmlDecode(Regex.Match(html, name + "=\"([^\"]+)\"").Groups[1].Value);
            http.DefaultRequestHeaders.Add("X-KScript-Live", Attr("data-ks-route"));
            using var clicked = await http.PostAsJsonAsync("/liked/2", new { block = "c", handler = 0, state = Attr("data-ks-state"), args = Attr("data-ks-args-0") });
            string after = await clicked.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, clicked.StatusCode);
            Assert.Contains("<p>Second: 1</p>", after);
        }
        finally
        {
            stop.Cancel();
            await server;
        }
    }
}
