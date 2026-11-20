using KScript.Pages;
using KScript.Projects;
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

        Assert.Equal("$schema,lint,name,packages,serve,test", Keys(props));
        Assert.Equal("host,port,root", Keys(props.GetProperty("serve").GetProperty("properties")));
        Assert.Equal("exclude,include,rules,warningsAsErrors", Keys(props.GetProperty("lint").GetProperty("properties")));
        Assert.Equal("exclude,include,packages,timeout", Keys(props.GetProperty("test").GetProperty("properties")));

        // Every key the schema allows is one ProjectConfig reads.
        var c = Parse("""{ "$schema": "x", "name": "n", "serve": { "root": ".", "port": 1, "host": "h" }, "lint": { "include": [], "exclude": [], "rules": {}, "warningsAsErrors": false }, "test": { "include": [], "exclude": [], "timeout": 5, "packages": ["KScript.IO"] } }""");
        Assert.Empty(c.Warnings);
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
    }
}

public class MakeProjectTests
{
    private static (int Code, string Out, string Err) Make(string parent, params string[] args)
    {
        var o = new StringWriter { NewLine = "\n" };
        var e = new StringWriter { NewLine = "\n" };
        int code = MakeCommand.Run(new[] { "project" }.Concat(args).Concat(new[] { "--dir", parent }).ToArray(), o, e);
        return (code, o.ToString(), e.ToString());
    }

    [Fact]
    public void CreatesAProjectThatServes()
    {
        using var parent = new TempSite();
        var (code, output, err) = Make(parent.Root, "My Site", "--port", "3000");
        Assert.True(code == 0, err);
        string root = Path.Combine(parent.Root, "My Site");

        foreach (var f in new[] { "kscript.json", "index.html", "about.md", "_layout.html", "_components/site-nav.html", "site.css", "scripts/hello.ks.xml", "lib/greeting.ks.xml", "tests/greeting.test.ks.xml", ".vscode/extensions.json" })
        {
            Assert.True(File.Exists(Path.Combine(root, f)), f + " is created");
            Assert.Contains(f, output);
        }
        Assert.Contains("cd \"My Site\"", output);

        var config = ProjectConfig.Load(Path.Combine(root, "kscript.json"));
        Assert.Empty(config.Warnings);
        Assert.Equal("my-site", config.Name);
        Assert.Equal(3000, config.Serve.Port);
        Assert.Equal(root, config.SiteRoot);
        Assert.Contains("kylepantall.kscript", File.ReadAllText(Path.Combine(root, ".vscode", "extensions.json")));
        Assert.Equal(KScript.Ide.IdeCommand.ExtensionId, "kylepantall.kscript");

        var site = new PageSite(config.SiteRoot);
        var home = PageRenderer.RenderFile(new PageContext(site.Root), site.Resolve("/").File);
        Assert.Equal(200, home.Status);
        Assert.Contains("<title>Home · My Site</title>", home.Body);
        Assert.Contains("<nav class=\"site-nav\">", home.Body);
        Assert.Contains("Welcome to My Site", home.Body);

        var about = PageRenderer.RenderFile(new PageContext(site.Root), site.Resolve("/about").File);
        Assert.Contains("About My Site", about.Body);

        Assert.Equal(PageRoute.RouteKind.NotFound, site.Resolve("/kscript.json").Kind);
        Assert.Equal(PageRoute.RouteKind.NotFound, site.Resolve("/.vscode/extensions.json").Kind);
        Assert.Equal(PageRoute.RouteKind.NotFound, site.Resolve("/scripts/hello.ks.xml").Kind);
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
