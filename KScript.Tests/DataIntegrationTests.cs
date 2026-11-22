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
}
