using System.Globalization;
using System.Net;

using KScript.Data;
using KScript.Http;
using KScript.Pages;
using KScript.Projects;

namespace KScript.Tests;

/// <summary>policy for Model { }: @can, @cannot, @authorize, can: on routes and @inject, and the policies folder.</summary>
public sealed class PolicyTests : DataFixture
{
    private const string Models = """
        import KScript.Data
        import KScript.Auth

        model Post {
          title: string
          author_id: int
          published: bool = false
        }

        type User {
          id: int
          role: string = 'reader'
        }

        """;

    private const string PostPolicy = """
        policy for Post {
          fn before(user: User) => $user.role == 'admin' ? true : null

          fn create(user: User) => $user.role == 'editor'
          fn read(user: User?, post: Post) => $post.published or $user?.id == $post.author_id
          fn update(user: User, post: Post) => $user.id == $post.author_id
        }

        """;

    private const string Seed = """
        $db->create_table(Post)
        mine = Post->create(title: 'Mine', author_id: 1)
        live = Post->create(title: 'Live', author_id: 2, published: true)

        """;

    private string[] Asks(string code) => Lines(Models + PostPolicy + Seed + code);

    [Fact]
    public void APolicyAnswersForTheUserAndTheRecord()
    {
        Assert.Equal(new[] { "true", "false", "true", "true", "false", "true", "true", "false" }, Asks("""
            @acting_as(User(id: 1))
            echo @can(update, $mine)
            echo @can(update, $live)
            echo @cannot(update, $live)
            echo @can('read', $live)
            echo @can(create, Post)
            @acting_as(User(id: 3, role: 'editor'))
            echo @can(create, Post)
            echo @authorize(read, $live) == $live
            echo @can(update, $live)
            """));
    }

    [Fact]
    public void BeforeDecidesWhenItAnswers()
    {
        Assert.Equal(new[] { "true", "true", "true" }, Asks("""
            @acting_as(User(id: 9, role: 'admin'))
            echo @can(update, $mine)
            echo @can(create, Post)
            echo @can(read, $mine)
            """));
    }

    [Fact]
    public void AGuestOnlyReachesAnAbilityThatTakesOne()
    {
        Assert.Equal(new[] { "true", "false", "false", "false", "Unauthorized Sign in first.", "Forbidden You are not allowed to read this Post." }, Asks("""
            echo @can(read, $live)
            echo @can(read, $mine)
            echo @can(update, $mine)
            echo @can(create, Post)
            try {
              @authorize(update, $mine)
            } catch err {
              echo "{$err.type} {$err.message}"
            }
            try {
              @authorize(read, $mine)
            } catch err {
              echo "{$err.type} {$err.message}"
            }
            """));
    }

    [Fact]
    public void WhatItCannotAnswerIsAnError()
    {
        string Error(string code) => Fails(Models + PostPolicy + Seed + code).Message;
        Assert.Contains("The Post policy has no 'publish'. It has: create, read, update.", Error("echo @can(publish, $mine)"));
        Assert.Contains("The Post policy has no 'before'.", Error("echo @can(before, $mine)"));
        Assert.Contains("User has no policy. Write one in policies/user.kscript: policy for User { }.", Error("echo @can(read, User(id: 1))"));
        Assert.Contains("@can() asks about a record or a model, such as @can(read, $post) or @can(create, Post), but got", Error("echo @can(read, 3)"));
        Assert.Contains("@can() takes an ability's name first, such as @can(update, $post).", Error("echo @can(3, $mine)"));
        Assert.Contains("Post has two policies", Error("policy for Post {\n}\n"));

        string Declared(string policy) => Fails(Models + policy).Message;
        Assert.Contains("Unknown type 'Nope' for the policy.", Declared("policy for Nope {\n}\n"));
        Assert.Contains("policy for Post { } holds fn abilities only, not echo.", Declared("policy for Post {\n  echo 1\n}\n"));
        Assert.Contains("The Post policy has 'read' twice.", Declared("policy for Post {\n  fn read() => true\n  fn read() => false\n}\n"));
        Assert.Contains("An ability takes the user, then the Post: fn read(user: User, post: Post).", Declared("policy for Post {\n  fn read(a, b, c) => true\n}\n"));
        Assert.Contains("before takes the user only: fn before(user: User).", Declared("policy for Post {\n  fn before(a, b) => true\n}\n"));
        Assert.Contains("A policy goes at the top level of a file", Declared("if true {\n  policy for Post {\n  }\n}\n"));
    }

    [Fact]
    public void TheXmlFormSaysTheSame()
    {
        FileOf("app.ks.xml", """
            <kscript>
              <import package="KScript.Auth" />
              <type name="Note"><field name="owner" type="int" /></type>
              <policy for="Note">
                <fn name="read" params="user, note: Note" value="$user != null and $user.id == $note.owner" />
              </policy>
              <do>@acting_as({ id: 4 })</do>
              <echo>{ @can(read, Note(owner: 4)) } { @can(read, Note(owner: 5)) }</echo>
            </kscript>
            """);
        var output = new StringWriter(CultureInfo.InvariantCulture);
        new KScript.Runtime.Interpreter(KScript.Runtime.KScriptHost.ForText(output)).RunFile(Path.Combine(Project, "app.ks.xml"));
        Assert.Equal("true false", output.ToString().Trim());

        string Broken(string body)
        {
            FileOf("broken.ks.xml", "<kscript><import package=\"KScript.Auth\" /><type name=\"Note\"><field name=\"owner\" type=\"int\" /></type>" + body + "</kscript>");
            return Assert.ThrowsAny<Exception>(() => new KScript.Runtime.Interpreter(KScript.Runtime.KScriptHost.ForText(new StringWriter())).RunFile(Path.Combine(Project, "broken.ks.xml"))).Message;
        }
        Assert.Contains("<policy> needs a model, such as <policy for=\"Post\">.", Broken("<policy for=\"\" />"));
        Assert.Contains("<policy> holds <fn> elements only, not <echo>.", Broken("<policy for=\"Note\"><echo>1</echo></policy>"));
        Assert.Contains("<fn> needs a name, such as <fn name=\"update\">.", Broken("<policy for=\"Note\"><fn name=\"\" value=\"true\" /></policy>"));
        Assert.Contains("<policy> must be a direct child of <kscript>.", Broken("<if test=\"true\"><policy for=\"Note\" /></if>"));
    }

    private HttpApp Blog(string routes = "", string config = "{ \"name\": \"blog\" }")
    {
        Database.CloseAll();
        if (Server != null) DatabaseServer.Reset(Server);
        FileOf("kscript.json", config);
        FileOf("models/post.kscript", "import KScript.Data\n\nmodel Post {\n  title: string\n  author_id: int\n  published: bool = false\n}\n");
        FileOf("models/user.kscript", "type User {\n  id: int\n  role: string = 'reader'\n}\n");
        FileOf("policies/post.kscript", "import KScript.Auth\nimport \"../models/post.kscript\"\nimport \"../models/user.kscript\"\n\n" + PostPolicy);
        FileOf("_components/post-card.kscript", """
            import KScript.Web
            import "../models/post.kscript"

            type PostCard extends Component {
              construct({ post: Post = @inject(can: read) })

              fn render() => ```html
                <h2>{$post.title}</h2>
                ```
            }
            """);
        FileOf("views/show.html", "<import package=\"KScript.Auth\" /><import path=\"../models/post.kscript\" /><post-card /><if test=\"@can(update, @inject(Post))\"><a>Edit</a></if>");
        FileOf("controllers/posts.kscript", """
            import KScript.Http
            import KScript.Auth
            import "../models/post.kscript"

            fn as(request) {
              if $request.query.as != null {
                @acting_as({ id: $request.query.as->to_int(), role: $request.query.role ?? 'reader' })
              }
            }

            fn index() => 'all'
            fn show(request, post: Post) {
              @as($request)
              return $post.title
            }
            fn store(request) {
              @as($request)
              return 'made'
            }
            fn update(request, post: Post) => $post.title
            fn destroy(post: Post) => "gone {$post.id}"
            fn edit(request, post: Post) {
              @as($request)
              @authorize(update, $post)
              return 'editing'
            }
            fn two(a: Post, b: Post) => 'two'
            fn plain(id: int) => 'plain'
            """);
        const string act = """
            fn act(request, next) {
              if $request.query.as != null {
                @acting_as({ id: $request.query.as->to_int(), role: $request.query.role ?? 'reader' })
              }
              return $next($request)
            }

            fn loose(request) => 'inline'

            """;
        const string imports = "import KScript.Http\nimport KScript.Auth\nimport \"../models/post.kscript\"\nimport \"../controllers/posts.kscript\" as posts\nimport \"../controllers/keys.kscript\" as keys\n\n";
        FileOf("routes/web.kscript", imports + act + """
            Routes(middleware: [act]) {
              Get(path: "/view/{post}", view: 'show')
              Get(path: "/edit/{post}", to: @posts.edit)
            }
            """);
        FileOf("routes/api.kscript", imports + act + """
            Routes(middleware: [act]) {
              Put(path: "/posts/{post}", to: @posts.update, can: update)
              Put(path: "/two/{a}/{b}", to: @posts.two, can: update)
              Put(path: "/plain/{id}", to: @posts.plain, can: update)
              Put(path: "/inline/{id}", to: @loose, can: update)
              Resource(path: "/items", to: posts, model: Post, param: "post")
              Resource(path: "/keys", to: keys, model: Post, only: [show])
            ROUTES
            }
            """.Replace("ROUTES", routes));
        FileOf("controllers/keys.kscript", "fn show(id) => \"key {$id}\"\n");
        var output = new StringWriter(CultureInfo.InvariantCulture);
        FileOf("keep.kscript", "import KScript.Data\nimport \"models/post.kscript\"\n$db->create_table(Post)\nPost->create(title: 'Mine', author_id: 1)\nPost->create(title: 'Live', author_id: 2, published: true)\n");
        new KScript.Runtime.Interpreter(KScript.Runtime.KScriptHost.ForText(output)).RunFile(Path.Combine(Project, "keep.kscript"));
        Database.CloseAll();
        return new HttpApp(Project, ProjectConfig.FindAndLoad(Project), null);
    }

    private static PageResult Send(HttpApp app, string method, string path, string accept = null)
    {
        string[] parts = path.Split('?');
        var query = new Dictionary<string, object>();
        foreach (string pair in parts.Length > 1 ? parts[1].Split('&') : Array.Empty<string>()) query[pair.Split('=')[0]] = pair.Split('=')[1];
        var headers = new Dictionary<string, object>();
        if (accept != null) headers["accept"] = accept;
        return app.Handle(new Dictionary<string, object> { ["method"] = method, ["path"] = parts[0], ["url"] = path, ["body"] = string.Empty, ["headers"] = headers, ["query"] = query });
    }

    private static string Text(PageResult page) => WebUtility.HtmlDecode(page.Body);

    [Fact]
    public void ARouteAsksThePolicyBeforeItsController()
    {
        HttpApp app = Blog();
        Assert.Equal("Mine", Send(app, "PUT", "/api/posts/1?as=1").Body);
        PageResult other = Send(app, "PUT", "/api/posts/2?as=1");
        Assert.Equal(403, other.Status);
        Assert.Contains("You are not allowed to update this Post.", Text(other));
        Assert.Equal("Live", Send(app, "PUT", "/api/posts/2?as=7&role=admin").Body);

        // A guest gets a 401 here: the site has no sign-in page to be sent to.
        Assert.Equal(401, Send(app, "PUT", "/api/posts/1").Status);
        Assert.Equal(404, Send(app, "PUT", "/api/posts/99?as=1").Status);

        Assert.Contains("can: update can't tell which record to ask about: the route /api/two/{a}/{b} holds a and b.", Text(Send(app, "PUT", "/api/two/1/2?as=1")));
        Assert.Contains("can: update needs a model in the route /api/plain/{id}", Text(Send(app, "PUT", "/api/plain/1?as=1")));
        Assert.Contains("can: update needs a model in the route /api/inline/{id}", Text(Send(app, "PUT", "/api/inline/1?as=1")));
    }

    [Fact]
    public void AControllerAndAViewAskToo()
    {
        HttpApp app = Blog();
        Assert.Equal("editing", Send(app, "GET", "/edit/1?as=1").Body);
        Assert.Equal(403, Send(app, "GET", "/edit/2?as=1").Status);

        // The view's component takes the post only when the policy lets this user read it.
        string mine = Send(app, "GET", "/view/1?as=1").Body;
        Assert.Contains("<h2>Mine</h2>", mine);
        Assert.Contains("<a>Edit</a>", mine);
        string live = Send(app, "GET", "/view/2?as=1").Body;
        Assert.Contains("<h2>Live</h2>", live);
        Assert.DoesNotContain("<a>Edit</a>", live);
        PageResult hidden = Send(app, "GET", "/view/1?as=5");
        Assert.Equal(403, hidden.Status);
        Assert.Contains("You are not allowed to read this Post.", Text(hidden));
        Assert.Equal(404, Send(app, "GET", "/view/99?as=1").Status);
    }

    [Fact]
    public void AResourceWithAModelAsksForEachAction()
    {
        HttpApp app = Blog();
        Assert.Equal("all", Send(app, "GET", "/api/items").Body);
        Assert.Equal("Live", Send(app, "GET", "/api/items/2").Body);
        Assert.Equal(403, Send(app, "GET", "/api/items/1?as=5").Status);
        Assert.Equal("Mine", Send(app, "GET", "/api/items/1?as=1").Body);
        PageResult refused = Send(app, "POST", "/api/items?as=1");
        Assert.Equal(403, refused.Status);
        Assert.Contains("You are not allowed to create a Post.", refused.Body);
        Assert.Equal("made", Send(app, "POST", "/api/items?as=3&role=editor").Body);
        Assert.Equal(401, Send(app, "POST", "/api/items").Status);
        Assert.Equal("Mine", Send(app, "PUT", "/api/items/1?as=1").Body);
        // The policy has no delete, so the route says what it lacks.
        Assert.Contains("The Post policy has no 'delete'. It has: create, read, update.", Text(Send(app, "DELETE", "/api/items/1?as=1")));
        Assert.Equal(404, Send(app, "GET", "/api/items/99?as=1").Status);

        // A controller that takes the key still has the record asked about.
        Assert.Equal("key 2", Send(app, "GET", "/api/keys/2").Body);
        Assert.Equal(403, Send(app, "GET", "/api/keys/1?as=5").Status);
        Assert.Equal(404, Send(app, "GET", "/api/keys/99").Status);
    }

    [Fact]
    public void AGuestInABrowserIsSentToSignIn()
    {
        HttpApp app = Blog();
        PageResult guest = Send(app, "GET", "/edit/1");
        Assert.Equal(302, guest.Status);
        Assert.Equal("/login", guest.Headers["Location"]);
        Assert.Equal(401, Send(app, "GET", "/edit/1", accept: "application/json").Status);

        // A site with no sign-in page answers 401.
        HttpApp none = Blog(config: "{ \"name\": \"blog\", \"http\": { \"auth\": { \"login\": \"\" } } }");
        Assert.Equal(401, Send(none, "GET", "/edit/1").Status);
    }

    [Fact]
    public void ARouteThatCannotAskSaysWhy()
    {
        Assert.Contains("can: read needs a controller that takes the model, such as to: @posts.update with fn update(post: Post). In a view, use @inject(can: read) or @authorize().",
            Text(Send(Blog("Get(path: \"/v/{post}\", view: 'show', can: read)"), "GET", "/api/items")));
        Assert.Contains("There is no model called Nope here. Import the file that declares it.",
            Text(Send(Blog("Resource(path: \"/n\", to: posts, model: Nope)"), "GET", "/api/items")));
    }

    [Fact]
    public void ThePoliciesFolderCanBeMoved()
    {
        HttpApp app = Blog(config: "{ \"name\": \"blog\", \"paths\": { \"policies\": \"rules\" } }");
        Directory.Move(Path.Combine(Project, "policies"), Path.Combine(Project, "rules"));
        Assert.Equal(403, Send(app, "PUT", "/api/posts/2?as=1").Status);
        Assert.Equal("Mine", Send(app, "PUT", "/api/posts/1?as=1").Body);
    }

    [Fact]
    public void ATypeJoinedToTheModelIsAskedTheSame()
    {
        Assert.Equal(new[] { "true", "false" }, Lines("""
            import KScript.Auth

            type Note {
              owner: int = 0
            }

            type LongNote with [Note] {
              pages: int = 2
            }

            policy for Note {
              fn read(user, note: Note) => $note.owner == 1
            }

            echo @can(read, LongNote(owner: 1))
            echo @can(read, LongNote(owner: 2))
            """));

        // A project file that can't be read leaves the folder out: the model has no policy.
        FileOf("kscript.json", "{ nope");
        Assert.Contains("Note has no policy.", Fails("import KScript.Auth\n\ntype Note {\n  owner: int = 0\n}\n\necho @can(read, Note())\n").Message);
    }
}
