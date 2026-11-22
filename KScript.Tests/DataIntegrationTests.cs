using System.Globalization;
using System.Text.Json;

using KScript.Data;
using KScript.Http;
using KScript.Pages;
using KScript.Projects;

namespace KScript.Tests;

/// <summary>KScript.Data with the rest of KScript: routes, OpenAPI, sign-in and tests.</summary>
public sealed class DataIntegrationTests : DataFixture
{
    private const string PostModel = """
        import KScript.Data

        model Post {
          timestamps: yes
          soft_deletes: yes
          hidden: [secret]
          title: string(max: 200)
          views: int = 0
          secret: string = 'x'
          author: User?
        }
        """;

    private const string UserModel = """
        import KScript.Data

        model User {
          name: string
          posts: Post[]
        }
        """;

    private HttpApp Blog()
    {
        Database.CloseAll();
        if (Server != null) DatabaseServer.Reset(Server);
        FileOf("kscript.json", "{ \"name\": \"blog\" }");
        FileOf("models/post.kscript", PostModel);
        FileOf("models/user.kscript", UserModel);
        FileOf("controllers/posts.kscript", """
            import KScript.Http
            import KScript.Data
            import "../models/post.kscript"

            /// One post.
            /// @returns Post the post
            fn show(post: Post) => $post

            /// The posts, a page at a time.
            /// @returns Post[] the page
            fn index(query: map) => $Post->latest()->paginate(page: $query.page ?? 1)
            fn views(post: Post, id: int) => { id: $id, views: $post.views }
            fn remove(post: Post) {
              $post->delete()
              return 'gone'
            }
            """);
        FileOf("routes/api.kscript", """
            KScript.Routes {
              import "../controllers/posts.kscript" as posts

              Get(path: "/posts", to: posts.index)
              Get(path: "/posts/{post}", to: posts.show)
              Get(path: "/posts/{post}/views/{id}", to: posts.views)
              Delete(path: "/posts/{post}", to: posts.remove)
            }
            """);
        Keep("$db->create_table($User)\n$db->create_table($Post)\n$Post->create(title: 'Hello', views: 3)\n$Post->create(title: 'Second')");
        return new HttpApp(Project, ProjectConfig.FindAndLoad(Project), null);
    }

    /// <summary>Runs code against the project's database without emptying it.</summary>
    private string Keep(string code)
    {
        Database.CloseAll();
        var output = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        FileOf("keep.kscript", "import KScript.Data\nimport \"models/post.kscript\"\nimport \"models/user.kscript\"\n" + code + "\n");
        new KScript.Runtime.Interpreter(KScript.Runtime.KScriptHost.ForText(output)).RunFile(Path.Combine(Project, "keep.kscript"));
        return output.ToString().TrimEnd('\n');
    }

    private static PageResult Send(HttpApp app, string method, string path) =>
        app.Handle(new Dictionary<string, object> { ["method"] = method, ["path"] = path, ["body"] = string.Empty, ["headers"] = new Dictionary<string, object>(), ["query"] = new Dictionary<string, object>() });

    [Fact]
    public void AModelParameterIsTheRecordThePathNames()
    {
        HttpApp app = Blog();
        PageResult hello = Send(app, "GET", "/api/posts/1");
        Assert.Equal(200, hello.Status);
        Assert.Contains("\"title\":\"Hello\"", hello.Body);
        Assert.DoesNotContain("secret", hello.Body);
        Assert.Equal("{\"id\":7,\"views\":3}", Send(app, "GET", "/api/posts/1/views/7").Body);

        // No such post, or a key that can't be one: 404.
        PageResult missing = Send(app, "GET", "/api/posts/99");
        Assert.Equal(404, missing.Status);
        Assert.Contains("There is no Post with id 99", missing.Body);
        Assert.Equal(404, Send(app, "GET", "/api/posts/abc").Status);

        // A deleted post is gone too.
        Assert.Equal("gone", Send(app, "DELETE", "/api/posts/2").Body);
        Assert.Equal(404, Send(app, "GET", "/api/posts/2").Status);
    }

    [Fact]
    public void OpenApiDescribesModels()
    {
        HttpApp app = Blog();
        using JsonDocument doc = JsonDocument.Parse(OpenApi.ToJson(OpenApi.Document(app, app.LoadRoutes(), ProjectConfig.FindAndLoad(Project))));
        JsonElement show = doc.RootElement.GetProperty("paths").GetProperty("/api/posts/{post}").GetProperty("get");
        JsonElement key = show.GetProperty("parameters")[0].GetProperty("schema");
        Assert.Equal("integer", key.GetProperty("type").GetString());
        Assert.Equal("The id of the Post.", key.GetProperty("description").GetString());
        Assert.Equal("There is no Post with the {post} in the path.", show.GetProperty("responses").GetProperty("404").GetProperty("description").GetString());

        JsonElement post = doc.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("Post");
        JsonElement properties = post.GetProperty("properties");
        Assert.False(properties.TryGetProperty("secret", out _));
        Assert.True(properties.GetProperty("id").GetProperty("readOnly").GetBoolean());
        Assert.True(properties.GetProperty("created_at").GetProperty("readOnly").GetBoolean());
        Assert.Equal("#/components/schemas/User", properties.GetProperty("author").GetProperty("allOf")[0].GetProperty("$ref").GetString());
        Assert.Contains("->with(author)", properties.GetProperty("author").GetProperty("description").GetString());
        Assert.Equal("integer", properties.GetProperty("author_id").GetProperty("type").GetString());
        JsonElement user = doc.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("User");
        Assert.Equal("array", user.GetProperty("properties").GetProperty("posts").GetProperty("type").GetString());
        Assert.DoesNotContain(user.GetProperty("required").EnumerateArray(), r => r.GetString() == "posts");

        // A paginated index is one page, with its numbers.
        JsonElement page = doc.RootElement.GetProperty("paths").GetProperty("/api/posts").GetProperty("get").GetProperty("responses").GetProperty("200")
            .GetProperty("content").GetProperty("application/json").GetProperty("schema");
        Assert.Equal("object", page.GetProperty("type").GetString());
        Assert.Equal("#/components/schemas/Post", page.GetProperty("properties").GetProperty("items").GetProperty("items").GetProperty("$ref").GetString());
        Assert.Equal("boolean", page.GetProperty("properties").GetProperty("has_more").GetProperty("type").GetString());
        Assert.Contains("\"total\":2", Send(app, "GET", "/api/posts").Body);
    }

    private const string Member = """
        import KScript.Data

        model User {
          timestamps: yes
          hidden: [password]
          email: email
          password: password
          name: string = 'Someone'
          nickname: string?
          posts: Post[]
        }
        """;

    private Browser Members(string model = Member, string users = "User")
    {
        Database.CloseAll();
        if (Server != null) DatabaseServer.Reset(Server);
        FileOf("kscript.json", "{ \"name\": \"members-" + Guid.NewGuid().ToString("N") + "\", \"http\": { \"auth\": { \"users\": \"" + users + "\", \"folder\": \"storage\" } } }");
        FileOf("models/post.kscript", PostModel);
        FileOf("models/user.kscript", model);
        FileOf("controllers/members.kscript", """
            import KScript.Http
            import KScript.Auth
            import KScript.Data

            fn reg(json) => @register($json.email, $json.password, name: $json.name)
            fn colour(json) => @register($json.email, $json.password, colour: 'red')
            fn sign_in(json) => { user: @attempt($json.email, $json.password) }
            fn me() {
              me = @user()
              return { name: $me.name, posts: $me.posts->count(), json: @json($me) }
            }
            fn people(json) {
              u = @users()
              return { count: $u.count, all: $u->all(), found: $u->find($json.id), none: $u->find('99'), email: $u->find_email('JOAN@example.com'), ok: $u->verify('joan@example.com', $json.password) }
            }
            fn update(json) => @users()->update($json.id, nickname: $json.nickname)
            fn renew(json) => @users()->set_password($json.id, $json.password)
            fn remove(json) => { first: @users()->delete($json.id), second: @users()->delete($json.id) }
            """);
        FileOf("routes/api.kscript", """
            KScript.Routes {
              import "../controllers/members.kscript" as m

              Post(path: "/reg", to: m.reg)
              Post(path: "/colour", to: m.colour)
              Post(path: "/attempt", to: m.sign_in)
              Get(path: "/me", to: m.me, middleware: auth)
              Post(path: "/users", to: m.people)
              Post(path: "/update", to: m.update)
              Post(path: "/renew", to: m.renew)
              Post(path: "/remove", to: m.remove)
            }
            """);
        if (model == Member) Keep("$db->create_table($User)\n$db->create_table($Post)");
        return new Browser(new HttpApp(Project, ProjectConfig.FindAndLoad(Project), new StringWriter()));
    }

    private static Dictionary<string, object> Json(PageResult r)
    {
        Assert.True(r.Status == 200, r.Status + " " + r.Body);
        return (Dictionary<string, object>)KScript.Runtime.Values.ParseJson(r.Body);
    }

    [Fact]
    public void SignInUsersCanBeAModel()
    {
        Browser b = Members();
        const string joan = "{ \"email\": \"Joan@Example.com\", \"password\": \"correct horse battery\", \"name\": \"Joan\" }";
        Dictionary<string, object> made = Json(b.Send("POST", "/api/reg", json: joan));
        Assert.Equal(1.0, made["id"]);
        Assert.Equal("Joan@example.com", made["email"]);
        Assert.False(made.ContainsKey("password"));
        Assert.Contains("already signed up", b.Send("POST", "/api/reg", json: joan).Body);
        Assert.Contains("User has no field 'colour'", b.Send("POST", "/api/colour", json: "{ \"email\": \"amy@example.com\", \"password\": \"correct horse battery\" }").Body);

        // The user is a row of the users table, with a hash in its password field.
        Assert.Equal("1 Joan true", Keep("u = $User->find(1)\nok = $u.password->verify('correct horse battery')\necho \"$u.id $u.name $ok\""));
        Keep("$Post->create(title: 'Mine', author_id: 1)");

        Assert.Equal(401, b.Send("GET", "/api/me", headers: new Dictionary<string, object> { ["accept"] = "application/json" }).Status);
        Assert.Equal("Joan@example.com", ((Dictionary<string, object>)Json(b.Send("POST", "/api/attempt", json: joan))["user"])["email"]);
        Dictionary<string, object> me = Json(b.Send("GET", "/api/me"));
        Assert.Equal("Joan", me["name"]);
        Assert.Equal(1.0, me["posts"]);
        Assert.DoesNotContain("password", KScript.Runtime.Values.ToStr(me["json"]));

        Dictionary<string, object> users = Json(b.Send("POST", "/api/users", json: "{ \"id\": \"1\", \"password\": \"correct horse battery\" }"));
        Assert.Equal(1.0, users["count"]);
        Assert.Single((List<object>)users["all"]);
        Assert.Equal("Joan", ((Dictionary<string, object>)users["found"])["name"]);
        Assert.Null(users["none"]);
        Assert.Equal(1.0, ((Dictionary<string, object>)users["email"])["id"]);
        Assert.NotNull(users["ok"]);

        Assert.Equal("Jo", Json(b.Send("POST", "/api/update", json: "{ \"id\": \"1\", \"nickname\": \"Jo\" }"))["nickname"]);
        Assert.Null(Json(b.Send("POST", "/api/update", json: "{ \"id\": \"1\", \"nickname\": null }"))["nickname"]);
        Json(b.Send("POST", "/api/renew", json: "{ \"id\": \"1\", \"password\": \"a new long password\" }"));
        Assert.Null(Json(b.Send("POST", "/api/users", json: "{ \"id\": \"1\", \"password\": \"correct horse battery\" }"))["ok"]);

        Keep("$Post->query()->delete()");
        Dictionary<string, object> removed = Json(b.Send("POST", "/api/remove", json: "{ \"id\": \"1\" }"));
        Assert.Equal(true, removed["first"]);
        Assert.Equal(false, removed["second"]);
        Assert.Equal("0", Keep("echo $User->count()"));
    }

    [Fact]
    public void AUsersModelNeedsAnEmailAndAPassword()
    {
        Assert.Contains("there is no Member model in models", Members(users: "Member").Send("POST", "/api/reg", json: "{}").Body);
        Assert.Contains("doesn't declare a model called User", Members("import KScript.Data\n\nmodel Person {\n  email: email\n}\n").Send("POST", "/api/reg", json: "{}").Body);
        Assert.Contains("User needs an email field", Members("import KScript.Data\n\nmodel User {\n  name: string\n}\n").Send("POST", "/api/reg", json: "{}").Body);
        Assert.Contains("User needs a password field", Members("import KScript.Data\n\nmodel User {\n  email: email\n}\n").Send("POST", "/api/reg", json: "{}").Body);
    }
}
