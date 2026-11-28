using System.Globalization;

using KScript.Projects;
using KScript.Runtime;

namespace KScript.Tests;

/// <summary>$config, &lt;config&gt;, YAML text and .env. Tests in one class run one at a time, so the environment variables they set don't collide.</summary>
public sealed class ConfigTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "kscript-config-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> variables = new List<string>();

    public ConfigTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        foreach (string v in variables) Environment.SetEnvironmentVariable(v, null);
        Directory.Delete(dir, recursive: true);
    }

    private void FileOf(string name, string text)
    {
        string path = Path.Combine(dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, text);
    }

    private void Env(string name, string value)
    {
        variables.Add(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    /// <summary>A unique variable name, so tests never see each other's (or the computer's) variables.</summary>
    private static string Unique(string name) => $"KST{Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant()}_{name}";

    private string Run(string body, ConfigOptions options = null, Action<KScriptHost> setup = null)
    {
        var output = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        var host = KScriptHost.ForText(output);
        host.Config = options;
        setup?.Invoke(host);
        new Interpreter(host, options: new InterpreterOptions { Timeout = TimeSpan.FromSeconds(20) })
            .RunText("<kscript>" + body + "</kscript>", Path.Combine(dir, "script.ks.xml"));
        return output.ToString();
    }

    private KScriptException Fails(string body, ConfigOptions options = null, Action<KScriptHost> setup = null) =>
        Assert.Throws<KScriptException>(() => Run(body, options, setup));

    [Fact]
    public void Projects_read_the_config_section_of_kscript_json_and_dotenv_feeds_env()
    {
        string key = Unique("FROM_ENV");
        FileOf("kscript.json", """{ "name": "shop", "config": { "app": { "name": "Shop" }, "port": 8080 } }""");
        FileOf("config.yaml", "ignored: true");
        FileOf("config.json", """{ "ignored": true }""");
        FileOf(".env", $"{key}=ready\n");
        string output = Run($"<echo>{{@env('{key}')}} {{$env.{key}}} {{$config.app.name}} {{$config.port + 1}} {{$config.ignored ?? 'absent'}} {{$config.{key} ?? 'not in config'}}</echo>",
            setup: host => host.FileRoot = dir);
        Assert.Equal("ready ready Shop 8081 absent not in config\n", output);
        Assert.Null(Environment.GetEnvironmentVariable(key));
    }

    [Fact]
    public void Environment_files_lay_their_config_section_over_kscript_json()
    {
        FileOf("kscript.json", """{ "config": { "db": { "host": "localhost", "port": 5432 }, "level": "debug" } }""");
        FileOf("kscript.stage.json", """{ "$schema": "x", "config": { "db": { "host": "stage-db" } } }""");
        FileOf(".env", "TOKEN=base\n");
        FileOf(".env.stage", "TOKEN=stage\n");
        string output = Run("<echo>{$config->environment()} {$config.db.host}:{$config.db.port} {$config.level}</echo>" +
            "<for each=\"s\" in=\"$config->sources()\"><echo>{$s.name} {$s.kind}</echo></for>",
            new ConfigOptions { Environment = "stage", ExportEnv = false });
        Assert.Equal("stage stage-db:5432 debug\nkscript.json file\nkscript.stage.json file\n.env env-file\n.env.stage env-file\n", output);
        Assert.Equal("localhost\n", Run("<echo>{$config.db.host}</echo>", new ConfigOptions { ExportEnv = false }));

        FileOf("kscript.bad.json", """{ "config": {}, "serve": { "port": 1 } }""");
        Assert.Contains("kscript.bad.json can only hold a \"config\" section", Fails("<echo>{$config.db}</echo>", new ConfigOptions { Environment = "bad" }).Message);
        FileOf("kscript.list.json", """{ "config": [1] }""");
        Assert.Contains("The \"config\" section of kscript.list.json should hold settings by name", Fails("<echo>{$config.db}</echo>", new ConfigOptions { Environment = "list" }).Message);
        FileOf("kscript.text.json", "[]");
        Assert.Contains("kscript.text.json should hold an object", Fails("<echo>{$config.db}</echo>", new ConfigOptions { Environment = "text" }).Message);
    }

    [Fact]
    public void Config_kscript_is_laid_over_the_json_settings()
    {
        string key = Unique("SECRET");
        FileOf("kscript.json", """{ "config": { "app": { "name": "Shop", "debug": true }, "key": "none" } }""");
        FileOf("kscript.production.json", """{ "config": { "app": { "debug": false } } }""");
        FileOf(".env", $"{key}=from-env\n");
        FileOf("config.kscript", $$"""
            fn configure(environment: string) {
              return { key: @env('{{key}}'), timeout: $environment == 'production' ? 8s : 30s }
            }
            """);
        Assert.Equal("Shop false from-env duration 8s\n", Run(
            "<echo>{$config.app.name} {$config.app.debug} {$config.key} {$config.timeout->type()} {$config.timeout}</echo>",
            new ConfigOptions { Environment = "production", ExportEnv = false }));
        Assert.Equal("true 30s\n", Run("<echo>{$config.app.debug} {$config.timeout}</echo>", new ConfigOptions { ExportEnv = false }));
        Assert.True(ConfigLoader.IsConfiguredScript(new ConfigOptions { Root = dir }, Path.Combine(dir, "config.kscript")));
        Assert.False(ConfigLoader.IsConfiguredScript(new ConfigOptions { Root = dir }, Path.Combine(dir, "other.kscript")));
        Assert.False(ConfigLoader.IsConfiguredScript(new ConfigOptions(), Path.Combine(dir, "config.kscript")));
    }

    [Fact]
    public void Standalone_code_can_read_dotenv_without_accessing_config()
    {
        string key = Unique("STANDALONE");
        FileOf(".env", $"{key}=yes\n");
        Assert.Equal("yes yes\n", Run($"<echo>{{@env('{key}')}} {{$env.{key}}}</echo>"));
        Assert.Null(Environment.GetEnvironmentVariable(key));
    }

    [Fact]
    public void Config_scripts_listed_in_files_use_the_environment_and_can_read_local_files()
    {
        string key = Unique("TIMEOUT_LABEL");
        FileOf(".env", $"{key}=live\n");
        FileOf("settings/limits.json", """{ "retries": 2 }""");
        FileOf("settings/config.kscript", $$"""
            import KScript.IO
            fn configure(environment: string) {
              return {
                timeout: $environment == 'production' ? 8s : 30s
                label: @env('{{key}}')
                limits: @read_json('limits.json')
              }
            }
            """);
        Assert.Equal("duration 8s live 2\n", Run(
            "<config files=\"settings/config.kscript, .env?\" env=\"production\" />" +
            "<echo>{$config.timeout->type()} {$config.timeout} {$config.label} {$config.limits.retries}</echo>",
            setup: host => host.FileRoot = dir));
        Assert.True(ConfigLoader.IsConfiguredScript(new ConfigOptions { Root = dir, Files = new List<string> { "settings/config.kscript" } }, Path.Combine(dir, "settings", "config.staging.kscript")));
        Assert.Null(Environment.GetEnvironmentVariable(key));
    }

    [Fact]
    public void Failed_config_reload_preserves_the_previous_settings()
    {
        FileOf("kscript.json", "{}");
        FileOf("config.kscript", "fn configure(environment: string) => { value: 42 }");
        Assert.Equal("42\n", Run("""
            <import package="KScript.IO" />
            <do>@write_text('config.kscript', "fn configure(environment: string) => 'invalid'")</do>
            <try><do>$config->reload()</do><catch type="Config"><echo>{$config.value}</echo></catch></try>
            """));
    }

    [Fact]
    public void Scripts_outside_a_project_read_config_kscript_and_dotenv_from_their_folder()
    {
        FileOf("config.json", "{ \"ignored\": true }");
        FileOf("config.ks.xml", "<kscript><fn name=\"configure\" params=\"environment: string\"><return value=\"{ name: 'shop', key: @env('API_KEY') }\" /></fn></kscript>");
        FileOf(".env", "API_KEY=abc123\n");
        string output = Run("<echo>{$config.name} {$config.key} {$config.ignored ?? 'absent'}</echo>", new ConfigOptions { ExportEnv = false });
        Assert.Equal("shop abc123 absent\n", output);
    }

    [Fact]
    public void Listed_files_have_environment_versions()
    {
        FileOf("settings.json", "{ \"theme\": \"light\", \"size\": 1 }");
        FileOf("settings.staging.json", "{ \"theme\": \"dark\" }");
        Assert.Equal("dark 1\n", Run("<config files=\"settings\" env=\"staging\" export_env=\"no\" /><echo>{$config.theme} {$config.size}</echo>"));
    }

    [Fact]
    public void Prefixed_environment_variables_win_and_nest_with_double_underscores()
    {
        string prefix = Unique("APP_");
        FileOf("kscript.json", """{ "config": { "database": { "host": "localhost", "port": 5432 } } }""");
        Env(prefix + "DATABASE__HOST", "db.internal");
        Env(prefix + "DATABASE__POOL__SIZE", "20");
        Env(prefix + "FEATURE", "false");
        string output = Run("<echo>{$config.database.host} {$config.database.port} {$config.database.pool.size + 1} {$config.feature eq false}</echo>",
            new ConfigOptions { Prefix = prefix });
        Assert.Equal("db.internal 5432 21 true\n", output);
    }

    [Fact]
    public void Env_file_values_can_refer_to_variables()
    {
        string home = Unique("HOME");
        Env(home, "/srv");
        FileOf(".env", $"DATA_DIR=${{{home}}}/data\nexport GREETING='Hello ${{NOT_EXPANDED}}'\nNAME=\"Ann\\nLee\" # a comment\nPLAIN=one two # comment\nEMPTY=\nFALLBACK=${{MISSING_{home}:-guest}}\n");
        FileOf("config.kscript", "fn configure(environment: string) => { data: @env('DATA_DIR'), greeting: @env('GREETING'), name: @env('NAME'), plain: @env('PLAIN'), empty: @env('EMPTY'), fallback: @env('FALLBACK') }");
        string output = Run("<echo>{$config.data}|{$config.greeting}|{$config.name}|{$config.plain}|{$config.empty}|{$config.fallback}</echo>",
            new ConfigOptions { ExportEnv = false });
        Assert.Equal("/srv/data|Hello ${NOT_EXPANDED}|Ann\nLee|one two||guest\n", output);
    }

    [Fact]
    public void Env_file_values_become_environment_variables_unless_already_set()
    {
        string fresh = Unique("FRESH");
        string existing = Unique("EXISTING");
        Env(existing, "kept");
        variables.Add(fresh);
        FileOf(".env", $"{fresh}=from-file\n{existing}=replaced\n");
        // Reading $config loads the .env files.
        Assert.Equal("from-file kept\n", Run($"<do>$config->all()</do><echo>{{@env('{fresh}')}} {{@env('{existing}')}}</echo>"));
        Assert.Equal("from-file", Environment.GetEnvironmentVariable(fresh));
    }

    [Fact]
    public void Config_element_changes_files_defaults_and_requirements()
    {
        FileOf("settings/app.json", "{ \"name\": \"Shop\" }");
        FileOf("settings/app.production.json", "{ \"name\": \"Shop (live)\" }");
        string output = Run("<config root=\"settings\" files=\"app, secrets.json?\" env=\"production\" defaults=\"{ name: 'x', port: 80 }\" require=\"name\" />" +
            "<echo>{$config.name} {$config.port} {$config->get('port')} {$config->get('missing', 'd')} {$config->all()->count()}</echo>");
        Assert.Equal("Shop (live) 80 80 d 2\n", output);
    }

    [Fact]
    public void Config_to_keeps_its_own_settings_and_leaves_config_alone()
    {
        FileOf("kscript.json", """{ "config": { "who": "main" } }""");
        FileOf("other.json", "{ \"who\": \"other\" }");
        Assert.Equal("other main\n", Run("<config files=\"other.json\" into=\"o\" /><echo>{$o.who} {$config.who}</echo>"));
    }

    [Fact]
    public void Methods_get_has_require_all_and_reload()
    {
        FileOf("kscript.json", """{ "config": { "servers": [{ "name": "a" }, { "name": "b" }], "zero": 0 } }""");
        string output = Run("<import package=\"KScript.IO\" />" +
            "<echo>{$config->get('servers.1.name')} {$config->get('servers.5.name', 'none')} {$config->get('servers.x', 'bad')} {$config->has('zero')} {$config->has('nope')} {$config->require('zero')} {$config->all()->keys()->count()} {$config.nothing ?? 'null'}</echo>" +
            "<do>@write_text('kscript.json', '{ \"config\": { \"servers\": [] } }')</do><echo>{$config->reload()->get('servers')->count()}</echo>");
        Assert.Equal("b none bad true false 0 2 null\n0\n", output);
    }

    [Fact]
    public void Missing_settings_explain_where_to_set_them()
    {
        FileOf("kscript.json", """{ "config": { "a": 1 } }""");
        const string home = "the \"config\" section of kscript.json (or config.kscript)";
        Assert.Contains($"'db.host' is required but not set. Add it to {home} or set APP_DB__HOST", Fails("<config prefix=\"APP_\" /><echo>{$config->require('db.host')}</echo>").Message);
        Assert.Contains($"'db.host' is required but not set. Add it to {home}.", Fails("<echo>{$config->require('db.host')}</echo>").Message);
        Assert.Contains($"These settings are required but not set: db.host, key. Add them to {home}, or set APP_DB__HOST, APP_KEY.", Fails("<config prefix=\"APP_\" require=\"db.host, key, a\" />").Message);
        Assert.Contains("Add them to settings.json or more.kscript.", Fails("<config files=\"settings.json?, more.kscript?, .env?\" require=\"x\" />").Message);
        Assert.Contains("Add them to a config file.", Fails("<config files=\".env?\" require=\"x\" />").Message);
    }

    [Fact]
    public void Bad_files_and_options_are_reported()
    {
        Assert.Contains("There is no config file 'app' or 'b.json'", Fails("<config files=\"app, b.json, , c?\" />").Message);
        FileOf("app.yaml", "a: 1\n");
        Assert.Contains("There is no config file 'app'", Fails("<config files=\"app\" />").Message);
        Assert.Contains("can only use letters", Fails("<config env=\"../x\" />").Message);
        Assert.Contains("needs a map", Fails("<config defaults=\"[1]\" />").Message);
        FileOf("list.json", "[1, 2]");
        Assert.Contains("list.json should hold settings by name", Fails("<config files=\"list.json\" />").Message);
        FileOf("bad.json", "{ nope");
        Assert.Contains("bad.json: Not valid JSON", Fails("<config files=\"bad.json\" />").Message);
        FileOf("bad.env", "this is not a setting\n");
        Assert.Contains("bad.env line 1: expected NAME=value", Fails("<config files=\"bad.env\" />").Message);
    }

    [Fact]
    public void Empty_files_and_a_whole_config_as_text()
    {
        FileOf("kscript.json", "{ \"config\": { \"a\": { \"b\": 1 } } }");
        FileOf("empty.json", "null");
        Assert.Equal("{\n  \"a\": {\n    \"b\": 1\n  }\n}\n", Run("<echo>{$config}</echo>"));
        Assert.Equal("kscript.json\n", Run("<echo>{ $config->sources()->map(fn(s) => $s.name)->join(' ') }</echo>"));
        FileOf("kscript.json", "{}");
        Assert.Equal("{}\n", Run("<echo>{$config}</echo>"));
        Assert.Equal("empty.json\n", Run("<config files=\"empty.json\" /><echo>{ $config->sources()->map(fn(s) => $s.name)->join(' ') }</echo>"));
    }

    [Fact]
    public void Pages_and_other_sandboxes_read_config_from_their_folder_without_exporting()
    {
        string name = Unique("SANDBOXED");
        variables.Add(name);
        FileOf(".env", $"{name}=secret\n");
        FileOf("config.kscript", $"fn configure(environment: string) => {{ value: @env('{name}') }}");
        Assert.Equal("secret\n", Run("<echo>{$config.value}</echo>", setup: h => h.FileRoot = dir));
        Assert.Null(Environment.GetEnvironmentVariable(name));
        Assert.Contains("is outside", Fails("<config root=\"..\" />", setup: h => h.FileRoot = dir).Message);
    }

    [Fact]
    public void KSCRIPT_ENV_picks_the_environment_when_nothing_else_does()
    {
        FileOf("kscript.json", """{ "config": { "env": "base" } }""");
        FileOf("kscript.qa.json", """{ "config": { "env": "qa" } }""");
        string before = Environment.GetEnvironmentVariable("KSCRIPT_ENV");
        try
        {
            Environment.SetEnvironmentVariable("KSCRIPT_ENV", "qa");
            Assert.Equal("qa qa\n", Run("<echo>{$config.env} {$config->environment()}</echo>"));
            Assert.Equal("qa", ProjectConfig.Default(dir).ConfigOptions().Environment);
            Assert.Equal("cli", ProjectConfig.Default(dir).ConfigOptions("cli").Environment);
        }
        finally
        {
            Environment.SetEnvironmentVariable("KSCRIPT_ENV", before);
        }
        Assert.Equal("base\n", Run("<echo>{$config.env} {$config->environment()}</echo>"));
    }

    [Fact]
    public void Kscript_json_config_section_takes_any_settings()
    {
        FileOf("kscript.json", "{ \"config\": { \"environment\": \"prod\", \"files\": [\"app.yaml\"], \"anything\": { \"else\": 1 } } }");
        ProjectConfig project = ProjectConfig.Load(Path.Combine(dir, "kscript.json"));
        Assert.Empty(project.Warnings);
        ConfigOptions options = project.ConfigOptions("prod");
        Assert.Null(options.Files);
        Assert.Equal("prod", options.Environment);
        Assert.Equal(dir, options.Root);
        Assert.Equal("prod 1\n", Run("<echo>{$config.environment} {$config.anything.else}</echo>", options));
        Assert.Null(ProjectConfig.Default(dir).ConfigOptions().Root);
        Assert.Contains("config should be an object", ProjectConfig.Parse("{ \"config\": 1 }", Path.Combine(dir, "kscript.json")).Warnings[0]);
    }

    [Fact]
    public void Yaml_parses_types_and_writes_back()
    {
        string yaml = "a: 1\nb: 1.5e3\nc: 'text'\nd: \"2\"\ne: ~\nf: True\ng: no\nh: 0x1F\ni: .inf\nj: -.inf\nk: 1_000\nl: 007\nm: FALSE\nbase: &base\n  x: 1\n  y: 2\nmerged:\n  <<: *base\n  y: 3\nboth:\n  <<: [*base]\nlist:\n  - name: Ann\n    age: 3\n  - [1, 2]\n  - {}\n  - []\n";
        var value = (Dictionary<string, object>)Yaml.Parse(yaml);
        Assert.Equal(1.0, value["a"]);
        Assert.Equal(1500.0, value["b"]);
        Assert.Equal("text", value["c"]);
        Assert.Equal("2", value["d"]);
        Assert.Null(value["e"]);
        Assert.Equal(true, value["f"]);
        Assert.Equal("no", value["g"]);
        Assert.Equal(31.0, value["h"]);
        Assert.Equal(double.PositiveInfinity, value["i"]);
        Assert.Equal(double.NegativeInfinity, value["j"]);
        Assert.Equal(1000.0, value["k"]);
        Assert.Equal(7.0, value["l"]);
        Assert.Equal(false, value["m"]);
        Assert.True(double.IsNaN((double)Yaml.Parse(".nan")));
        Assert.Equal(".nan\n", Yaml.Write(double.NaN));
        Assert.Equal("\"007\"\n", Yaml.Write("007"));
        Assert.Equal(3.0, ((Dictionary<string, object>)value["merged"])["y"]);
        Assert.Equal(1.0, ((Dictionary<string, object>)value["merged"])["x"]);
        Assert.Equal(2.0, ((Dictionary<string, object>)value["both"])["y"]);

        string written = Yaml.Write(value);
        Assert.Equal(Values.ToJson(value), Values.ToJson(Yaml.Parse(written)));
        Assert.Contains("c: text\n", written);
        Assert.Contains("d: \"2\"\n", written);
        Assert.Contains("- name: Ann\n    age: 3\n", written);

        Assert.Null(Yaml.Parse(""));
        Assert.Equal(new List<object> { 1.0, 2.0 }, Yaml.Parse("--- 1\n--- 2\n"));
        Assert.Equal("{}", Yaml.Write(new Dictionary<string, object>()).Trim());
        Assert.Equal("[]", Yaml.Write(new List<object>()).Trim());
        Assert.Equal("\"\"\n", Yaml.Write(""));
        Assert.Equal("\" padded\"\n", Yaml.Write(" padded"));
        Assert.Equal("\"a: \\\"b\\\"\\\\\\n\\r\\t\"\n", Yaml.Write("a: \"b\"\\\n\r\t"));
        Assert.Equal("\"-dash\"\n", Yaml.Write("-dash"));
        Assert.Equal("\"true\": false\n\"two words\": null\n", Yaml.Write(new Dictionary<string, object> { ["true"] = false, ["two words"] = null }));
        Assert.Equal("2026-01-02\n", Yaml.Write(new DateTime(2026, 1, 2)));
        Assert.Contains("must be text", Assert.Throws<KScriptException>(() => Yaml.Parse("? [1]\n: x\n")).Message);
        Assert.Throws<KScriptException>(() => Yaml.Write(SelfNested()));
    }

    private static Dictionary<string, object> SelfNested()
    {
        var map = new Dictionary<string, object>();
        map["me"] = map;
        return map;
    }

    [Fact]
    public void Yaml_and_env_commands()
    {
        Assert.Equal("Ann 2\n", Run("<echo>{ 'name: Ann'->yaml().name } { '[1, 2]'->yaml()->count() }</echo>"));
        Assert.Equal("a: 1\nb:\n  - x\n", Run("<echo>{ { a: 1, b: ['x'] }->to_yaml() }</echo>"));
        Assert.Equal("8080 Ann\n", Run("<echo>{ @parse_env('PORT=8080').PORT } { @parse_env('N=Ann').N }</echo>"));

        FileOf("data.yaml", "items: [a, b]\n");
        FileOf("vars.env", "A=1\nB=${A}2\n");
        Assert.Equal("b 12\nx: 1\n", Run("<import package=\"KScript.IO\" /><echo>{ @read_yaml('data.yaml').items[1] } { @read_env('vars.env').B }</echo>" +
            "<do>@write_yaml('out.yaml', { x: 1 })</do><echo>{ @read_text('out.yaml') }</echo>"));
    }

    [Fact]
    public void Env_parser_reports_unclosed_quotes_and_handles_multiline_values()
    {
        Assert.Contains("the ' quote around A's value isn't closed", Assert.Throws<KScriptException>(() => DotEnv.Parse("A='x")).Message);
        Assert.Contains(".env line 2: the \" quote around B's value isn't closed", Assert.Throws<KScriptException>(() => DotEnv.Parse("\nB=\"x\ny")).Message);
        var pairs = DotEnv.Parse("# comment\r\nKEY=\"line one\nline \\\"two\\\"\"\nT=\"a\\tb\\rc\"\nC=${KEY}");
        Assert.Equal("line one\nline \"two\"", pairs[0].Value);
        Assert.Equal("a\tb\rc", pairs[1].Value);
        Assert.Equal("line one\nline \"two\"", pairs[2].Value);
        Assert.Null(DotEnv.Expand(null, null));
        Assert.Equal("", DotEnv.Expand("${X}", null));
        Assert.Equal("d", DotEnv.Expand("${X-d}", _ => ""));
    }

    [Fact]
    public void Kscript_serve_never_serves_config_files()
    {
        FileOf("config.json", "{}");
        FileOf("config.staging.json", "{}");
        FileOf("kscript.staging.json", "{}");
        FileOf("staging.yaml", "a: 1");
        FileOf("data.json", "{}");
        FileOf("prod.env", "A=1");
        var site = new KScript.Pages.PageSite(dir);
        Assert.Equal(KScript.Pages.PageRoute.RouteKind.NotFound, site.Resolve("/config.json").Kind);
        Assert.Equal(KScript.Pages.PageRoute.RouteKind.NotFound, site.Resolve("/config.staging.json").Kind);
        Assert.Equal(KScript.Pages.PageRoute.RouteKind.NotFound, site.Resolve("/kscript.staging.json").Kind);
        Assert.Equal(KScript.Pages.PageRoute.RouteKind.NotFound, site.Resolve("/staging.yaml").Kind);
        Assert.Equal(KScript.Pages.PageRoute.RouteKind.NotFound, site.Resolve("/prod.env").Kind);
        Assert.Equal(KScript.Pages.PageRoute.RouteKind.Static, site.Resolve("/data.json").Kind);
    }

    [Fact]
    public void Config_values_scalars_and_paths()
    {
        Assert.Equal(true, ConfigLoader.Scalar("true"));
        Assert.Equal(false, ConfigLoader.Scalar("false"));
        Assert.Equal(-1.5, ConfigLoader.Scalar("-1.5"));
        Assert.Equal("0123", ConfigLoader.Scalar("0123"));
        Assert.Equal("12345678901234567", ConfigLoader.Scalar("12345678901234567"));
        Assert.True(ConfigLoader.TryGet(new Dictionary<string, object>(), "", out object root));
        Assert.NotNull(root);
        Assert.False(ConfigLoader.TryGet("text", "a", out _));
        Assert.Equal("APP_DB__HOST", ConfigLoader.EnvName("APP_", "db.host"));
        Assert.Equal("config", new ConfigValue(KScriptHost.ForText(new StringWriter()), dir, null).TypeName);
        var value = new ConfigValue(KScriptHost.ForText(new StringWriter()), dir, null);
        Assert.False(value.TryCallMethod(null, "nope", out _));
    }
}
