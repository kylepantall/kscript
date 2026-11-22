using KScript.Projects;

namespace KScript.Tests;

public class StandardsTests
{
    private static ProjectConfig Parse(string json) => ProjectConfig.Parse(json, Path.Combine(Path.GetTempPath(), "proj", ProjectConfig.FileName));

    private static string[] Labels(ProjectConfig c) => c.AppliedStandards.Select(s => s.Label).ToArray();

    [Fact]
    public void WithoutAStandardAProjectUsesRecommended()
    {
        var c = Parse("{}");
        Assert.Equal(new[] { Standards.Recommended }, c.Standard);
        Assert.Equal(new[] { Standards.Recommended }, Labels(c));
        Assert.Equal("warning", c.SeverityFor("KS6001"));
        Assert.Null(c.SeverityFor("KS3001")); // correctness rules keep their built-in severity
        Assert.Equal(Standards.Recommended, c.Sources["lint.rules.KS6*"]);
        Assert.Equal(80, c.Format.PrintWidth);
        Assert.Null(c.Format.IndentSize); // the editor's tab settings still apply unless a standard pins the indent
        Assert.Empty(c.Warnings);
    }

    [Fact]
    public void FoldersWithoutAProjectAlsoGetRecommended()
    {
        Assert.Equal(new[] { Standards.Recommended }, Labels(ProjectConfig.Default(Path.GetTempPath())));
        Assert.Equal("warning", ProjectConfig.Recommended.SeverityFor("KS6001"));
    }

    [Fact]
    public void StrictExtendsRecommended()
    {
        var c = Parse("""{ "standard": "kscript:strict" }""");
        Assert.Equal(new[] { Standards.Recommended, Standards.Strict }, Labels(c));
        Assert.Equal("error", c.SeverityFor("KS6001"));
        Assert.True(c.Lint.WarningsAsErrors);
        Assert.Equal(Standards.Strict, c.Sources["lint.warningsAsErrors"]);
        Assert.Equal(80, c.Format.PrintWidth); // inherited
    }

    [Fact]
    public void NoStandardTurnsStyleRulesOff()
    {
        var c = Parse("""{ "standard": [] }""");
        Assert.Empty(c.AppliedStandards);
        Assert.Empty(c.Lint.Rules);
        Assert.Null(c.SeverityFor("KS6001"));
        Assert.True(Standards.IsStyleRule("KS6001"));
        Assert.False(Standards.IsStyleRule("KS3001"));
    }

    [Fact]
    public void ProjectSettingsWinOverTheirStandard()
    {
        var c = Parse("""{ "standard": "kscript:strict", "lint": { "rules": { "KS6001": "info" }, "warningsAsErrors": false }, "format": { "printWidth": 80 } }""");
        Assert.Equal("info", c.SeverityFor("KS6001"));
        Assert.Equal("error", c.SeverityFor("KS6002"));
        Assert.False(c.Lint.WarningsAsErrors);
        Assert.Equal(80, c.Format.PrintWidth);
        Assert.Equal(ProjectConfig.FileName, c.Sources["lint.rules.KS6001"]);
        Assert.Equal(ProjectConfig.FileName, c.Sources["format.printWidth"]);
        Assert.Equal(Standards.Recommended, c.Sources["format.maxBlankLines"]);
    }

    [Fact]
    public void APrefixInALaterLayerReplacesWhatEarlierLayersSaidAboutItsCodes()
    {
        using var site = new TempSite(
            ("team.json", """{ "extends": "kscript:recommended", "lint": { "rules": { "KS6001": "error", "KS3003": "off" } } }"""),
            (ProjectConfig.FileName, """{ "standard": "./team.json", "lint": { "rules": { "KS6*": "off" } } }"""));
        var c = ProjectConfig.Load(Path.Combine(site.Root, ProjectConfig.FileName));
        Assert.Equal("off", c.SeverityFor("KS6001"));
        Assert.Equal("off", c.SeverityFor("KS3003"));
        Assert.False(c.Lint.Rules.ContainsKey("KS6001"));

        // Within one layer the longest match still wins.
        var same = Parse("""{ "standard": [], "lint": { "rules": { "KS6001": "error", "KS6*": "off" } } }""");
        Assert.Equal("error", same.SeverityFor("KS6001"));
        Assert.Equal("off", same.SeverityFor("KS6002"));
    }

    [Fact]
    public void StandardsAreFilesOrFoldersAndExtendPathsAreRelativeToTheStandard()
    {
        using var site = new TempSite(
            ("shared/base/kscript-standard.json", """{ "name": "base", "extends": "kscript:recommended", "format": { "indent": 4, "maxBlankLines": 2 } }"""),
            ("shared/acme/kscript-standard.json", """
                // comments are allowed
                { "name": "acme", "description": "Acme house style.", "extends": "../base", "lint": { "rules": { "KS6*": "error" } }, "format": { "maxBlankLines": 3, }, }
                """),
            ("app/kscript.json", """{ "standard": "../shared/acme" }"""));
        var c = ProjectConfig.Load(Path.Combine(site.Root, "app", ProjectConfig.FileName));
        Assert.Empty(c.Warnings);
        Assert.Equal(new[] { Standards.Recommended, "../shared/base/kscript-standard.json", "../shared/acme/kscript-standard.json" }, Labels(c));
        Assert.Equal(new[] { "base", "acme" }, c.AppliedStandards.Skip(1).Select(s => s.Name));
        Assert.Equal("Acme house style.", c.AppliedStandards[2].Description);
        Assert.Equal(new[] { "../base" }, c.AppliedStandards[2].Extends);
        Assert.Equal(4, c.Format.IndentSize);
        Assert.Equal(3, c.Format.MaxBlankLines);
        Assert.Equal("error", c.SeverityFor("KS6001"));
        Assert.Equal("../shared/base/kscript-standard.json", c.Sources["format.indent"]);
        Assert.Equal(2, c.StandardFiles.Count);
    }

    [Fact]
    public void SeveralStandardsApplyInOrder()
    {
        using var site = new TempSite(
            ("a.kscript-standard.json", """{ "format": { "printWidth": 90, "indent": "tab" } }"""),
            ("b.kscript-standard.json", """{ "format": { "printWidth": 100 } }"""),
            (ProjectConfig.FileName, """{ "standard": ["kscript:recommended", "a.kscript-standard.json", "b.kscript-standard.json"] }"""));
        var c = ProjectConfig.Load(Path.Combine(site.Root, ProjectConfig.FileName));
        Assert.Equal(100, c.Format.PrintWidth);
        Assert.True(c.Format.UseTabs);
        Assert.Equal("warning", c.SeverityFor("KS6001"));
    }

    [Fact]
    public void AStandardOnlyHoldsShareableSettings()
    {
        using var site = new TempSite(
            ("team.json", """{ "serve": { "port": 1 }, "lint": { "exclude": ["x"] }, "format": { "include": ["y"], "indnet": 2 } }"""),
            (ProjectConfig.FileName, """{ "standard": "team.json" }"""));
        var c = ProjectConfig.Load(Path.Combine(site.Root, ProjectConfig.FileName));
        Assert.Equal(8080, c.Serve.Port);
        Assert.Equal(LintSettings.DefaultExclude, c.Lint.Exclude);
        Assert.Equal(FormatSettings.DefaultInclude, c.Format.Include);
        Assert.All(c.Warnings, w => Assert.StartsWith("standard team.json: ", w));
        Assert.Contains(c.Warnings, w => w.Contains("'serve'"));
        Assert.Contains(c.Warnings, w => w.Contains("lint.exclude belongs in the project's kscript.json"));
        Assert.Contains(c.Warnings, w => w.Contains("format.include belongs in the project's kscript.json"));
        Assert.Contains(c.Warnings, w => w.Contains("'format.indnet'"));
    }

    [Fact]
    public void MissingUnknownAndLoopingStandardsAreErrors()
    {
        var missing = Assert.Throws<ProjectConfigException>(() => Parse("""{ "standard": "./nope.json" }"""));
        Assert.Contains("can't find the standard './nope.json'", missing.Message);
        Assert.Contains(ProjectConfig.FileName, missing.Message);

        var unknown = Assert.Throws<ProjectConfigException>(() => Parse("""{ "standard": "kscript:lax" }"""));
        Assert.Contains("kscript:recommended, kscript:strict", unknown.Message);

        using var site = new TempSite(
            ("a.json", """{ "extends": "b.json" }"""),
            ("b.json", """{ "extends": "./a.json" }"""),
            (ProjectConfig.FileName, """{ "standard": "a.json" }"""));
        var loop = Assert.Throws<ProjectConfigException>(() => ProjectConfig.Load(Path.Combine(site.Root, ProjectConfig.FileName)));
        Assert.Contains("loop: a.json -> b.json -> a.json", loop.Message);

        site.Write("bad.json", "{ nope");
        site.Write(ProjectConfig.FileName, """{ "standard": "bad.json" }""");
        Assert.Contains("not valid JSON", Assert.Throws<ProjectConfigException>(() => ProjectConfig.Load(Path.Combine(site.Root, ProjectConfig.FileName))).Message);

        Assert.Contains(Parse("""{ "standard": 3 }""").Warnings, w => w.Contains("standard should be"));
    }

    [Fact]
    public void StandardsCanBeReadFromAnEditorsOpenFiles()
    {
        string dir = Path.Combine(Path.GetTempPath(), "proj-in-memory");
        var files = new Dictionary<string, string> { [Path.Combine(dir, "team.json")] = """{ "format": { "printWidth": 70 } }""" };
        var c = ProjectConfig.Parse("""{ "standard": "team.json" }""", Path.Combine(dir, ProjectConfig.FileName), files.ContainsKey, p => files[p]);
        Assert.Equal(70, c.Format.PrintWidth);
        Assert.Equal(new[] { Path.Combine(dir, "team.json") }, c.StandardFiles);
    }

    [Fact]
    public void StandardsThatCannotBeReadAreErrors()
    {
        string dir = Path.Combine(Path.GetTempPath(), "proj-unreadable");
        string file = Path.Combine(dir, ProjectConfig.FileName);
        string team = Path.Combine(dir, "team.json");
        var locked = Assert.Throws<ProjectConfigException>(() => ProjectConfig.Parse("""{ "standard": "team.json" }""", file, p => p == team, p => throw new UnauthorizedAccessException("locked")));
        Assert.Contains("can't read the standard", locked.Message);

        var list = Assert.Throws<ProjectConfigException>(() => ProjectConfig.Parse("""{ "standard": "team.json" }""", file, p => p == team, p => "[]"));
        Assert.Contains("a standard should hold an object", list.Message);

        Assert.Contains("not a valid path", Assert.Throws<ProjectConfigException>(() => Parse("{ \"standard\": \"a\\u0000b\" }")).Message);
        Assert.Contains("can't find the standard '~/no-such-standard'", Assert.Throws<ProjectConfigException>(() => Parse("""{ "standard": "~/no-such-standard" }""")).Message);
        Assert.Contains("can't find the standard '~'", Assert.Throws<ProjectConfigException>(() => Parse("""{ "standard": "~" }""")).Message);
    }

    [Fact]
    public void AppliedStandardsSayWhereTheyCameFrom()
    {
        string dir = Path.Combine(Path.GetTempPath(), "proj-applied");
        string team = Path.Combine(dir, "team.json");
        var c = ProjectConfig.Parse("""{ "standard": "team.json" }""", Path.Combine(dir, ProjectConfig.FileName), p => p == team, p => "{}");
        Assert.True(Parse("{}").AppliedStandards.Single().IsBuiltIn);
        Assert.Null(Parse("{}").AppliedStandards.Single().Path);
        Assert.Equal(team, c.AppliedStandards.Single().Path);
        Assert.False(c.AppliedStandards.Single().IsBuiltIn);
    }

    [Fact]
    public void TheBuiltInStandardsAreValidStandards()
    {
        foreach (string name in Standards.BuiltIn.Keys)
        {
            var c = Parse($$"""{ "standard": "{{name}}" }""");
            Assert.Empty(c.Warnings);
            Assert.Equal(name, c.AppliedStandards.Last().Name);
        }
        // kscript:recommended names every format rule, so the defaults are written down in one place.
        Assert.Equal(FormatSettings.RuleNames.OrderBy(n => n), Parse("{}").Format.Rules.Keys.OrderBy(n => n));
    }

    [Fact]
    public void TheStandardSchemaMatchesTheProjectSchema()
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "KScript.sln"))) dir = Path.GetDirectoryName(dir);
        if (dir == null) return;

        string schemas = Path.Combine(dir, "editors", "vscode", "schemas");
        using var project = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(schemas, "kscript.schema.json")));
        using var standard = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(schemas, "kscript-standard.schema.json")));
        string Keys(System.Text.Json.JsonElement e) => string.Join(",", e.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
        var p = project.RootElement.GetProperty("properties");
        var s = standard.RootElement.GetProperty("properties");

        Assert.Equal("$schema,description,extends,format,lint,name", Keys(s));
        Assert.Equal("rules,warningsAsErrors", Keys(s.GetProperty("lint").GetProperty("properties")));
        Assert.Equal(
            Keys(p.GetProperty("format").GetProperty("properties")).Replace("exclude,", "").Replace("include,", ""),
            Keys(s.GetProperty("format").GetProperty("properties")));
        Assert.Equal(
            p.GetProperty("format").GetProperty("properties").GetProperty("rules").GetRawText(),
            s.GetProperty("format").GetProperty("properties").GetProperty("rules").GetRawText());

        // Both schemas list the built-in standards.
        foreach (var schema in new[] { project, standard })
        {
            var names = schema.RootElement.GetProperty("definitions").GetProperty("standardReference").GetProperty("anyOf")[0].GetProperty("enum")
                .EnumerateArray().Select(e => e.GetString());
            Assert.Equal(Standards.BuiltIn.Keys.OrderBy(n => n), names.OrderBy(n => n));
        }
    }
}
