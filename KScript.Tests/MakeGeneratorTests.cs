using System.Text.RegularExpressions;

using KScript.Pages;
using KScript.Projects;
using KScript.Scaffolding;

namespace KScript.Tests;

/// <summary>kscript make route, controller, request, view and the other generators, and the command directory.</summary>
public sealed class MakeGeneratorTests : IDisposable
{
    private readonly string userTemplates = Path.Combine(Path.GetTempPath(), "kscript-user-templates-" + Guid.NewGuid().ToString("N"));
    private readonly string previousUserTemplates = Environment.GetEnvironmentVariable("KSCRIPT_TEMPLATES");
    private readonly TempSite parent = new TempSite();

    public MakeGeneratorTests() => Environment.SetEnvironmentVariable("KSCRIPT_TEMPLATES", userTemplates);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("KSCRIPT_TEMPLATES", previousUserTemplates);
        parent.Dispose();
    }

    private static (int Code, string Out, string Err) Make(string dir, params string[] args)
    {
        var o = new StringWriter { NewLine = "\n" };
        var e = new StringWriter { NewLine = "\n" };
        int code = MakeCommand.Run(args.Concat(new[] { "--dir", dir }).ToArray(), o, e);
        return (code, o.ToString(), e.ToString());
    }

    /// <summary>A new bare project with a website and an API, in the given format.</summary>
    private string Project(string format = "kscript", string name = "app")
    {
        var o = new StringWriter();
        var e = new StringWriter();
        int code = MakeCommand.Run(new[] { "project", name, "--bare", "--web", "--api", "--format", format, "--dir", parent.Root }, o, e);
        Assert.True(code == 0, e.ToString());
        return Path.Combine(parent.Root, name);
    }

    private static string Read(string dir, string file) => File.ReadAllText(Path.Combine(dir, file)).Replace("\r\n", "\n");

    private static void AssertNoErrors(string dir)
    {
        var check = new StringWriter();
        KScript.LanguageServer.CheckCommand.Run(new[] { dir }, check);
        Assert.DoesNotMatch(@"[1-9]\d* errors?\b", check.ToString().Split('\n').Last(l => l.Contains("checked")));
        Assert.True(!Regex.IsMatch(check.ToString(), @": error "), check.ToString());
    }

    private static PageResult Request(string dir, string method, string path, string json = null)
    {
        var app = new KScript.Http.HttpApp(dir, ProjectConfig.Load(Path.Combine(dir, "kscript.json")));
        var request = new Dictionary<string, object> { ["method"] = method, ["path"] = path };
        if (json != null)
        {
            request["body"] = json;
            request["headers"] = new Dictionary<string, object> { ["content-type"] = "application/json" };
        }
        return app.Handle(request);
    }

    /// <summary>Splits an example command line the way a shell would: words, and "quoted words".</summary>
    private static string[] Words(string command) =>
        Regex.Matches(command, "\"([^\"]*)\"|(\\S+)").Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).ToArray();

    [Fact]
    public void RoutesAddThemselvesToTheRoutesFileWithTheirController()
    {
        string app = Project();

        var (code, output, err) = Make(app, "route", "users");
        Assert.True(code == 0, err);
        Assert.Contains("Created routes/web.kscript", output);
        Assert.Contains("Created controllers/users.kscript", output);
        Assert.Contains("Get(path: \"/users\", to: users.index)", output);
        Assert.Contains("open /users", output);

        Assert.Equal(0, Make(app, "route", "users/{id}", "--where", "id: int").Code);
        (code, output, err) = Make(app, "route", "users", "--method", "post");
        Assert.True(code == 0, err);
        Assert.Contains("Added store() to controllers/users.kscript", output);
        Assert.Contains("send POST /users", output);

        Assert.Equal(
            "// Web routes, served by kscript serve (see the Routes and APIs guide). kscript routes lists them.\n" +
            "KScript.Routes {\n" +
            "  import \"../controllers/users.kscript\" as users\n" +
            "\n" +
            "  Get(path: \"/users\", to: users.index)\n" +
            "  Get(path: \"/users/{id}\", to: users.show, where: \"id: int\")\n" +
            "  Post(path: \"/users\", to: users.store)\n" +
            "}\n", Read(app, "routes/web.kscript"));

        string controller = Read(app, "controllers/users.kscript");
        Assert.Contains("fn index() {", controller);
        Assert.Contains("fn show(id) {", controller);
        Assert.Contains("fn store(json) {", controller);
        // store() uses @response, so the controller now imports KScript.Http, after its opening comments.
        Assert.Matches(@"come from import KScript.Http\.\nimport KScript.Http\n\n/// GET /users", controller);

        AssertNoErrors(app);
        Assert.Equal("[]", Request(app, "GET", "/users").Body.Trim());
        Assert.Contains("\"id\":\"7\"", Request(app, "GET", "/users/7").Body);
        // /users/abc breaks the where rule, so no route answers it (kscript serve then gives 404).
        Assert.True(Request(app, "GET", "/users/abc") is null or { Status: 404 });
        var stored = Request(app, "POST", "/users", "{\"name\": \"Ann\"}");
        Assert.Equal(201, stored.Status);
        Assert.Contains("Ann", stored.Body);

        // The same route again is refused, and nothing changes.
        string before = Read(app, "routes/web.kscript");
        (code, _, err) = Make(app, "route", "users");
        Assert.Equal(1, code);
        Assert.Contains("already has", err);
        Assert.Equal(before, Read(app, "routes/web.kscript"));
    }

    [Fact]
    public void ApiRoutesResourcesViewsAndOtherControllers()
    {
        string app = Project();

        var (code, output, err) = Make(app, "route", "people", "--resource", "--api");
        Assert.True(code == 0, err);
        Assert.Contains("Added to routes/api.kscript", output);
        string api = Read(app, "routes/api.kscript");
        Assert.Contains("  import \"../controllers/hello.kscript\" as hello\n  import \"../controllers/people.kscript\" as people\n", api);
        Assert.Contains("  Get(path: \"/hello\", to: hello.show)\n  Resource(path: \"/people\", to: people)\n}", api);
        foreach (string action in new[] { "index", "show", "store", "update", "destroy" })
            Assert.Contains($"fn {action}(", Read(app, "controllers/people.kscript"));

        (code, output, err) = Make(app, "route", "about", "--view", "about");
        Assert.True(code == 0, err);
        Assert.Contains("Created views/about.html", output);
        Assert.Contains("Get(path: \"/about\", view: \"about\")", Read(app, "routes/web.kscript"));

        (code, _, err) = Make(app, "route", "reports/export", "--to", "reports.export", "--name", "reports.export");
        Assert.True(code == 0, err);
        Assert.Contains("Get(path: \"/reports/export\", to: reports.export, name: \"reports.export\")", Read(app, "routes/web.kscript"));
        Assert.Contains("fn export() {", Read(app, "controllers/reports.kscript"));

        // A page-style [slug] works too, and the controller's function takes the parameter by its name.
        Assert.Equal(0, Make(app, "route", "posts/[slug]").Code);
        Assert.Contains("Get(path: \"/posts/{slug}\", to: posts.show)", Read(app, "routes/web.kscript"));
        Assert.Contains("fn show(slug) {\n  return { slug: $slug }", Read(app, "controllers/posts.kscript"));

        // An admin/users controller doesn't take the users name from another import.
        Assert.Equal(0, Make(app, "route", "users").Code);
        Assert.Equal(0, Make(app, "route", "admin/users", "--method", "delete", "--controller", "admin/users").Code);
        Assert.Contains("import \"../controllers/admin/users.kscript\" as admin_users", Read(app, "routes/web.kscript"));
        Assert.Contains("Delete(path: \"/admin/users\", to: admin_users.destroy)", Read(app, "routes/web.kscript"));

        AssertNoErrors(app);
        Assert.Equal(201, Request(app, "POST", "/api/people", "{\"name\": \"Ann\"}").Status);
        Assert.Equal(204, Request(app, "DELETE", "/api/people/1").Status);
        Assert.Contains("<h1>About</h1>", Request(app, "GET", "/about").Body);
        Assert.Contains("export", Request(app, "GET", "/reports/export").Body);
    }

    [Fact]
    public void XmlProjectsGetXmlRoutesAndControllers()
    {
        string app = Project("xml");
        Assert.Equal(0, Make(app, "route", "users").Code);
        var (code, output, err) = Make(app, "route", "users/{id}");
        Assert.True(code == 0, err);
        Assert.Contains("<Get path=\"/users/{id}\" to=\"users.show\" />", output);
        Assert.Equal(0, Make(app, "route", "users", "--method", "post").Code);
        Assert.Equal(0, Make(app, "route", "tasks", "--resource", "--api").Code);

        Assert.Equal(
            "<!-- Web routes, served by kscript serve (see the Routes and APIs guide). kscript routes lists them. -->\n" +
            "<KScript.Routes>\n" +
            "  <import src=\"../controllers/users.ks.xml\" as=\"users\" />\n" +
            "\n" +
            "  <Get path=\"/users\" to=\"users.index\" />\n" +
            "  <Get path=\"/users/{id}\" to=\"users.show\" />\n" +
            "  <Post path=\"/users\" to=\"users.store\" />\n" +
            "</KScript.Routes>\n", Read(app, "routes/web.ks.xml"));
        Assert.Contains("<Resource path=\"/tasks\" to=\"tasks\" />", Read(app, "routes/api.ks.xml"));
        string controller = Read(app, "controllers/users.ks.xml");
        Assert.Contains("<kscript>\n  <import package=\"KScript.Http\" />", controller);
        Assert.Contains("<fn name=\"store\" params=\"json\"", controller);
        Assert.False(File.Exists(Path.Combine(app, "controllers", "users.kscript")));

        AssertNoErrors(app);
        Assert.Equal(201, Request(app, "POST", "/users", "{\"name\": \"Ann\"}").Status);
        Assert.Contains("\"id\":\"3\"", Request(app, "GET", "/users/3").Body);
    }

    [Fact]
    public void RoutesFollowKscriptJsonAndExistingFiles()
    {
        using var site = new TempSite(
            ("kscript.json", "{ \"http\": { \"routes\": [ { \"file\": \"routes/web.kscript\" }, { \"file\": \"routes/v1.kscript\", \"prefix\": \"/api/v1\", \"api\": true } ] } }"),
            ("routes/web.kscript", "KScript.Routes {\n  import \"../controllers/home.kscript\" as home\n  Get(path: \"/\", to: home.index)\n}\n"),
            ("controllers/home.kscript", "kscript {\n  fn index() => 'home'\n}\n"));

        var (code, output, err) = Make(site.Root, "route", "/", "--action", "about", "--method", "get", "--dry-run");
        Assert.Equal(1, code);
        Assert.Contains("already has a GET / route", err);

        (code, output, err) = Make(site.Root, "route", "about", "--controller", "home", "--action", "about", "--dry-run");
        Assert.True(code == 0, err);
        Assert.Contains("Would add to routes/web.kscript", output);
        Assert.Contains("Would add about() to controllers/home.kscript", output);
        Assert.Equal("kscript {\n  fn index() => 'home'\n}\n", Read(site.Root, "controllers/home.kscript"));

        Assert.Equal(0, Make(site.Root, "route", "about", "--controller", "home", "--action", "about").Code);
        Assert.Equal("KScript.Routes {\n  import \"../controllers/home.kscript\" as home\n  Get(path: \"/\", to: home.index)\n  Get(path: \"/about\", to: home.about)\n}\n", Read(site.Root, "routes/web.kscript"));
        // A controller written as a kscript { } block gets the function inside the block.
        Assert.Equal("kscript {\n  fn index() => 'home'\n\n  /// About.\n  fn about() {\n    return { action: 'about' }\n  }\n}\n", Read(site.Root, "controllers/home.kscript"));

        (code, output, err) = Make(site.Root, "route", "status", "--api");
        Assert.True(code == 0, err);
        Assert.Contains("Created routes/v1.kscript", output);
        Assert.Contains("open /api/status", output);
        Assert.Contains("import \"../controllers/status.kscript\" as status\n\n  Get(path: \"/status\", to: status.index)", Read(site.Root, "routes/v1.kscript"));
    }

    [Fact]
    public void RouteMistakesAreExplained()
    {
        string app = Project();
        Assert.Contains("--method should be one of", Make(app, "route", "users", "--method", "fetch").Err);
        Assert.Contains("Resource adds /{id} itself", Make(app, "route", "people/{id}", "--resource").Err);
        Assert.Contains("--resource or --view, not both", Make(app, "route", "people", "--resource", "--view", "x").Err);
        Assert.Contains("names a controller function", Make(app, "route", "people", "--to", "people").Err);
        Assert.Contains("is not a route path", Make(app, "route", "a/../b").Err);
        Assert.Contains("Give the route a path", Make(app, "route", " ").Err);

        // The route template only adds to a file, so it can't be used to make one.
        var ex = Assert.Throws<StubException>(() => Scaffolder.Make(new MakeRequest { Type = "route", Name = "users", Root = app }));
        Assert.Contains("adds to a file", ex.Message);

        using var broken = new TempSite(("routes/web.kscript", "// no routes block\necho 'hi'\n"));
        Assert.Contains("has no KScript.Routes { … } block", Make(broken.Root, "route", "users").Err);
    }

    [Fact]
    public void AProjectCanChangeWhatARouteLooksLike()
    {
        string app = Project();
        Assert.Equal(0, Make(app, "templates", "route", "controller").Code);
        string template = Path.Combine(app, "_templates", "route.kscript");
        File.WriteAllText(template, File.ReadAllText(template).Replace("to: {{ alias }}.{{ action }}", "to: {{ alias }}.{{ action }}, middleware: log"));

        var (code, output, err) = Make(app, "route", "users");
        Assert.True(code == 0, err);
        Assert.Contains("Get(path: \"/users\", to: users.index, middleware: log)", Read(app, "routes/web.kscript"));
    }

    [Fact]
    public void EveryBuiltInTypeIsInTheDirectoryWithAnExampleThatWorks()
    {
        var types = Scaffolder.BuiltInTemplates();
        Assert.Equal(Scaffolder.Categories.SelectMany(c => c.Types).OrderBy(t => t), types.Select(t => t.Type).OrderBy(t => t));
        foreach (var format in new[] { "kscript", "xml" })
        {
            string app = Project(format, "examples-" + format);
            File.WriteAllText(Path.Combine(app, "theme.json"), "{ \"colors\": { \"primary\": \"#aa0000\" } }");
            Directory.CreateDirectory(Path.Combine(app, "data"));
            File.WriteAllText(Path.Combine(app, "data", "order.json"), "{ \"id\": 1, \"total\": 9.5 }");
            foreach (StubInfo t in types)
            {
                Assert.False(string.IsNullOrEmpty(t.Description), t.Type);
                Assert.False(string.IsNullOrEmpty(t.Usage), t.Type);
                Assert.NotEmpty(t.Examples);
                foreach (string example in t.Examples)
                {
                    string[] words = Words(example);
                    Assert.Equal(new[] { "kscript", "make", t.Type }, words.Take(3));
                    // Examples can make the same file twice (a route makes a controller, then make controller does).
                    var (code, _, err) = Make(app, words.Skip(2).Concat(t.Type == "route" ? Array.Empty<string>() : new[] { "--force" }).ToArray());
                    Assert.True(code == 0, $"{example}: {err}");
                }
            }
            AssertNoErrors(app);
            var fmt = new StringWriter();
            Assert.True(KScript.LanguageServer.FormatCommand.Run(new[] { "--check", app }, fmt) == 0, fmt.ToString());
        }
    }

    [Fact]
    public void ExtensionsGoInTheExtensionsFolderAndApplyToTheProject()
    {
        string app = Project();
        var (code, output, err) = Make(app, "extension", "date", "--methods", "json,is_weekend");
        Assert.True(code == 0, err);
        Assert.Contains("Created extensions/date.kscript", output);
        Assert.Contains("->json(), ->is_weekend()", output);
        Assert.Equal(
            "/// Methods for every date in the project. $this is the date a method is called on.\n"
            + "extend date {\n  /// What json does.\n  fn json() => $this\n  /// What is_weekend does.\n  fn is_weekend() => $this\n}\n",
            Read(app, "extensions/date.kscript"));
        Assert.Equal(0, Make(app, "extension", "text/string").Code);
        Assert.Contains("extend string {\n  // fn json() => $this->to_json()\n}", Read(app, "extensions/text/string.kscript"));
        File.WriteAllText(Path.Combine(app, "try.kscript"), "echo \"{ @date('2026-11-21')->json() == @date('2026-11-21') }\"\n");
        AssertNoErrors(app);
        var o = new StringWriter { NewLine = "\n" };
        new KScript.Runtime.Interpreter(KScript.Runtime.KScriptHost.ForText(o, string.Empty)).RunFile(Path.Combine(app, "try.kscript"));
        Assert.Equal("true\n", o.ToString());

        string xml = Project("xml", "xml-app");
        Assert.Equal(0, Make(xml, "extension", "list", "--methods", "second").Code);
        Assert.Contains("<extend type=\"list\"", Read(xml, "extensions/list.ks.xml"));
    }

    [Fact]
    public void GeneratedScriptsRun()
    {
        string app = Project();
        foreach (var args in new[]
        {
            new[] { "storage", "people" }, new[] { "cache", "api-cache", "--minutes", "5" }, new[] { "type", "Repo", "--fields", "name: string, stars: int from stargazers_count" },
            new[] { "enum", "Size", "--values", "small,medium,large" }, new[] { "const", "limits", "--values", "max_items = 10" }, new[] { "logger", "alerts", "--level", "error" },
            new[] { "module", "pricing", "--fns", "total" }, new[] { "request", "NewPerson", "--fields", "name: string, age?: int" }, new[] { "locale", "fr" }, new[] { "config", "staging" }, new[] { "job", "SendReminders", "--fields", "user_id: int", "--queue", "mail", "--tries", "5" }, new[] { "css", "brand", "--primary", "#0a7" },
        })
        {
            var (code, _, err) = Make(app, args);
            Assert.True(code == 0, string.Join(" ", args) + ": " + err);
        }
        File.WriteAllText(Path.Combine(app, "try.kscript"), """
            import "lib/people.kscript" as people
            import "lib/api-cache.kscript" as cache
            import "types/repo.kscript"
            import "types/size.kscript"
            import "lib/limits.kscript"
            import "lib/pricing.kscript"
            import "requests/new-person.kscript"
            import "lib/alerts.kscript"
            log-provider(fn: alerts, level: error)
            ann = @people.save({ name: 'Ann' })
            @people.save({ name: 'Bob' })
            @people.save($ann->set('name', 'Annie'))
            echo "{ @people.all()->map(fn($p) => $p.name)->join(',') } { @people.find(2).name }"
            @people.remove(1)
            echo "{ @people.all()->count() }"
            first = @cache.remember('x', fn() => 42)
            second = @cache.remember('x', fn() => 0)
            echo "$first $second"
            @cache.forget('x')
            echo "{ @cache.remember('x', fn() => 7) }"
            echo "{ $Repo->from({ name: 'k', stargazers_count: 3 }).stars } { $Size.large } $max_items { @total(5) }"
            echo "{ $NewPerson->from({ name: 'Sam', age: 9 }).age }"
            locale(set: fr)
            echo "{ @t('items', { count: 2 }) }"
            log(level: error) "boom"
            """);
        string tryFile = Path.Combine(app, "try.kscript");
        string output = ScriptRunner.Run(File.ReadAllText(tryFile), path: tryFile);
        Assert.StartsWith("Annie,Bob Bob\n1\n42 42\n7\n3 large 10 5\n9\n2 items\n", output);
        Assert.Contains("error boom", File.ReadAllText(Path.Combine(app, "logs", "alerts.log")));
        Assert.True(File.Exists(Path.Combine(app, "data", "people.json")));

        string css = KScript.Style.Stylesheets.Compile(Path.Combine(app, "styles", "brand.ks.css"));
        Assert.Contains("--color-primary: #0a7;", css);
    }

    [Fact]
    public void NativeTypesAreCheckedWhereverTheyAreUsed()
    {
        string app = Project();
        foreach (var args in new[]
        {
            new[] { "native", "Sku", "--pattern", "/^[A-Z]{3}-\\d{4}$/", "--normalize", "$value->trim()->upper()", "--validate", "not $value->starts_with('ZZZ'), 'ZZZ is kept for tests'" },
            new[] { "native", "Percent", "--base", "int", "--min", "0", "--max", "100" },
            new[] { "native", "Username", "--base", "handle", "--max", "16", "--not", "@admin,@root" },
            new[] { "native", "Tier", "--one-of", "gold,silver" },
            new[] { "native", "Level", "--base", "int", "--one-of", "1,2,3" },
            new[] { "native", "Note" },
        })
        {
            var (code, _, err) = Make(app, args);
            Assert.True(code == 0, string.Join(" ", args) + ": " + err);
        }
        Assert.Equal("""
            /// Percent: a native type built on int. A value that breaks a rule fails where it is made, and the editor checks the values written in code.
            type Percent with [int] {
              min: 0
              max: 100
              // Rules: normalize, pattern, min, max, one_of, not and validate. Add methods with fn.
            }

            """.Replace("\r\n", "\n"), Read(app, "types/percent.kscript"));
        Assert.Contains("  not: [@admin, @root]\n", Read(app, "types/username.kscript"));
        Assert.Contains("  one_of: ['gold', 'silver']\n", Read(app, "types/tier.kscript"));
        Assert.Contains("  one_of: [1, 2, 3]\n", Read(app, "types/level.kscript"));
        Assert.Contains("type Note with [string] {\n", Read(app, "types/note.kscript"));

        File.WriteAllText(Path.Combine(app, "try.kscript"), """
            import "types/sku.kscript"
            import "types/percent.kscript"
            import "types/username.kscript"
            import "types/tier.kscript"
            import "types/level.kscript"
            sku: Sku = abc-1234
            who: Username = @Joan
            tier: Tier = gold
            fn off(p: Percent, l: Level) => $p * $l
            echo "$sku $who $tier { @off(15, 2) }"
            try {
              bad: Sku = zzz-0001
            } catch err {
              echo $err.message
            }
            """);
        AssertNoErrors(app);
        string tryFile = Path.Combine(app, "try.kscript");
        Assert.Equal("ABC-1234 @joan gold 30\n$bad doesn't fit Sku: ZZZ is kept for tests.\n", ScriptRunner.Run(File.ReadAllText(tryFile), path: tryFile));

        File.AppendAllText(tryFile, "\nlate: Username = @root\necho $late\n");
        var check = new StringWriter();
        KScript.LanguageServer.CheckCommand.Run(new[] { app }, check);
        Assert.True(Regex.IsMatch(check.ToString(), @"try.kscript\(\d+,\d+\): error KS5012: '@root' doesn't fit Username: it is reserved \('@root'\)\."), check.ToString());
    }

    [Fact]
    public void ListAndHelpShowEachTypesOptionsAndExamples()
    {
        string app = Project();
        var (code, output, _) = Make(app, "--list");
        Assert.Equal(0, code);
        Assert.Contains("Routes and APIs\n---------------", output);
        Assert.Contains("  kscript make route <path> [--method", output);
        Assert.Contains("      --resource    Add Resource(path:", output);
        Assert.Contains("    $ kscript make controller people --resource", output);
        Assert.True(output.IndexOf("Routes and APIs", StringComparison.Ordinal) < output.IndexOf("Settings\n", StringComparison.Ordinal));

        (code, output, _) = Make(app, "request", "--help");
        Assert.Equal(0, code);
        Assert.StartsWith("kscript make request <Name> [--fields", output);
        Assert.Contains("  $ kscript make request NewPerson", output);

        // A project's own template shows where it comes from, under its own heading.
        File.WriteAllText(Path.Combine(app, "_templates", "card.html").Also(p => Directory.CreateDirectory(Path.GetDirectoryName(p)!)), "--- make\ndescription: A card\nexample: kscript make card Hello\n---\n<div></div>\n");
        (code, output, _) = Make(app, "--list");
        Assert.Contains("Your templates\n--------------\n\n  kscript make card <name>\n    A card\n    From _templates/card.html.\n    $ kscript make card Hello", output);
        (_, output, _) = Make(app);
        Assert.Contains("Scripts and data:\n  script ", output);
        Assert.Contains("Your templates:\n  card", output);
    }

    [Fact]
    public void TheDocsSiteHasTheCommandDirectory()
    {
        var site = KScript.Docs.DocsSite.Build(version: "test");
        var page = site.Find("/cli/make");
        Assert.NotNull(page);
        foreach (StubInfo t in Scaffolder.BuiltInTemplates())
        {
            Assert.Contains($"id=\"make-{t.Type}\"", page.Body);
            Assert.Contains("kscript make " + t.Type + " ", System.Net.WebUtility.HtmlDecode(page.Body));
            Assert.Contains(t.Examples[0], System.Net.WebUtility.HtmlDecode(page.Body));
        }
        Assert.Contains(page.Toc, e => e.Level == 2 && e.Text == "Routes and APIs");
        Assert.Contains(site.Search, s => s.Url == "/cli/make#make-route");
        Assert.Contains("href=\"/cli/make\"", site.Find("/cli").Body);
        Assert.Contains("Make commands", site.Render(page));
    }

    [Fact]
    public void EveryKindOfDeclarationHasAGenerator()
    {
        // fn, const, enum and type: what a script can declare and export. A new kind needs a kscript make type too.
        var builtIn = Scaffolder.BuiltInTemplates().Select(t => t.Type).ToHashSet();
        foreach (string kind in KScript.Runtime.Interpreter.Declarations)
        {
            Assert.True(Scaffolder.DeclarationGenerators.TryGetValue(kind, out var generator), $"There is no kscript make generator for '{kind}' declarations. Add a template and list it in Scaffolder.DeclarationGenerators.");
            Assert.Contains(generator, builtIn);
            string made = Read(Path.Combine(parent.Root), MakeOne(generator));
            Assert.Matches($@"(^|\n)(/// .*\n)?{kind} ", made);
        }
        // Every catalog element that declares a named, exported thing is one of them.
        var catalog = KScript.Language.Catalog.LanguageCatalog.Default;
        Assert.All(KScript.Runtime.Interpreter.Declarations, kind => Assert.NotNull(catalog.GetElement(kind)));

        // Aliases: struct and record are types, fn and function are modules of functions.
        using var site = new TempSite();
        foreach (var (alias, folder) in new[] { ("struct", "types"), ("record", "types"), ("fn", "lib"), ("function", "lib") })
            Assert.Contains($"Created {folder}/", Make(site.Root, alias, "Thing" + alias, "--force").Out);
    }

    private string MakeOne(string type)
    {
        string name = Scaffolder.Example(type);
        var result = Scaffolder.Make(new MakeRequest { Type = type, Name = name, Root = parent.Root, Force = true });
        return result.RelativePath;
    }

    [Fact]
    public void TemplateConditionsCompareValues()
    {
        var values = new Dictionary<string, string> { ["name"] = "Base", ["actions"] = "index, store" };
        Assert.Equal("yes", StubTemplate.Render("{{#if name is base}}yes{{else}}no{{/if}}", values));
        Assert.Equal("yes", StubTemplate.Render("{{#if name in default, base}}yes{{/if}}", values));
        Assert.Equal("no", StubTemplate.Render("{{#if name in a, b}}yes{{else}}no{{/if}}", values));
        Assert.Equal("yes", StubTemplate.Render("{{#if actions has store}}yes{{/if}}", values));
        Assert.Equal("", StubTemplate.Render("{{#if actions has show}}yes{{/if}}", values));
        Assert.Equal("yes", StubTemplate.Render("{{#unless missing is x}}yes{{/unless}}", values));
    }

    [Fact]
    public void SettingsFilesAreNamedForTheirEnvironment()
    {
        // A new project keeps its settings in config/ (config.root in kscript.json says so).
        string app = Project();
        Assert.Contains("already exists", Make(app, "config", "base").Err);
        Assert.Equal(0, Make(app, "config", "dev").Code);
        Assert.Contains("debug: true", Read(app, "config/config.dev.yaml"));
        // Without config.root, $config reads the script's folder, so the files go at the top.
        using var plain = new TempSite(("kscript.json", "{}"));
        Assert.Equal(0, Make(plain.Root, "config", "base").Code);
        Assert.Contains("logging:", Read(plain.Root, "config.yaml"));
        Assert.Equal(0, Make(app, "env", "base", "--keys", "api_token").Code);
        Assert.Equal(0, Make(app, "env", "staging").Code);
        Assert.Contains("\nAPI_TOKEN=\n", Read(app, ".env"));
        Assert.Contains("Secrets for staging", Read(app, ".env.staging"));
        Assert.Equal(0, Make(app, "view", "people/show", "--vars", "person").Code);
        Assert.Contains("<p>Person: {$person}</p>", Read(app, "views/people/show.html"));
    }
}

internal static class TestExtensions
{
    public static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}
