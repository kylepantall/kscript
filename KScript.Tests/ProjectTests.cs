using KScript.Pages;
using KScript.Projects;
using KScript.Runtime;
using KScript.Scaffolding;

namespace KScript.Tests;

public class ProjectConfigTests
{
    private static ProjectConfig Parse(string json) => ProjectConfig.Parse(json, Path.Combine(Path.GetTempPath(), "proj", ProjectConfig.FileName));

    [Fact]
    public void DefaultsApplyWhenSectionsAreMissing()
    {
        var c = Parse("{}");
        Assert.Equal(".", c.Serve.Root);
        Assert.Equal(8080, c.Serve.Port);
        Assert.Equal("localhost", c.Serve.Host);
        Assert.Equal(LintSettings.DefaultExclude, c.Lint.Exclude);
        Assert.False(c.Lint.WarningsAsErrors);
        Assert.Empty(c.Warnings);
    }

    [Fact]
    public void ReadsEverySectionAndAllowsCommentsAndTrailingCommas()
    {
        var c = Parse("""
            {
              // a comment
              "name": "site",
              "serve": { "root": "public", "port": 3000, "host": "0.0.0.0", },
              "lint": {
                "include": ["**/*.html"],
                "exclude": ["drafts/**"],
                "rules": { "KS3003": "off", "KS4*": "error", "KS5001": "Information" },
                "warningsAsErrors": true,
              },
            }
            """);
        Assert.Empty(c.Warnings);
        Assert.Equal("site", c.Name);
        Assert.Equal(Path.Combine(c.Directory, "public"), c.SiteRoot);
        Assert.Equal(3000, c.Serve.Port);
        Assert.Equal("0.0.0.0", c.Serve.Host);
        Assert.Equal(new[] { "**/*.html" }, c.Lint.Include);
        Assert.Equal(new[] { "drafts/**" }, c.Lint.Exclude);
        Assert.True(c.Lint.WarningsAsErrors);
        Assert.Equal("off", c.SeverityFor("KS3003"));
        Assert.Equal("error", c.SeverityFor("KS4001"));
        Assert.Equal("info", c.SeverityFor("ks5001"));
        Assert.Null(c.SeverityFor("KS3001"));
    }

    [Fact]
    public void PackagesAreReadAndUnknownOnesWarn()
    {
        ProjectConfig config = Parse("{ \"packages\": [\"KScript.Terminal\", \"Math\", \"KScript.Nope\"] }");
        Assert.Equal(new[] { "KScript.Terminal", "Math", "KScript.Nope" }, config.Packages);
        Assert.Single(config.Warnings);
        Assert.Contains("KScript.Nope", config.Warnings[0]);
        Assert.Empty(Parse("{}").Packages);
    }

    [Fact]
    public void TheLongestRulePrefixWins()
    {
        var c = Parse("""{ "lint": { "rules": { "KS*": "hint", "KS3*": "warning", "KS30*": "error" } } }""");
        Assert.Equal("error", c.SeverityFor("KS3001"));
        Assert.Equal("warning", c.SeverityFor("KS3105"));
        Assert.Equal("hint", c.SeverityFor("KS5001"));
    }

    [Fact]
    public void UnknownOrBadSettingsAreWarningsNotFailures()
    {
        var c = Parse("""{ "nmae": "x", "serve": { "port": "eighty", "colour": 1 }, "lint": { "rules": { "KS3003": "loud", "E100": "off" }, "exclude": "drafts" } }""");
        Assert.Equal(8080, c.Serve.Port);
        Assert.Empty(c.Lint.Rules);
        Assert.Equal(LintSettings.DefaultExclude, c.Lint.Exclude);
        Assert.Contains(c.Warnings, w => w.Contains("'nmae'"));
        Assert.Contains(c.Warnings, w => w.Contains("serve.port"));
        Assert.Contains(c.Warnings, w => w.Contains("'serve.colour'"));
        Assert.Contains(c.Warnings, w => w.Contains("lint.rules.KS3003"));
        Assert.Contains(c.Warnings, w => w.Contains("'E100'"));
        Assert.Contains(c.Warnings, w => w.Contains("lint.exclude"));
    }

    [Fact]
    public void InvalidJsonIsAnError()
    {
        var ex = Assert.Throws<ProjectConfigException>(() => Parse("{\n  \"name\": \n}"));
        Assert.Contains("line 3", ex.Message);
        Assert.Throws<ProjectConfigException>(() => Parse("[]"));
    }

    [Theory]
    [InlineData("_templates", "_templates/page.html", true)]
    [InlineData("_templates", "blog/_templates/page.html", true)]
    [InlineData("_templates", "templates/page.html", false)]
    [InlineData("*.draft.html", "blog/post.draft.html", true)]
    [InlineData("drafts/**", "drafts/a/b.html", true)]
    [InlineData("drafts/**", "blog/drafts/b.html", false)]
    [InlineData("drafts", "drafts/b.html", true)]
    [InlineData("./drafts/", "drafts/b.html", true)]
    [InlineData("**/*.html", "index.html", true)]
    [InlineData("**/*.html", "a/b/index.html", true)]
    [InlineData("**/*.html", "a/b/index.md", false)]
    [InlineData("blog/*.md", "blog/a.md", true)]
    [InlineData("blog/*.md", "blog/x/a.md", false)]
    [InlineData("blog/**/*.md", "blog/a.md", true)]
    [InlineData("page?.html", "page1.html", true)]
    public void GlobsMatchLikeGitignore(string pattern, string path, bool matches) =>
        Assert.Equal(matches, ProjectConfig.GlobMatches(pattern, path));

    [Fact]
    public void IncludeAndExcludeAreRelativeToTheProject()
    {
        var c = Parse("""{ "lint": { "exclude": ["drafts/**"] } }""");
        Assert.True(c.IsIncluded(Path.Combine(c.Directory, "index.html")));
        Assert.True(c.IsIncluded(Path.Combine(c.Directory, "scripts", "a.ks.xml")));
        Assert.False(c.IsIncluded(Path.Combine(c.Directory, "drafts", "a.html")));
        Assert.True(c.IsExcluded(Path.Combine(c.Directory, "drafts", "a.html")));
        Assert.False(c.IsIncluded(Path.Combine(c.Directory, "site.css")));
        Assert.False(c.IsIncluded(Path.Combine(Path.GetDirectoryName(c.Directory)!, "outside.html")));
    }

    [Fact]
    public void FindLooksInParentFolders()
    {
        using var site = new TempSite((ProjectConfig.FileName, "{}"), ("blog/2026/post.md", "# Hi"));
        Assert.Equal(Path.Combine(site.Root, ProjectConfig.FileName), ProjectConfig.Find(Path.Combine(site.Root, "blog", "2026")));
        Assert.Equal(Path.Combine(site.Root, ProjectConfig.FileName), ProjectConfig.Find(Path.Combine(site.Root, "blog", "2026", "post.md")));
        Assert.Equal(site.Root, ProjectConfig.FindAndLoad(site.Root)!.Directory);
    }

    [Fact]
    public void TheSchemaListsTheSameSettings()
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "KScript.sln"))) dir = Path.GetDirectoryName(dir);
        if (dir == null) return;

        using var schema = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "editors", "vscode", "schemas", "kscript.schema.json")));
        var props = schema.RootElement.GetProperty("properties");
        string Keys(System.Text.Json.JsonElement e) => string.Join(",", e.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));

        Assert.Equal("$schema,app,format,lint,name,packages,serve,test", Keys(props));
        Assert.Equal("icon,id,main,name,version", Keys(props.GetProperty("app").GetProperty("properties")));
        Assert.Equal("host,port,root", Keys(props.GetProperty("serve").GetProperty("properties")));
        Assert.Equal("exclude,include,rules,warningsAsErrors", Keys(props.GetProperty("lint").GetProperty("properties")));
        Assert.Equal("exclude,include,packages,timeout", Keys(props.GetProperty("test").GetProperty("properties")));
        Assert.Equal("enabled,endOfLine,exclude,include,indent,maxBlankLines,preserve,printWidth,rules,tabWidth", Keys(props.GetProperty("format").GetProperty("properties")));

        // Every key the schema allows is one ProjectConfig reads.
        var c = Parse("""{ "$schema": "x", "name": "n", "serve": { "root": ".", "port": 1, "host": "h" }, "lint": { "include": [], "exclude": [], "rules": {}, "warningsAsErrors": false }, "test": { "include": [], "exclude": [], "timeout": 5, "packages": ["KScript.IO"] }, "app": { "main": "a.ks.xml", "name": "A", "id": "x.a", "version": "1", "icon": "a.png" }, "format": { "enabled": true, "rules": { "indent": true, "statementPerLine": false, "tidyTags": "on", "wrapAttributes": "off", "trimTrailingWhitespace": true, "collapseBlankLines": true, "finalNewline": true }, "indent": 4, "tabWidth": 4, "printWidth": 100, "maxBlankLines": 1, "endOfLine": "lf", "preserve": ["echo"], "include": [], "exclude": [] } }""");
        Assert.Empty(c.Warnings);
        Assert.Equal("a.ks.xml", c.App.Main);
        Assert.Equal(5, c.Test.Timeout);
        Assert.Equal(
            props.GetProperty("test").GetProperty("properties").GetProperty("include").GetProperty("default").EnumerateArray().Select(e => e.GetString()),
            TestSettings.DefaultInclude);
        Assert.Equal(
            props.GetProperty("test").GetProperty("properties").GetProperty("exclude").GetProperty("default").EnumerateArray().Select(e => e.GetString()),
            TestSettings.DefaultExclude);
        Assert.Equal(TestSettings.DefaultTimeout, props.GetProperty("test").GetProperty("properties").GetProperty("timeout").GetProperty("default").GetDouble());
        Assert.Equal(
            props.GetProperty("lint").GetProperty("properties").GetProperty("exclude").GetProperty("default").EnumerateArray().Select(e => e.GetString()),
            LintSettings.DefaultExclude);
        Assert.Equal(
            props.GetProperty("lint").GetProperty("properties").GetProperty("include").GetProperty("default").EnumerateArray().Select(e => e.GetString()),
            LintSettings.DefaultInclude);
        Assert.Equal(
            props.GetProperty("format").GetProperty("properties").GetProperty("include").GetProperty("default").EnumerateArray().Select(e => e.GetString()),
            FormatSettings.DefaultInclude);
        Assert.Equal(
            props.GetProperty("format").GetProperty("properties").GetProperty("exclude").GetProperty("default").EnumerateArray().Select(e => e.GetString()),
            FormatSettings.DefaultExclude);
        Assert.Equal(
            string.Join(",", FormatSettings.RuleNames.OrderBy(n => n, StringComparer.Ordinal)),
            Keys(props.GetProperty("format").GetProperty("properties").GetProperty("rules").GetProperty("properties")));
    }
}

public class MakeProjectTests : IDisposable
{
    // Keep the developer's own ~/.kscript/templates out of the tests.
    private readonly string userTemplates = Path.Combine(Path.GetTempPath(), "kscript-user-templates-" + Guid.NewGuid().ToString("N"));
    private readonly string previousUserTemplates = Environment.GetEnvironmentVariable("KSCRIPT_TEMPLATES");

    public MakeProjectTests() => Environment.SetEnvironmentVariable("KSCRIPT_TEMPLATES", userTemplates);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("KSCRIPT_TEMPLATES", previousUserTemplates);
        try { if (Directory.Exists(userTemplates)) Directory.Delete(userTemplates, recursive: true); } catch (IOException) { }
    }

    private static (int Code, string Out, string Err) Make(string parent, params string[] args)
    {
        var o = new StringWriter { NewLine = "\n" };
        var e = new StringWriter { NewLine = "\n" };
        int code = MakeCommand.Run(new[] { "project" }.Concat(args).Concat(new[] { "--dir", parent }).ToArray(), o, e);
        return (code, o.ToString(), e.ToString());
    }

    private static (int Code, string Out, string Err) MakeCommandRun(params string[] args)
    {
        var o = new StringWriter { NewLine = "\n" };
        var e = new StringWriter { NewLine = "\n" };
        int code = MakeCommand.Run(args, o, e);
        return (code, o.ToString(), e.ToString());
    }

    private static string Attr(string html, string name)
    {
        var m = System.Text.RegularExpressions.Regex.Match(html, name + "=\"([^\"]*)\"");
        return System.Net.WebUtility.HtmlDecode(m.Groups[1].Value);
    }

    [Fact]
    public void CreatesAProjectThatServes()
    {
        using var parent = new TempSite();
        var (code, output, err) = Make(parent.Root, "My Site", "--port", "3000");
        Assert.True(code == 0, err);
        string root = Path.Combine(parent.Root, "My Site");

        foreach (var f in new[] { "kscript.json", "index.html", "tasks.html", "about.md", "_layout.html", "_components/site-nav.html", "site.css", "scripts/hello.ks.xml", "lib/greeting.ks.xml", "lib/tasks.ks.xml", "desktop.ks.xml", "tests/greeting.test.ks.xml", "tests/tasks.test.ks.xml", ".vscode/extensions.json", ".gitignore" })
        {
            Assert.True(File.Exists(Path.Combine(root, f)), f + " is created");
            Assert.Contains(f, output);
        }
        Assert.False(File.Exists(Path.Combine(root, ProjectTemplate.ManifestName)), "the template's own settings are not copied");
        Assert.False(File.Exists(Path.Combine(root, "kscript.json.stub")));
        Assert.Contains("cd \"My Site\"", output);
        Assert.Contains("kscript serve           serve the site at http://localhost:3000", output);
        Assert.Contains("kscript desktop.ks.xml", output);

        var config = ProjectConfig.Load(Path.Combine(root, "kscript.json"));
        Assert.Empty(config.Warnings);
        Assert.Equal("my-site", config.Name);
        Assert.Equal(3000, config.Serve.Port);
        Assert.Equal(root, config.SiteRoot);
        Assert.Equal("desktop.ks.xml", config.App.Main);
        Assert.Equal("My Site", config.App.Name);
        Assert.Equal("app.kscript.my-site", config.App.Id);
        Assert.Contains("kylepantall.kscript", File.ReadAllText(Path.Combine(root, ".vscode", "extensions.json")));
        Assert.Equal(KScript.Ide.IdeCommand.ExtensionId, "kylepantall.kscript");
        Assert.StartsWith("<!--", File.ReadAllText(Path.Combine(root, "desktop.ks.xml")));

        var site = new PageSite(config.SiteRoot);
        var home = PageRenderer.RenderFile(new PageContext(site.Root), site.Resolve("/").File);
        Assert.Equal(200, home.Status);
        Assert.Contains("<title>Home · My Site</title>", home.Body);
        Assert.Contains("<nav class=\"site-nav\">", home.Body);
        Assert.Contains("Welcome to My Site", home.Body);
        Assert.Contains("kscript app build", home.Body);

        var about = PageRenderer.RenderFile(new PageContext(site.Root), site.Resolve("/about").File);
        Assert.Contains("About My Site", about.Body);

        Assert.Equal(PageRoute.RouteKind.NotFound, site.Resolve("/kscript.json").Kind);
        Assert.Equal(PageRoute.RouteKind.NotFound, site.Resolve("/.vscode/extensions.json").Kind);
        Assert.Equal(PageRoute.RouteKind.NotFound, site.Resolve("/scripts/hello.ks.xml").Kind);
        Assert.Equal(PageRoute.RouteKind.NotFound, site.Resolve("/desktop.ks.xml").Kind);
    }

    [Fact]
    public void TheTasksPageAddsTasksWithTheSharedRules()
    {
        using var parent = new TempSite();
        Assert.Equal(0, Make(parent.Root, "app").Code);
        var site = new PageSite(Path.Combine(parent.Root, "app"));
        var page = PageRenderer.RenderFile(new PageContext(site.Root), site.Resolve("/tasks").File);
        Assert.Equal(200, page.Status);
        Assert.Contains("Try the web app: kscript serve", page.Body);
        Assert.Contains("2 tasks left", page.Body);

        var json = Values.ToJson(new Dictionary<string, object>
        {
            ["page"] = "/tasks",
            ["block"] = "tasks",
            ["handler"] = 0d, // the form's on-submit
            ["state"] = Attr(page.Body, "data-ks-state"),
            ["args"] = null,
            ["binds"] = new Dictionary<string, object> { ["draft"] = "  Ship it " },
            ["event"] = new Dictionary<string, object>(),
        });
        var added = PageServer.HandleLive(site, json, new Dictionary<string, object>());
        Assert.True(added.Status == 200, added.Body);
        Assert.Contains("Ship it", added.Body);
        Assert.Contains("3 tasks left", added.Body);
    }

    [Fact]
    public void TheNewProjectsTestsPass()
    {
        using var parent = new TempSite();
        Assert.Equal(0, Make(parent.Root, "app").Code);
        var o = new StringWriter { NewLine = "\n" };
        var e = new StringWriter { NewLine = "\n" };
        int code = KScript.Testing.TestCommand.Run(new[] { Path.Combine(parent.Root, "app") }, o, e, color: false);
        Assert.True(code == 0, o.ToString() + e);
        Assert.Contains("Tests: 14 passed, 14 total", o.ToString());
    }

    [Fact]
    public void NoDesktopLeavesTheDesktopAppOut()
    {
        using var parent = new TempSite();
        // --no-desktop never takes the next word as its value, so it can come before the name.
        var (code, output, err) = Make(parent.Root, "--no-desktop", "site");
        Assert.True(code == 0, err);
        string root = Path.Combine(parent.Root, "site");
        Assert.False(File.Exists(Path.Combine(root, "desktop.ks.xml")));
        Assert.DoesNotContain("desktop", output);
        var config = ProjectConfig.Load(Path.Combine(root, "kscript.json"));
        Assert.Empty(config.Warnings);
        Assert.Null(config.App.Main);
        Assert.DoesNotContain("desktop.ks.xml", File.ReadAllText(Path.Combine(root, "tasks.html")));
        Assert.DoesNotContain("app build", File.ReadAllText(Path.Combine(root, "index.html")));
    }

    [Fact]
    public void AMistypedOptionIsPointedOut()
    {
        using var parent = new TempSite();
        var (code, output, _) = Make(parent.Root, "site", "--no-dekstop");
        Assert.Equal(0, code);
        Assert.Contains("no 'dekstop' option", output);
        Assert.True(File.Exists(Path.Combine(parent.Root, "site", "desktop.ks.xml")));
    }

    [Fact]
    public void HelpListsTheTemplatesOptions()
    {
        using var parent = new TempSite();
        var (code, output, _) = Make(parent.Root, "--help");
        Assert.Equal(0, code);
        Assert.Contains("Template: built in", output);
        Assert.Contains("--no-desktop", output);
        Assert.Contains("--port", output);
        Assert.Contains("(default 8080)", output);
    }

    [Fact]
    public void TheProjectTemplateCanBeCopiedAndChangedForEveryNewProject()
    {
        using var parent = new TempSite();
        string previous = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(parent.Root); // not inside a project: the template goes to the user's folder
            var (code, output, err) = MakeCommandRun("templates", "project");
            Assert.True(code == 0, err);
            Assert.Contains("Copied the project template", output);
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
        }

        string folder = Path.Combine(userTemplates, "project");
        Assert.True(File.Exists(Path.Combine(folder, ProjectTemplate.ManifestName)));
        Assert.True(File.Exists(Path.Combine(folder, "kscript.json.stub")), "kscript.json is kept as .stub so tools don't read it as a project");
        Assert.True(File.Exists(Path.Combine(folder, ".vscode", "extensions.json")));
        Assert.True(File.Exists(Path.Combine(folder, "desktop.ks.xml")));

        // Change a file, add one that is only made with --api, and drop the About page.
        File.WriteAllText(Path.Combine(folder, "index.html"), "<h1>{{ title }}, our way</h1>\n");
        File.WriteAllText(Path.Combine(folder, "api.md"), "--- make\nwhen: api\nto: docs/{{ kebab }}-api.md\n---\n# {{ title }} API\n");
        File.Delete(Path.Combine(folder, "about.md"));

        var (c1, out1, err1) = Make(parent.Root, "Shop");
        Assert.True(c1 == 0, err1);
        Assert.Contains("from the template in", out1);
        string shop = Path.Combine(parent.Root, "Shop");
        Assert.Equal("<h1>Shop, our way</h1>\n", File.ReadAllText(Path.Combine(shop, "index.html")));
        Assert.False(File.Exists(Path.Combine(shop, "about.md")));
        Assert.False(File.Exists(Path.Combine(shop, "docs", "shop-api.md")));
        Assert.True(File.Exists(Path.Combine(shop, "kscript.json")));

        Assert.Equal(0, Make(parent.Root, "Store", "--api").Code);
        Assert.Equal("# Store API\n", File.ReadAllText(Path.Combine(parent.Root, "Store", "docs", "store-api.md")));

        // Copying again keeps the changes unless --force.
        string again = Path.Combine(parent.Root, "again");
        Directory.CreateDirectory(again);
        previous = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(again);
            var (_, output, _) = MakeCommandRun("templates", "project", "--global");
            Assert.Contains("about.md", output);
            Assert.DoesNotContain("index.html", output);
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
        }
        Assert.Equal("<h1>{{ title }}, our way</h1>\n", File.ReadAllText(Path.Combine(folder, "index.html")));
    }

    [Fact]
    public void AProjectsOwnTemplateAndTemplateFoldersWin()
    {
        // _templates/project in a project is used for projects made inside it (a monorepo of apps).
        using var mono = new TempSite((ProjectConfig.FileName, "{ \"name\": \"mono\" }"));
        var (code, output, err) = MakeCommandRun("templates", "project", "--dir", mono.Root);
        Assert.True(code == 0, err);
        string own = Path.Combine(mono.Root, "_templates", "project");
        Assert.True(File.Exists(Path.Combine(own, "tasks.html")));
        File.WriteAllText(Path.Combine(own, "tasks.html"), "<p>mono tasks</p>\n");

        Assert.Equal(0, Make(Path.Combine(mono.Root), "apps/one").Code);
        Assert.Equal("<p>mono tasks</p>\n", File.ReadAllText(Path.Combine(mono.Root, "apps", "one", "tasks.html")));

        // --template names any folder.
        using var other = new TempSite(("starter/README.md", "# {{ title }}\n"), ("starter/kscript.json.stub", "{ \"name\": \"{{ kebab }}\" }\n"));
        var (c2, out2, err2) = Make(other.Root, "Mini App", "--template", Path.Combine(other.Root, "starter"));
        Assert.True(c2 == 0, err2);
        Assert.Equal("# Mini App\n", File.ReadAllText(Path.Combine(other.Root, "Mini App", "README.md")));
        Assert.Equal("mini-app", ProjectConfig.Load(Path.Combine(other.Root, "Mini App", "kscript.json")).Name);
        Assert.Contains("Next:", out2);

        Assert.Contains("no template folder", Make(other.Root, "x", "--template", Path.Combine(other.Root, "nope")).Err);
    }

    [Fact]
    public void DotMeansTheCurrentFolderAndExistingFilesAreKept()
    {
        using var parent = new TempSite(("blog/index.html", "<h1>Mine</h1>"));
        var (code, output, err) = Make(Path.Combine(parent.Root, "blog"), ".");
        Assert.Equal(1, code);
        Assert.Contains("already has index.html", err);
        Assert.Equal("<h1>Mine</h1>", File.ReadAllText(Path.Combine(parent.Root, "blog", "index.html")));
        Assert.False(File.Exists(Path.Combine(parent.Root, "blog", "kscript.json")));

        (code, output, err) = Make(Path.Combine(parent.Root, "blog"), ".", "--force");
        Assert.True(code == 0, err);
        Assert.Contains("this folder", output);
        Assert.Equal("blog", ProjectConfig.Load(Path.Combine(parent.Root, "blog", "kscript.json")).Name);
    }

    [Fact]
    public void DryRunWritesNothing()
    {
        using var parent = new TempSite();
        var (code, output, _) = Make(parent.Root, "shop", "--dry-run");
        Assert.Equal(0, code);
        Assert.Contains("Would create", output);
        Assert.Contains("\"name\": \"shop\"", output);
        Assert.False(Directory.Exists(Path.Combine(parent.Root, "shop")));
    }

    [Fact]
    public void BadInputIsExplained()
    {
        using var parent = new TempSite();
        Assert.Contains("folder name", Make(parent.Root).Err);
        Assert.Contains("--port", Make(parent.Root, "x", "--port", "http").Err);
        Assert.Contains("no letters or digits", Make(parent.Root, "!!!").Err);
    }

    [Fact]
    public void OtherMakeTypesUseTheProjectRootFromAnywhereInside()
    {
        using var parent = new TempSite();
        Assert.Equal(0, Make(parent.Root, "site").Code);
        string root = Path.Combine(parent.Root, "site");
        Directory.CreateDirectory(Path.Combine(root, "blog"));

        string previous = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(Path.Combine(root, "blog"));
            var o = new StringWriter { NewLine = "\n" };
            var e = new StringWriter { NewLine = "\n" };
            Assert.Equal(0, MakeCommand.Run(new[] { "component", "Hero" }, o, e));
            Assert.True(File.Exists(Path.Combine(root, "_components", "hero.html")), o.ToString() + e);
            Assert.Contains("Created ../_components/hero.html", o.ToString());
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
        }
    }

    [Fact]
    public void TheListOfTypesMentionsProject()
    {
        using var parent = new TempSite();
        var o = new StringWriter();
        MakeCommand.Run(new[] { "--dir", parent.Root }, o, new StringWriter());
        Assert.Contains("kscript make project", o.ToString());
    }
}
