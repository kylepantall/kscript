using System.Text.Json;
using KScript.Http;
using KScript.Projects;

namespace KScript.Tests;

/// <summary>The OpenAPI document made from a site's routes: kscript openapi, /_openapi.json and the /_api page.</summary>
public class OpenApiTests
{
    private const string Controller = """
        kscript {
          import KScript.Http

          /// Someone in the team.
          type Person {
            id: int
            /// What they are called.
            name: string
            role: Role
          }

          enum Role { member, admin }

          type NewPerson(fields: [name: string, role?: Role])
          type Search(fields: [role?: Role, page?: int])

          /// Lists the people.
          /// @returns Person[]
          fn index(query: Search) => []

          /// One person.
          /// Looks them up by id.
          /// @param id The person's id.
          /// @returns Person the person
          fn show(id: int) {
            return @abort(404, message: 'Nobody has that id.')
          }

          /// Adds a person.
          /// @returns Person
          fn store(json: NewPerson) => @response({ id: 3, name: $json.name }, status: 201)

          /// @deprecated Use PATCH instead.
          fn replace(id) => null

          fn destroy(id) => @response(null, status: 204)

          fn page() => @view('home')
        }
        """;

    private const string Api = """
        import KScript.Http
        import "controllers/people.kscript" as people

        Routes {
          Get(path: "/people", to: @people.index)
          Get(path: "/people/{id}", to: @people.show, name: "people.show")
          Get(path: "/files/{path*}", where: "path: '[a-z/]+'") {
            return 'file'
          }
          Get(path: "/by-slug/{slug}", where: "slug: slug", to: @people.index)
          Group(middleware: [auth, throttle]) {
            Post(path: "/people", to: @people.store)
            Put(path: "/people/{id}", to: @people.replace)
            Delete(path: "/people/{id}", to: @people.destroy)
          }
        }
        """;

    private static TempSite Site(params (string Path, string Content)[] more)
    {
        var files = new List<(string, string)> { ("controllers/people.kscript", Controller), ("api.kscript", Api), ("routes.kscript", "import KScript.Http\nimport \"controllers/people.kscript\" as people\n\nRoutes {\n  Get(path: \"/\", to: @people.page)\n}\n"), ("views/home.html", "<h1>Home</h1>") };
        files.AddRange(more);
        return new TempSite(files.ToArray());
    }

    private static JsonElement Document(TempSite site, string kscriptJson = null)
    {
        ProjectConfig project = kscriptJson == null ? null : ProjectConfig.Parse(kscriptJson, Path.Combine(site.Root, "kscript.json"));
        var app = new HttpApp(site.Root, project);
        string json = OpenApi.ToJson(OpenApi.Document(app, app.LoadRoutes(), project));
        return JsonDocument.Parse(json).RootElement;
    }

    private static JsonElement Op(JsonElement doc, string path, string method) => doc.GetProperty("paths").GetProperty(path).GetProperty(method);

    [Fact]
    public void DescribesTheApiRoutes()
    {
        using var site = Site();
        JsonElement doc = Document(site);
        Assert.Equal("3.1.0", doc.GetProperty("openapi").GetString());
        Assert.Equal("1.0.0", doc.GetProperty("info").GetProperty("version").GetString());

        // Only the API routes: routes.kscript's page isn't part of the API while there is an api.* file.
        List<string> paths = doc.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(new[] { "/api/by-slug/{slug}", "/api/files/{path}", "/api/people", "/api/people/{id}" }, paths);

        JsonElement index = Op(doc, "/api/people", "get");
        Assert.Equal("Lists the people.", index.GetProperty("summary").GetString());
        Assert.Equal("people", index.GetProperty("tags")[0].GetString());
        Assert.Equal("#/components/schemas/Person", index.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("items").GetProperty("$ref").GetString());
    }

    [Fact]
    public void TypedQueriesBecomeQueryParameters()
    {
        using var site = Site();
        JsonElement index = Op(Document(site), "/api/people", "get");
        JsonElement[] parameters = index.GetProperty("parameters").EnumerateArray().ToArray();
        Assert.Equal(new[] { "role", "page" }, parameters.Select(p => p.GetProperty("name").GetString()));
        Assert.All(parameters, p => Assert.Equal("query", p.GetProperty("in").GetString()));
        Assert.Equal("#/components/schemas/Role", parameters[0].GetProperty("schema").GetProperty("$ref").GetString());
        Assert.Equal("integer", parameters[1].GetProperty("schema").GetProperty("type").GetString());
        Assert.True(index.GetProperty("responses").TryGetProperty("422", out _));
    }

    [Fact]
    public void PathParametersTakeTheirTypeOrWhereRule()
    {
        using var site = Site();
        JsonElement doc = Document(site);
        JsonElement show = Op(doc, "/api/people/{id}", "get");
        JsonElement id = show.GetProperty("parameters")[0];
        Assert.Equal("path", id.GetProperty("in").GetString());
        Assert.True(id.GetProperty("required").GetBoolean());
        Assert.Equal("integer", id.GetProperty("schema").GetProperty("type").GetString());
        Assert.Equal("The person's id.", id.GetProperty("description").GetString());
        Assert.Equal("people.show", show.GetProperty("operationId").GetString());
        Assert.Equal("Looks them up by id.", show.GetProperty("description").GetString());
        Assert.Equal("the person", show.GetProperty("responses").GetProperty("200").GetProperty("description").GetString());
        // @abort(404) in the controller, and a typed path parameter, can both answer 404.
        Assert.True(show.GetProperty("responses").TryGetProperty("404", out _));

        JsonElement slug = Op(doc, "/api/by-slug/{slug}", "get").GetProperty("parameters")[0].GetProperty("schema");
        Assert.StartsWith("^(?:[a-z0-9]+", slug.GetProperty("pattern").GetString());
        JsonElement file = Op(doc, "/api/files/{path}", "get").GetProperty("parameters")[0];
        Assert.Equal("^(?:[a-z/]+)$", file.GetProperty("schema").GetProperty("pattern").GetString());
        Assert.Contains("rest of the path", file.GetProperty("description").GetString());
    }

    [Fact]
    public void TypedBodiesAuthAndStatuses()
    {
        using var site = Site();
        JsonElement doc = Document(site);
        JsonElement store = Op(doc, "/api/people", "post");
        JsonElement body = store.GetProperty("requestBody");
        Assert.True(body.GetProperty("required").GetBoolean());
        Assert.Equal("#/components/schemas/NewPerson", body.GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());

        JsonElement responses = store.GetProperty("responses");
        Assert.True(responses.TryGetProperty("201", out _));
        Assert.False(responses.TryGetProperty("200", out _));
        foreach (string code in new[] { "400", "401", "422", "429" }) Assert.True(responses.TryGetProperty(code, out _), code);
        Assert.Equal("bearerAuth", store.GetProperty("security")[0].EnumerateObject().Single().Name);
        Assert.Equal("bearer", doc.GetProperty("components").GetProperty("securitySchemes").GetProperty("bearerAuth").GetProperty("scheme").GetString());

        JsonElement destroy = Op(doc, "/api/people/{id}", "delete");
        Assert.Equal("No content", destroy.GetProperty("responses").GetProperty("204").GetProperty("description").GetString());
        Assert.False(destroy.GetProperty("responses").GetProperty("204").TryGetProperty("content", out _));

        JsonElement replace = Op(doc, "/api/people/{id}", "put");
        Assert.True(replace.GetProperty("deprecated").GetBoolean());
        Assert.Contains("Use PATCH instead.", replace.GetProperty("description").GetString());
    }

    [Fact]
    public void TypesBecomeComponentSchemas()
    {
        using var site = Site();
        JsonElement schemas = Document(site).GetProperty("components").GetProperty("schemas");
        JsonElement person = schemas.GetProperty("Person");
        Assert.Equal("Someone in the team.", person.GetProperty("description").GetString());
        Assert.Equal("What they are called.", person.GetProperty("properties").GetProperty("name").GetProperty("description").GetString());
        Assert.Equal("#/components/schemas/Role", person.GetProperty("properties").GetProperty("role").GetProperty("$ref").GetString());
        Assert.Equal(new[] { "id", "name", "role" }, person.GetProperty("required").EnumerateArray().Select(r => r.GetString()));
        Assert.Equal(new[] { "member", "admin" }, schemas.GetProperty("Role").GetProperty("enum").EnumerateArray().Select(r => r.GetString()));
        Assert.True(schemas.TryGetProperty("Error", out _));
    }

    [Fact]
    public void KscriptJsonSetsTheInfoAndWhichRoutes()
    {
        using var site = Site();
        JsonElement doc = Document(site, """{ "name": "team", "app": { "version": "2.1.0" }, "http": { "openapi": { "routes": "all", "description": "The team API." } } }""");
        Assert.Equal("team", doc.GetProperty("info").GetProperty("title").GetString());
        Assert.Equal("2.1.0", doc.GetProperty("info").GetProperty("version").GetString());
        Assert.Equal("The team API.", doc.GetProperty("info").GetProperty("description").GetString());
        JsonElement page = Op(doc, "/", "get");
        Assert.True(page.GetProperty("responses").GetProperty("200").GetProperty("content").TryGetProperty("text/html", out _));

        doc = Document(site, """{ "http": { "openapi": { "title": "People", "version": "0.1" } } }""");
        Assert.Equal("People", doc.GetProperty("info").GetProperty("title").GetString());
        Assert.Equal("0.1", doc.GetProperty("info").GetProperty("version").GetString());
    }

    [Fact]
    public void ServeAnswersTheDocumentAndThePage()
    {
        using var site = Site();
        var app = new HttpApp(site.Root);
        var json = app.OpenApiPage("/_openapi.json", app.Table())!;
        Assert.StartsWith("application/json", json.ContentType);
        Assert.Equal("3.1.0", JsonDocument.Parse(json.Body).RootElement.GetProperty("openapi").GetString());

        var yaml = app.OpenApiPage("/_openapi.yaml", app.Table())!;
        Assert.StartsWith("application/yaml", yaml.ContentType);
        Assert.Contains("openapi: \"3.1.0\"\n", yaml.Body);
        Assert.Contains("  /api/people:\n    get:\n", yaml.Body);
        Assert.Contains("        - name: role\n          in: query\n", yaml.Body);

        var page = app.OpenApiPage("/_api", app.Table())!;
        Assert.Contains("/_openapi.json", page.Body);
        Assert.Null(app.OpenApiPage("/people", app.Table()));

        // Through the server's request handling, and off with "openapi": false.
        var off = new HttpApp(site.Root, ProjectConfig.Parse("""{ "http": { "openapi": false } }""", Path.Combine(site.Root, "kscript.json")));
        Assert.False(off.Settings.OpenApi.Enabled);
    }

    [Theory]
    [InlineData("""{ "http": { "openapi": { "routes": "some" } } }""", "http.openapi.routes should be auto, api, all")]
    [InlineData("""{ "http": { "openapi": { "titel": "x" } } }""", "unknown setting 'http.openapi.titel'")]
    [InlineData("""{ "http": { "openapi": 3 } }""", "http.openapi")]
    public void OpenApiSettingMistakesAreWarned(string json, string warning) =>
        Assert.Contains(ProjectConfig.Parse(json, "/x/kscript.json").Warnings, w => w.Contains(warning));

    [Fact]
    public void ApiRoutesOnlyWhenAskedAndTheEnabledSetting()
    {
        using var site = Site();
        JsonElement doc = Document(site, """{ "http": { "openapi": { "enabled": true, "routes": "api" } } }""");
        Assert.False(doc.GetProperty("paths").TryGetProperty("/", out _));
        Assert.True(doc.GetProperty("paths").TryGetProperty("/api/people", out _));
        Assert.False(ProjectConfig.Parse("""{ "http": { "openapi": { "enabled": false } } }""", "/x/kscript.json").Http.OpenApi.Enabled);
    }

    [Fact]
    public void WhereRulesAndOtherPackagesTypesGiveFormats()
    {
        const string controller = """
            kscript {
              fn find(a: email, b: uuid, c: url, d: password) => null
              fn search(query: Nowhere) => []
            }
            """;
        const string api = """
            import KScript.Http
            import "controllers/other.kscript" as other

            Routes {
              Get(path: "/rules/{a}/{b}/{c}", where: "a: int, b: number, c: uuid") {
                return 'x'
              }
              Get(path: "/find/{a}/{b}/{c}/{d}", to: @other.find)
              Get(path: "/search", to: @other.search)
            }
            """;
        using var site = new TempSite(("controllers/other.kscript", controller), ("api.kscript", api));
        JsonElement doc = Document(site);
        string Schema(string path, int i, string key) =>
            Op(doc, path, "get").GetProperty("parameters")[i].GetProperty("schema").GetProperty(key).GetString();

        Assert.Equal("integer", Schema("/api/rules/{a}/{b}/{c}", 0, "type"));
        Assert.Equal("number", Schema("/api/rules/{a}/{b}/{c}", 1, "type"));
        Assert.Equal("uuid", Schema("/api/rules/{a}/{b}/{c}", 2, "format"));

        Assert.Equal("email", Schema("/api/find/{a}/{b}/{c}/{d}", 0, "format"));
        Assert.Equal("uuid", Schema("/api/find/{a}/{b}/{c}/{d}", 1, "format"));
        Assert.Equal("uri", Schema("/api/find/{a}/{b}/{c}/{d}", 2, "format"));
        Assert.Equal("password", Schema("/api/find/{a}/{b}/{c}/{d}", 3, "format"));

        // A query type that isn't there adds no query parameters.
        Assert.False(Op(doc, "/api/search", "get").TryGetProperty("parameters", out _));
    }

    [Theory]
    [InlineData(200, "OK")]
    [InlineData(201, "Created")]
    [InlineData(202, "Accepted")]
    [InlineData(204, "No content")]
    [InlineData(301, "Moved permanently")]
    [InlineData(302, "Found")]
    [InlineData(304, "Not modified")]
    [InlineData(400, "Bad request")]
    [InlineData(401, "Unauthorized")]
    [InlineData(403, "Forbidden")]
    [InlineData(404, "Not found")]
    [InlineData(405, "Method not allowed")]
    [InlineData(409, "Conflict")]
    [InlineData(410, "Gone")]
    [InlineData(415, "Unsupported media type")]
    [InlineData(422, "Unprocessable content")]
    [InlineData(429, "Too many requests")]
    [InlineData(500, "Server error")]
    [InlineData(503, "Service unavailable")]
    [InlineData(418, "Status 418")]
    public void StatusesHaveTheirReason(int status, string reason) => Assert.Equal(reason, OpenApi.Reason(status));

    [Fact]
    public void YamlWritesNullsBoolsAndNumbers()
    {
        string yaml = OpenApi.ToYaml(new Dictionary<string, object> { ["none"] = null, ["on"] = true, ["off"] = false, ["count"] = 3, ["big"] = 4L, ["ratio"] = 1.5 });
        Assert.Equal("none: null\n\"on\": true\n\"off\": false\ncount: 3\nbig: 4\nratio: 1.5\n", yaml);
    }

    [Fact]
    public void TheSampleSiteHasAFullDocument()
    {
        string sample = ScriptRunner.RepoFile("samples", "http-app");
        var project = ProjectConfig.Load(Path.Combine(sample, "kscript.json"));
        var app = new HttpApp(sample, project);
        JsonElement doc = JsonDocument.Parse(OpenApi.ToJson(OpenApi.Document(app, app.LoadRoutes(), project))).RootElement;
        Assert.Equal("#/components/schemas/Person", Op(doc, "/api/people/{id}", "get").GetProperty("responses").GetProperty("200")
            .GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
        Assert.True(Op(doc, "/api/people/{id}", "get").GetProperty("responses").TryGetProperty("304", out _));
        Assert.False(Op(doc, "/api/people", "post").GetProperty("responses").TryGetProperty("304", out _));
    }
}
