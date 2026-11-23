using KScript.Language.Features;
using KScript.Projects;
using KScript.Syntax.Text;

namespace KScript.Language.Tests;

public class ConfigNavigationTests
{
    [Theory]
    [InlineData("yaml", "aside: { url: elsewhere }\npasskeys:\n  'URL': private-setting\n", "URL")]
    [InlineData("json", "{\n  // café 🙂\n  \"aside\": { \"url\": false }, \"passkeys\": { \"other\": \"🙂\", \"U\\u0052L\": \"private-setting\", },\n}", "U\\u0052L")]
    public void NestedFieldsNavigateToConfiguredKeysAndHoverContainsOnlyTheirType(string extension, string contents, string writtenKey)
    {
        string dir = Path.Combine(Path.GetTempPath(), "project-settings-" + extension);
        string script = Path.Combine(dir, "src", "api.kscript");
        string config = Path.Combine(dir, "settings", "service." + extension);
        const string code = """fn url(kind: string) => "{$config.passkeys.url}/{$kind}" """;
        var files = new MemoryFiles().Add(Path.Combine(dir, ProjectConfig.FileName), """{ "config": { "root": "settings", "files": ["service", "optional.json?"] } }""")
            .Add(config, contents).Add(script, code);
        var service = new LanguageService(files: files);
        var analysis = service.Analyze(code, script);
        foreach (var (field, type, key) in new[] { ("passkeys", "map", "passkeys"), ("url", "string", writtenKey) })
        {
            int offset = code.IndexOf("." + field, StringComparison.Ordinal) + 2;
            var source = new SourceText(contents);
            var location = Assert.Single(service.GetDefinition(analysis, offset));
            Assert.Equal(config, location.Path);
            Assert.Equal(key, contents[source.GetOffset(location.Start.Line, location.Start.Character)..source.GetOffset(location.End.Line, location.End.Character)]);
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
        var files = new MemoryFiles().Add(Path.Combine(dir, ProjectConfig.FileName), """{"config":{"files":["config.kscript"]}}""")
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
        string baseFile = Path.Combine(dir, "base.yaml");
        string overrideFile = Path.Combine(dir, "overrides.yml");
        const string code = """echo "{$config.passkeys.url} {$config.passkeys.secret} {$config.unknown}" """;
        var files = new MemoryFiles().Add(Path.Combine(dir, ProjectConfig.FileName), """{"config":{"files":["base","overrides"]}}""")
            .Add(baseFile, "passkeys: { url: base, secret: true }").Add(overrideFile, "passkeys: { url: active }").Add(script, code);
        var service = new LanguageService(files: files);
        var analysis = service.Analyze(code, script);
        int url = code.IndexOf(".url", StringComparison.Ordinal) + 2;
        int secret = code.IndexOf(".secret", StringComparison.Ordinal) + 2;
        Assert.Equal(overrideFile, Assert.Single(service.GetDefinition(analysis, url)).Path);
        Assert.Equal(baseFile, Assert.Single(service.GetDefinition(analysis, secret)).Path);
        Assert.Empty(service.GetDefinition(analysis, code.IndexOf(".unknown", StringComparison.Ordinal) + 2));
        files.Add(overrideFile, "passkeys: false");
        Assert.Empty(service.GetDefinition(analysis, url));
        Assert.Empty(service.GetDefinition(analysis, secret));
    }

    [Fact]
    public void LocalConfigParameterDoesNotNavigateToTheProjectSetting()
    {
        string dir = Path.Combine(Path.GetTempPath(), "shadow-config");
        string script = Path.Combine(dir, "task.kscript");
        const string code = "fn echo_setting(config: map) => $config.passkeys.url";
        var files = new MemoryFiles().Add(Path.Combine(dir, "config.yaml"), "passkeys: { url: private-setting }").Add(script, code);
        var service = new LanguageService(files: files);
        Assert.Empty(service.GetDefinition(service.Analyze(code, script), code.IndexOf(".url", StringComparison.Ordinal) + 2));
    }
}
