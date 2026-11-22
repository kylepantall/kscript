using KScript.Http;
using KScript.Output;
using KScript.Pages;
using KScript.Projects;
using KScript.Runtime;
using KScript.Scaffolding;

namespace KScript.Tests;

/// <summary>
/// The api and fullstack project templates, the options they share (--db, --auth, --cache, --docker), the setup
/// wizard, and what they are built on: @api('/path') answered by the app's own routes, and kscript.json test.env.
/// </summary>
public sealed class ProjectTemplateApiTests : IDisposable
{
    private readonly string userTemplates = Path.Combine(Path.GetTempPath(), "kscript-user-templates-" + Guid.NewGuid().ToString("N"));
    private readonly string previousUserTemplates = Environment.GetEnvironmentVariable("KSCRIPT_TEMPLATES");
    private readonly Func<TextWriter, CliPrompt> previousPrompts = MakeCommand.Prompts;

    public ProjectTemplateApiTests() => Environment.SetEnvironmentVariable("KSCRIPT_TEMPLATES", userTemplates);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("KSCRIPT_TEMPLATES", previousUserTemplates);
        MakeCommand.Prompts = previousPrompts;
        try { if (Directory.Exists(userTemplates)) Directory.Delete(userTemplates, recursive: true); } catch (IOException) { }
    }

    private static (int Code, string Out, string Err) Make(string parent, params string[] args)
    {
        var o = new StringWriter { NewLine = "\n" };
        var e = new StringWriter { NewLine = "\n" };
        int code = MakeCommand.Run(new[] { "project" }.Concat(args).Concat(new[] { "--dir", parent }).ToArray(), o, e);
        return (code, o.ToString(), e.ToString());
    }

    private static (int Code, string Out) Test(string root)
    {
        var o = new StringWriter { NewLine = "\n" };
        var e = new StringWriter { NewLine = "\n" };
        int code = KScript.Testing.TestCommand.Run(new[] { root }, o, e, color: false);
        return (code, o.ToString() + e);
    }

    private static List<string> Files(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Where(f => !f.StartsWith(".kscript/", StringComparison.Ordinal) && !f.StartsWith("data/", StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal).ToList();

    private static PageResult Get(HttpApp app, string path, string method = "GET", string json = null, Dictionary<string, object> headers = null)
    {
        headers ??= new Dictionary<string, object>();
        if (json != null) headers["content-type"] = "application/json";
        var request = new Dictionary<string, object> { ["method"] = method, ["path"] = path, ["headers"] = headers };
        if (json != null) request["body"] = json;
        return app.Handle(request);
    }

    // ---- the api template -----------------------------------------------------------------------------------------

    [Fact]
    public void TheApiTemplateMakesAnApiWithAModelAuthCacheAndTests()
    {
        using var parent = new TempSite();
        var (code, output, err) = Make(parent.Root, "shop", "--template", "api");
        Assert.True(code == 0, err);
        string root = Path.Combine(parent.Root, "shop");
        Assert.Equal(new[]
        {
            ".gitignore", ".vscode/extensions.json", "AGENTS.md", "CLAUDE.md", "Makefile", "config/config.test.yaml", "config/config.yaml",
            "controllers/accounts.kscript", "controllers/notes.kscript", "kscript.json", "lib/cache.kscript", "middleware/request_id.kscript",
            "models/note.kscript", "requests/note-input.kscript", "requests/sign-in.kscript", "requests/sign-up.kscript", "routes/api.kscript", "tests/accounts.test.kscript", "tests/api.test.kscript", "tests/notes.test.kscript",
        }, Files(root));
        // Each type has its own file, and the controllers import them.
        Assert.Contains("import \"../requests/note-input.kscript\"", File.ReadAllText(Path.Combine(root, "controllers", "notes.kscript")));
        Assert.DoesNotContain("\ntype ", File.ReadAllText(Path.Combine(root, "controllers", "accounts.kscript")));
        Assert.StartsWith("/// What POST /api/register takes.", File.ReadAllText(Path.Combine(root, "requests", "sign-up.kscript")));
        Assert.Contains("serve the API at http://localhost:8080/api", output);
        Assert.DoesNotContain("Note:", output);
        Assert.DoesNotContain("docker compose", output);

        var config = ProjectConfig.Load(Path.Combine(root, "kscript.json"));
        Assert.Empty(config.Warnings);
        Assert.Equal("test", config.Test.Env);
        Assert.Contains("url: ${DATABASE_URL:-sqlite:data/app.sqlite}", File.ReadAllText(Path.Combine(root, "config", "config.yaml")));
        Assert.Contains("sqlite::memory:", File.ReadAllText(Path.Combine(root, "config", "config.test.yaml")));

        var (tested, report) = Test(root);
        Assert.True(tested == 0, report);
        Assert.Matches(@"Tests:\s+12 passed, 12 total", report);
        // The tests used the test database in memory, not the app's own file.
        Assert.False(File.Exists(Path.Combine(root, "data", "app.sqlite")), report);
        Assert.Null(Environment.GetEnvironmentVariable("KSCRIPT_ENV"));
    }

    [Fact]
    public void TheApiAnswersOverItsRoutes()
    {
        using var parent = new TempSite();
        Assert.Equal(0, Make(parent.Root, "svc", "--template", "api", "--no-auth", "--db", "none").Code);
        string root = Path.Combine(parent.Root, "svc");
        var app = new HttpApp(root, ProjectConfig.Load(Path.Combine(root, "kscript.json")));

        PageResult health = Get(app, "/api/health");
        Assert.Equal(200, health.Status);
        Assert.Contains("\"app\":\"Svc\"", health.Body);
        Assert.Matches("^[0-9a-f-]{36}$", health.Headers["X-Request-Id"]);

        Assert.Equal(201, Get(app, "/api/notes", "POST", "{\"title\": \"Ship it\"}").Status);
        Assert.Contains("Ship it", File.ReadAllText(Path.Combine(root, "data", "notes.json")));
        Assert.Equal(422, Get(app, "/api/notes", "POST", "{\"title\": \"\"}").Status);
        Assert.Contains("Ship it", Get(app, "/api/notes/1").Body);
        Assert.Equal(404, Get(app, "/api/notes/9").Status);
    }

    [Theory]
    [InlineData("--db", "none")]
    [InlineData("--no-auth", "--no-cache")]
    [InlineData("--format", "xml")]
    public void EveryApiOptionMixPassesItsTests(string option, string value)
    {
        using var parent = new TempSite();
        var (code, _, err) = Make(parent.Root, "mix", "--template", "api", option, value);
        Assert.True(code == 0, err);
        string root = Path.Combine(parent.Root, "mix");
        var (tested, report) = Test(root);
        Assert.True(tested == 0, report);
        if (value == "none") Assert.False(File.Exists(Path.Combine(root, "models", "note.kscript")));
        if (value == "--no-cache") Assert.False(File.Exists(Path.Combine(root, "lib", "cache.kscript")));
        if (value == "xml") Assert.True(File.Exists(Path.Combine(root, "routes", "api.ks.xml")));
    }

    [Fact]
    public void DockerAndAnotherDatabaseAddAComposeFileAndANote()
    {
        using var parent = new TempSite();
        var (code, output, err) = Make(parent.Root, "shop", "--template", "api", "--db", "Postgres", "--docker");
        Assert.True(code == 0, err);
        string root = Path.Combine(parent.Root, "shop");
        string compose = File.ReadAllText(Path.Combine(root, "compose.yaml"));
        Assert.Contains("image: postgres:17-alpine", compose);
        Assert.Contains("DATABASE_URL: postgres://shop:${DB_PASSWORD:-secret}@db:5432/shop", compose);
        Assert.Contains("CMD [\"kscript\", \"serve\", \"--host\", \"0.0.0.0\", \"--port\", \"8080\"]", File.ReadAllText(Path.Combine(root, "Dockerfile")));
        Assert.True(File.Exists(Path.Combine(root, ".dockerignore")));
        Assert.Contains("up:\n\tdocker compose up --build", File.ReadAllText(Path.Combine(root, "Makefile")));
        Assert.Contains("postgres://shop:secret@localhost:5432/shop", File.ReadAllText(Path.Combine(root, "config", "config.yaml")));
        Assert.Contains("Note: the app keeps its data in postgres, at the url in config/config.yaml (DATABASE_URL overrides it), and docker compose up starts the database too.", output);
        Assert.Contains("docker compose up       run the API and its database in containers", output);

        Assert.Equal(0, Make(parent.Root, "lite", "--template", "api", "--docker").Code);
        string lite = File.ReadAllText(Path.Combine(parent.Root, "lite", "compose.yaml"));
        Assert.DoesNotContain("db:", lite);
        Assert.Contains("(its SQLite database)", lite);
    }

    [Fact]
    public void OptionsWithChoicesTakeOneOfThem()
    {
        using var parent = new TempSite();
        var (code, _, err) = Make(parent.Root, "x", "--template", "api", "--db", "oracle");
        Assert.Equal(1, code);
        Assert.Contains("--db should be sqlite, postgres, mysql or none, not 'oracle'.", err);
        Assert.Equal("a", ProjectScaffolder.Either(new[] { "a" }));
    }

    [Fact]
    public void AYesNoOptionDoesNotTakeTheFolderName()
    {
        using var parent = new TempSite();
        var (code, _, err) = Make(parent.Root, "--template", "api", "--docker", "shop", "--cache", "no", "--auth", "yes");
        Assert.True(code == 0, err);
        string root = Path.Combine(parent.Root, "shop");
        Assert.True(File.Exists(Path.Combine(root, "compose.yaml")));
        Assert.False(File.Exists(Path.Combine(root, "lib", "cache.kscript")));
        Assert.True(File.Exists(Path.Combine(root, "controllers", "accounts.kscript")));
    }

    // ---- the fullstack template -----------------------------------------------------------------------------------

    [Fact]
    public void TheFullstackTemplatesPagesCallTheApi()
    {
        using var parent = new TempSite();
        var (code, output, err) = Make(parent.Root, "Notes App", "--template", "fullstack");
        Assert.True(code == 0, err);
        string root = Path.Combine(parent.Root, "Notes App");
        Assert.Equal(new[]
        {
            ".gitignore", ".vscode/extensions.json", "AGENTS.md", "CLAUDE.md", "Makefile", "_components/site-nav.html", "_layout.html",
            "config/config.test.yaml", "config/config.yaml", "controllers/notes.kscript", "index.html", "kscript.json", "lib/cache.kscript",
            "lib/notes_client.kscript", "models/note.kscript", "notes.html", "requests/note-input.kscript", "routes/api.kscript", "site.css", "tests/api.test.kscript",
            "tests/notes_client.test.kscript",
        }, Files(root));
        Assert.Contains("and its API under /api", output);
        var (tested, report) = Test(root);
        Assert.True(tested == 0, report);
        Assert.Matches(@"Tests:\s+9 passed, 9 total", report);

        // The page asks the API, in the same process, for the notes it shows; its handlers change them the same way.
        var config = ProjectConfig.Load(Path.Combine(root, "kscript.json"));
        var app = new HttpApp(root, config);
        Assert.Equal(201, Get(app, "/api/notes", "POST", "{\"title\": \"From the API\"}").Status);
        var site = new PageSite(config.SiteRoot);
        PageResult page = PageRenderer.RenderFile(new PageContext(site.Root, new Dictionary<string, object> { ["headers"] = new Dictionary<string, object>() }), site.Resolve("/notes").File);
        Assert.True(page.Status == 200, page.Body);
        Assert.Contains("From the API", page.Body);
        Assert.Contains("1 note left", page.Body);

        string state = System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Match(page.Body, "data-ks-state=\"([^\"]*)\"").Groups[1].Value);
        var json = Values.ToJson(new Dictionary<string, object>
        {
            ["page"] = "/notes",
            ["block"] = "notes",
            ["handler"] = 0d, // the form's on-submit
            ["state"] = state,
            ["args"] = null,
            ["binds"] = new Dictionary<string, object> { ["draft"] = "Second" },
            ["event"] = new Dictionary<string, object>(),
        });
        PageResult added = PageServer.HandleLive(site, json, new Dictionary<string, object>());
        Assert.True(added.Status == 200, added.Body);
        Assert.Contains("2 notes left", added.Body);

        var home = PageRenderer.RenderFile(new PageContext(site.Root), site.Resolve("/").File);
        Assert.Contains("Welcome to Notes App", home.Body);
        Assert.Contains("kscript make auth", home.Body);
    }

    [Fact]
    public void FullstackWithAuthAddsTheSignInPages()
    {
        using var parent = new TempSite();
        var (code, output, err) = Make(parent.Root, "app", "--template", "fullstack", "--auth", "--format", "xml");
        Assert.True(code == 0, err);
        string root = Path.Combine(parent.Root, "app");
        foreach (string f in new[] { "routes/web.ks.xml", "controllers/accounts.ks.xml", "views/auth/login.html", "views/auth/register.html" })
        {
            Assert.True(File.Exists(Path.Combine(root, f)), f);
            Assert.Contains(f, output);
        }
        Assert.Contains("Sign in", File.ReadAllText(Path.Combine(root, "notes.html")));
        var config = ProjectConfig.Load(Path.Combine(root, "kscript.json"));
        Assert.Empty(config.Warnings);
        var (tested, report) = Test(root);
        Assert.True(tested == 0, report);

        // Without a session the API says 401, and the page asks the person to sign in.
        var site = new PageSite(config.SiteRoot);
        PageResult page = PageRenderer.RenderFile(new PageContext(site.Root), site.Resolve("/notes").File);
        Assert.Contains("to keep notes", page.Body);

        // A dry run can't make the sign-in files, so it says so.
        var (dry, dryOut, dryErr) = Make(parent.Root, "dry", "--template", "fullstack", "--auth", "--dry-run");
        Assert.True(dry == 0, dryErr);
        Assert.DoesNotContain("views/auth", dryOut);
    }

    [Fact]
    public void TemplateFoldersCanAskForKitsAndNotes()
    {
        using var t = new TempSite(
            ("kit/kscript-template.json", "{ \"options\": { \"db\": { \"default\": \"a\", \"choices\": [\"a\", \"b\"], \"question\": \"Which?\" } }, \"make\": [\"{{#if db is b}}queue{{/if}}\"], \"notes\": [\"{{#if db is a}}picked a{{/if}}\"] }"),
            ("kit/one.md", "--- make\nwhen: db is a\n---\none"),
            ("kit/two.md", "--- make\nunless: db in a, c\n---\ntwo"),
            ("bad/kscript-template.json", "{ \"options\": { \"db\": { \"choices\": \"a\" } } }"));
        ProjectTemplate kit = ProjectTemplate.Find(Path.Combine(t.Root, "new"), Path.Combine(t.Root, "kit"));
        Assert.Equal("Which?", kit.Options[0].Question);
        Assert.Equal(new[] { "a", "b" }, kit.Options[0].Choices);
        Assert.False(kit.Options[0].IsFlag);

        var (code, output, err) = Make(t.Root, "made", "--template", Path.Combine(t.Root, "kit"));
        Assert.True(code == 0, err);
        Assert.Contains("Note: picked a", output);
        Assert.True(File.Exists(Path.Combine(t.Root, "made", "one.md")));
        Assert.False(File.Exists(Path.Combine(t.Root, "made", "two.md")));

        Assert.Contains("only auth can be made with a project", Make(t.Root, "other", "--template", Path.Combine(t.Root, "kit"), "--db", "b").Err);
        Assert.Contains("db.choices should be a list", Make(t.Root, "bad", "--template", Path.Combine(t.Root, "bad")).Err);
    }

    // ---- the setup wizard -----------------------------------------------------------------------------------------

    private static void Answer(string answers) =>
        MakeCommand.Prompts = w => new CliPrompt(new CliOutput(w, colour: false), new StringReader(answers), interactive: true);

    [Fact]
    public void WithNoOptionsTheWizardAsksForEverything()
    {
        using var parent = new TempSite();
        // Folder, template (by name), format, db (by number), auth, cache, docker, then a bad port and a good one.
        Answer("my-api\napi\n\n4\nn\n\ny\n99999\n3000\n");
        var o = new StringWriter { NewLine = "\n" };
        var e = new StringWriter { NewLine = "\n" };
        int code = MakeCommand.Run(new[] { "project", "--dir", parent.Root }, o, e);
        Assert.True(code == 0, e.ToString() + o);
        string output = o.ToString();
        Assert.Contains("New KScript project", output);
        Assert.Contains("What should the project folder be called?", output);
        Assert.Contains("api        A JSON API", output);
        Assert.Contains("Which database?", output);
        Assert.Contains("Give a number from 1 to 65535.", output);

        string root = Path.Combine(parent.Root, "my-api");
        Assert.Equal(3000, ProjectConfig.Load(Path.Combine(root, "kscript.json")).Serve.Port);
        Assert.True(File.Exists(Path.Combine(root, "controllers", "notes.kscript")));
        Assert.False(File.Exists(Path.Combine(root, "models", "note.kscript")), "db none");
        Assert.False(File.Exists(Path.Combine(root, "controllers", "accounts.kscript")), "no auth");
        Assert.True(File.Exists(Path.Combine(root, "lib", "cache.kscript")), "cache by default");
        Assert.True(File.Exists(Path.Combine(root, "compose.yaml")), "docker");
    }

    [Fact]
    public void TheWizardKeepsAGivenFolderAndOptionsOrNoInteractionSkipIt()
    {
        using var parent = new TempSite();
        // Enter takes every default: the web template.
        Answer("\n\n\n\n\n");
        var (code, output, err) = Make(parent.Root, "site");
        Assert.True(code == 0, err);
        Assert.DoesNotContain("What should the project folder be called?", output);
        Assert.Contains("What kind of project?", output);
        Assert.True(File.Exists(Path.Combine(parent.Root, "site", "tasks.html")));

        // With an option, or -n, nothing is asked.
        Answer(string.Empty);
        Assert.Equal(0, Make(parent.Root, "quiet", "--template", "bare").Code);
        Assert.Equal(0, Make(parent.Root, "quiet2", "-n").Code);
        Assert.True(File.Exists(Path.Combine(parent.Root, "quiet2", "tasks.html")));

        // Input that ends before the wizard is done stops it.
        var (stopped, _, stoppedErr) = Make(parent.Root);
        Assert.Equal(1, stopped);
        Assert.Contains("The input ended", stoppedErr);

        // Not at a terminal, a missing folder is still an error.
        MakeCommand.Prompts = w => new CliPrompt(new CliOutput(w, colour: false), new StringReader(string.Empty), interactive: false);
        Assert.Contains("Give the project a folder name", Make(parent.Root).Err);
    }

    [Fact]
    public void TheWizardChecksTheFolderName()
    {
        using var parent = new TempSite();
        Answer("!!!\nnamed\nbare\n\n\n\n\n\n");
        var (code, output, err) = Make(parent.Root);
        Assert.True(code == 0, err);
        Assert.Contains("Use at least one letter or digit.", output);
        Assert.True(File.Exists(Path.Combine(parent.Root, "named", "main.kscript")));
        Assert.Equal(string.Empty, ProjectWizard.Summary(null));
        Assert.Equal("No colon here", ProjectWizard.Summary("No colon here"));
    }

    // ---- @api('/path'): requests to the app's own routes ------------------------------------------------------------

    private static (string Out, KScriptException Error) RunScript(string file)
    {
        var o = new StringWriter { NewLine = "\n" };
        var host = new KScriptHost(o, new StringWriter()) { WorkingDirectory = Path.GetDirectoryName(file) };
        try
        {
            new Interpreter(host).RunFile(file);
            return (o.ToString(), null);
        }
        catch (KScriptException ex)
        {
            return (o.ToString(), ex);
        }
    }

    [Fact]
    public void ApiWithAPathAsksTheProjectsOwnRoutes()
    {
        using var site = new TempSite(
            ("kscript.json", "{ \"name\": \"local\" }"),
            ("routes/api.kscript", "import KScript.Http\n\nRoutes {\n  Get(path: \"/echo\") {\n    return { query: $request.query, auth: $request.headers.authorization ?? '', cookie: $request.cookies.sid ?? '' }\n  }\n  Post(path: \"/echo\") {\n    return @response({ body: $request.body, type: $request.headers['content-type'] }, status: 201)\n  }\n  Get(path: \"/loop\") {\n    return @api('/api/loop')->json()\n  }\n  Get(path: \"/again\") {\n    return @api('/api/echo')->json()\n  }\n}\n"),
            ("scripts/ask.kscript", "import KScript.Net\nr = @api('/api/echo?a=1&b=two+words&flag')\necho $r.status\necho $r->json().query\necho @api('/api/echo', options: { method: 'post', body: 'plain' })->json()\necho @api('/api/echo', options: { method: 'POST', body: { x: 1 }, headers: { 'Content-Type': 'application/merge+json' } })->json().type\necho @api('/nothing').status\necho @api('/api/missing').status\n"),
            ("scripts/loop.kscript", "import KScript.Net\necho @api('/api/loop').status\n"));
        var (output, error) = RunScript(Path.Combine(site.Root, "scripts", "ask.kscript"));
        Assert.Null(error);
        Assert.Contains("200", output);
        Assert.Contains("\"b\": \"two words\"", output);
        Assert.Contains("\"flag\": \"\"", output);
        Assert.Contains("\"body\": \"plain\"", output);
        Assert.Contains("text/plain; charset=utf-8", output);
        Assert.Contains("application/merge+json", output);
        Assert.Contains("404", output);

        // A route that calls itself stops after a few rounds, with a 500 for the request that went too deep.
        var (looped, loopError) = RunScript(Path.Combine(site.Root, "scripts", "loop.kscript"));
        Assert.Null(loopError);
        Assert.Contains("500", looped);
    }

    [Fact]
    public void ARequestMadeWhileAnsweringOneSendsOnItsCookiesAndToken()
    {
        using var site = new TempSite(
            ("kscript.json", "{ \"name\": \"fwd\" }"),
            ("routes/api.kscript", "import KScript.Http\nimport KScript.Net\n\nRoutes {\n  Get(path: \"/who\") {\n    return { auth: $request.headers.authorization ?? '', sid: $request.cookies.sid ?? '' }\n  }\n  Get(path: \"/relay\") {\n    return @api('/api/who')->json()\n  }\n  Get(path: \"/own\") {\n    return @api('/api/who', options: { headers: { Cookie: 'sid=mine' } })->json()\n  }\n}\n"),
            ("page.html", "<import package=\"KScript.Net\" />\n<p>{ @api('/api/who')->json().sid }</p>\n"));
        var app = new HttpApp(site.Root, ProjectConfig.Load(Path.Combine(site.Root, "kscript.json")));
        var request = new Dictionary<string, object>
        {
            ["method"] = "GET",
            ["path"] = "/api/relay",
            ["headers"] = new Dictionary<string, object> { ["authorization"] = "Bearer t0k", ["cookie"] = "sid=abc" },
            ["cookies"] = new Dictionary<string, object> { ["sid"] = "abc" },
        };
        PageResult relayed = app.Handle(request);
        Assert.Contains("\"auth\":\"Bearer t0k\"", relayed.Body);
        Assert.Contains("\"sid\":\"abc\"", relayed.Body);

        request["path"] = "/api/own";
        request.Remove("params");
        PageResult own = app.Handle(request);
        Assert.Contains("\"auth\":\"Bearer t0k\"", own.Body);
        Assert.DoesNotContain("\"sid\":\"abc\"", own.Body);

        // A page sends on the cookies of the person viewing it.
        PageResult page = site.Render("page.html", new Dictionary<string, object>
        {
            ["headers"] = new Dictionary<string, object>(),
            ["cookies"] = new Dictionary<string, object> { ["sid"] = "viewer" },
        });
        Assert.Contains("<p>viewer</p>", page.Body);
    }

    [Fact]
    public void ApiWithAPathNeedsAProjectWithRoutes()
    {
        using var lone = new TempSite(("ask.kscript", "import KScript.Net\necho @api('/api/x').status\n"));
        var (_, error) = RunScript(Path.Combine(lone.Root, "ask.kscript"));
        Assert.NotNull(error);
        Assert.Contains("isn't in a project with routes", error.Message);

        using var bare = new TempSite(("kscript.json", "{ }"), ("ask.kscript", "import KScript.Net\necho @api('/api/x').status\n"));
        Assert.Contains("isn't in a project with routes", RunScript(Path.Combine(bare.Root, "ask.kscript")).Error.Message);
        // Asking twice reuses the app it made for the project.
        Assert.Contains("isn't in a project with routes", RunScript(Path.Combine(bare.Root, "ask.kscript")).Error.Message);

        Assert.Contains("or a path on this app", RunScript(Write(lone, "bad.kscript", "import KScript.Net\n@api('ftp://x')\n")).Error.Message);
    }

    private static string Write(TempSite site, string path, string text)
    {
        site.Write(path, text);
        return Path.Combine(site.Root, path);
    }

    [Fact]
    public void LocalRequestsNeedKScriptHttpAndStopWhenNestedTooDeep()
    {
        Func<KScriptHost, string, Dictionary<string, object>, LocalResponse> handler = LocalRequests.Handler;
        try
        {
            LocalRequests.Handler = null;
            var ex = Assert.Throws<KScriptException>(() => LocalRequests.Send(null, ".", new Dictionary<string, object>()));
            Assert.Contains("needs KScript.Http", ex.Message);

            int calls = 0;
            LocalRequests.Handler = (host, folder, request) =>
            {
                calls++;
                return LocalRequests.Send(host, folder, request);
            };
            var deep = Assert.Throws<KScriptException>(() => LocalRequests.Send(null, ".", new Dictionary<string, object>()));
            Assert.Contains("nested more than 8 deep", deep.Message);
            Assert.Equal(LocalRequests.MaxDepth, calls);
        }
        finally
        {
            LocalRequests.Handler = handler;
        }
        LocalApi.Install();
        Assert.Same(handler, LocalRequests.Handler);
    }

    [Fact]
    public void ServedAppsAnswerLocalRequestsWhileTheyServe()
    {
        using var site = new TempSite(("kscript.json", "{ }"), ("routes/api.kscript", "import KScript.Http\n\nRoutes {\n  Get(path: \"/n\") {\n    return { n: 1 }\n  }\n}\n"));
        var app = new HttpApp(site.Root, ProjectConfig.Load(Path.Combine(site.Root, "kscript.json")));
        LocalApi.Serve(app);
        try
        {
            LocalResponse r = LocalRequests.Send(new KScriptHost(TextWriter.Null, TextWriter.Null), site.Root, new Dictionary<string, object> { ["method"] = "GET", ["path"] = "/api/n", ["headers"] = new Dictionary<string, object>() });
            Assert.Equal(200, r.Status);
            Assert.Equal("OK", r.Reason);
        }
        finally
        {
            LocalApi.Stop(app);
        }
        Assert.Null(LocalRequests.Send(new KScriptHost(TextWriter.Null, TextWriter.Null), Path.GetTempPath(), new Dictionary<string, object> { ["method"] = "GET", ["path"] = "/x", ["headers"] = new Dictionary<string, object>() }));
    }

    [Fact]
    public void LocalAnswersCarryTheirCookies()
    {
        using var site = new TempSite(
            ("kscript.json", "{ }"),
            ("routes/api.kscript", "import KScript.Http\nimport KScript.Auth\n\nRoutes {\n  Get(path: \"/c\") {\n    $session->set('k', 'v')\n    return { ok: true }\n  }\n}\n"));
        LocalResponse r = LocalRequests.Send(new KScriptHost(TextWriter.Null, TextWriter.Null), site.Root,
            new Dictionary<string, object> { ["method"] = "GET", ["path"] = "/api/c", ["headers"] = new Dictionary<string, object>() });
        Assert.Equal(200, r.Status);
        Assert.True(r.Headers.ContainsKey("Set-Cookie"));
    }

    // ---- test.env and $config in routes, pages and tests ------------------------------------------------------------

    [Fact]
    public void TestEnvPicksTheEnvironmentTestsRunIn()
    {
        using var site = new TempSite(
            ("kscript.json", "{ \"config\": { \"root\": \"config\" }, \"test\": { \"env\": \"test\" } }"),
            ("config/config.yaml", "where: dev\n"),
            ("config/config.test.yaml", "where: test\n"),
            ("tests/env.test.kscript", "test(name: \"env\") {\n  expect(value: $config.where, equals: 'test')\n  expect(value: @env('KSCRIPT_ENV'), equals: 'test')\n}\n"));
        var (code, report) = Test(site.Root);
        Assert.True(code == 0, report);
        Assert.Null(Environment.GetEnvironmentVariable("KSCRIPT_ENV"));

        // A test file given by name finds the project around it.
        var o = new StringWriter();
        Assert.True(KScript.Testing.TestCommand.Run(new[] { Path.Combine(site.Root, "tests", "env.test.kscript") }, o, o, color: false) == 0, o.ToString());

        // KSCRIPT_ENV that is already set wins.
        Environment.SetEnvironmentVariable("KSCRIPT_ENV", "staging");
        try
        {
            Assert.Equal(1, KScript.Testing.TestCommand.Run(new[] { site.Root }, new StringWriter(), new StringWriter(), color: false));
        }
        finally
        {
            Environment.SetEnvironmentVariable("KSCRIPT_ENV", null);
        }

        Assert.Contains("test.env should be the name of an environment", ProjectConfig.Parse("{ \"test\": { \"env\": 3 } }", "kscript.json").Warnings.Single());
        // A broken kscript.json leaves the environment alone, and the test run reports it.
        using var broken = new TempSite(("kscript.json", "{ nope"), ("a.test.kscript", "test(name: \"a\") { }\n"));
        KScript.Testing.TestCommand.Run(new[] { Path.Combine(broken.Root, "a.test.kscript") }, new StringWriter(), new StringWriter(), color: false);
        Assert.Null(Environment.GetEnvironmentVariable("KSCRIPT_ENV"));
        // --filter's value isn't a folder, so it isn't looked in for a project.
        Assert.Equal(1, KScript.Testing.TestCommand.Run(new[] { "--filter", "a", Path.Combine(broken.Root, "nowhere") }, new StringWriter(), new StringWriter(), color: false));
    }

    [Fact]
    public void RoutesAndPagesReadTheProjectsConfigFolder()
    {
        using var site = new TempSite(
            ("kscript.json", "{ \"config\": { \"root\": \"config\" } }"),
            ("config/config.yaml", "app:\n  name: Configured\n"),
            ("routes/api.kscript", "import KScript.Http\n\nRoutes {\n  Get(path: \"/name\") {\n    return { name: $config.app.name }\n  }\n}\n"),
            ("blog/page.html", "<p>{ $config.app.name }</p>\n"));
        var app = new HttpApp(site.Root, ProjectConfig.Load(Path.Combine(site.Root, "kscript.json")));
        Assert.Contains("Configured", Get(app, "/api/name").Body);
        Assert.Contains("<p>Configured</p>", site.Render("blog/page.html").Body);

        // Without config.root, the files are read from the project folder; a broken kscript.json is left to report itself.
        using var plain = new TempSite(("kscript.json", "{ }"), ("config.yaml", "app:\n  name: Plain\n"), ("sub/page.html", "<p>{ $config.app.name }</p>\n"));
        Assert.Contains("<p>Plain</p>", plain.Render("sub/page.html").Body);
        using var broken = new TempSite(("kscript.json", "{ nope"), ("page.html", "<p>{ $config->get('x', 'none') }</p>\n"));
        Assert.Contains("<p>none</p>", broken.Render("page.html").Body);
    }
}
