using KScript.Language.Features;
using KScript.Projects;
using KScript.Syntax.Text;

namespace KScript.Language.Tests;

public class ConfigNavigationTests
{
    [Fact]
    public void NestedFieldsNavigateToKeysInKscriptJsonAndHoverContainsOnlyTheirType()
    {
        string dir = Path.Combine(Path.GetTempPath(), "project-settings-json");
        string script = Path.Combine(dir, "src", "api.kscript");
        string manifest = Path.Combine(dir, ProjectConfig.FileName);
        const string contents = "{\n  // café 🙂\n  \"name\": \"shop\",\n  \"passkeys\": { \"url\": \"not this one\" },\n  \"config\": {\n    \"aside\": { \"url\": false }, \"passkeys\": { \"other\": \"🙂\", \"U\\u0052L\": \"private-setting\", },\n  },\n}";
        const string code = """fn url(kind: string) => "{$config.passkeys.url}/{$kind}" """;
        var files = new MemoryFiles().Add(manifest, contents).Add(script, code);
        var service = new LanguageService(files: files);
        var analysis = service.Analyze(code, script);
        foreach (var (field, type, key) in new[] { ("passkeys", "map", "passkeys"), ("url", "string", "U\\u0052L") })
        {
            int offset = code.IndexOf("." + field, StringComparison.Ordinal) + 2;
            var source = new SourceText(contents);
            var location = Assert.Single(service.GetDefinition(analysis, offset));
            Assert.Equal(manifest, location.Path);
            Assert.Equal(key, contents[source.GetOffset(location.Start.Line, location.Start.Character)..source.GetOffset(location.End.Line, location.End.Character)]);
            Assert.True(location.Start.Line >= 5, "the key is in the config section, not the top-level passkeys");
            string hover = service.GetHover(analysis, offset).Markdown;
            Assert.Contains(field + ": " + type, hover);
            Assert.DoesNotContain("private-setting", hover);
        }
    }

    [Fact]
    public void ScriptConfigFieldsNavigateWithoutExecutingTheConfig()
    {
        string dir = Path.Combine(Path.GetTempPath(), "project-settings-script");
        string path = Path.Combine(dir, "main.kscript");
        string settings = Path.Combine(dir, "config.kscript");
        const string config = """
            fn configure(environment: string) {
              return { http: { timeout: $environment == 'test' ? 1s : 30s } }
            }
            """;
        const string code = "echo $config.http.timeout";
        var files = new MemoryFiles().Add(Path.Combine(dir, ProjectConfig.FileName), "{}")
            .Add(settings, config).Add(path, code);
        var service = new LanguageService(files: files);
        var analysis = service.Analyze(code, path);
        int offset = code.IndexOf(".timeout", StringComparison.Ordinal) + 2;
        var source = new SourceText(config);

        var location = Assert.Single(service.GetDefinition(analysis, offset));
        Assert.Equal(settings, location.Path);
        Assert.Equal("timeout", config[source.GetOffset(location.Start.Line, location.Start.Character)..source.GetOffset(location.End.Line, location.End.Character)]);
        Assert.Contains("timeout: duration", service.GetHover(analysis, offset).Markdown);
    }

    [Fact]
    public void LayeredSettingsUseTheLastFileThatActuallySuppliesTheKey()
    {
        string dir = Path.Combine(Path.GetTempPath(), "project-setting-overlay");
        string script = Path.Combine(dir, "task.kscript");
        string manifest = Path.Combine(dir, ProjectConfig.FileName);
        string overrides = Path.Combine(dir, "config.kscript");
        const string code = """echo "{$config.passkeys.url} {$config.passkeys.secret} {$config.unknown}" """;
        var files = new MemoryFiles().Add(manifest, """{ "config": { "passkeys": { "url": "base", "secret": true } } }""")
            .Add(overrides, "fn configure(environment: string) => { passkeys: { url: 'active' } }").Add(script, code);
        var service = new LanguageService(files: files);
        var analysis = service.Analyze(code, script);
        int url = code.IndexOf(".url", StringComparison.Ordinal) + 2;
        int secret = code.IndexOf(".secret", StringComparison.Ordinal) + 2;
        Assert.Equal(overrides, Assert.Single(service.GetDefinition(analysis, url)).Path);
        Assert.Equal(manifest, Assert.Single(service.GetDefinition(analysis, secret)).Path);
        Assert.Empty(service.GetDefinition(analysis, code.IndexOf(".unknown", StringComparison.Ordinal) + 2));
        files.Add(overrides, "fn configure(environment: string) => { passkeys: false }");
        Assert.Empty(service.GetDefinition(analysis, url));
        Assert.Empty(service.GetDefinition(analysis, secret));
    }

    [Fact]
    public void LocalConfigParameterDoesNotNavigateToTheProjectSetting()
    {
        string dir = Path.Combine(Path.GetTempPath(), "shadow-config");
        string script = Path.Combine(dir, "task.kscript");
        const string code = "fn echo_setting(config: map) => $config.passkeys.url";
        var files = new MemoryFiles().Add(Path.Combine(dir, ProjectConfig.FileName), """{ "config": { "passkeys": { "url": "private-setting" } } }""").Add(script, code);
        var service = new LanguageService(files: files);
        Assert.Empty(service.GetDefinition(service.Analyze(code, script), code.IndexOf(".url", StringComparison.Ordinal) + 2));
    }
}
