using System.Globalization;

using KScript.Projects;
using KScript.Runtime;

namespace KScript.Tests;

/// <summary>$config, @config(), &lt;config&gt;, YAML and .env. Tests in one class run one at a time, so the environment variables they set don't collide.</summary>
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
    public void Config_reads_json_yaml_and_env_files_from_the_script_folder()
    {
        FileOf("config.json", "{ \"name\": \"shop\", \"server\": { \"port\": 8080, \"host\": \"localhost\" } }");
        FileOf("config.yaml", "server:\n  host: example.com\nfeatures: [search, cart]\n");
        FileOf(".env", "API_KEY=abc123\nDEBUG=true\n");
        string output = Run("<echo>{$config.name} {$config.server.host}:{$config.server.port} {$config.features[1]} {$config.api_key} {$config.DEBUG}</echo>",
            new ConfigOptions { ExportEnv = false });
        Assert.Equal("shop example.com:8080 cart abc123 true\n", output);
    }

    [Fact]
    public void Environment_files_layer_over_the_base_files()
    {
        FileOf("config.yaml", "db:\n  host: localhost\n  port: 5432\nlevel: debug\n");
        FileOf("config.stage.json", "{ \"level\": \"info\" }");
        FileOf("stage.yaml", "db:\n  host: stage-db\n");
        FileOf(".env", "TOKEN=base\n");
        FileOf(".env.stage", "TOKEN=stage\n");
        string output = Run("<echo>{$config->environment()} {$config.db.host}:{$config.db.port} {$config.level} {$config.token}</echo>" +
            "<for each=\"s\" in=\"$config->sources()\"><echo>{$s.name} {$s.kind}</echo></for>",
            new ConfigOptions { Environment = "stage", ExportEnv = false });
        Assert.Equal("stage stage-db:5432 info stage\nconfig.yaml file\n.env env-file\nconfig.stage.json file\n.env.stage env-file\nstage.yaml file\n", output);
    }

    [Fact]
    public void Kyles_example_names_staging_yaml_and_env_stage()
    {
        FileOf("staging.yaml", "theme: dark\n");
        FileOf(".env.stage", "REGION=eu\n");
        Assert.Equal("dark\n", Run("<config env=\"staging\" export-env=\"no\" /><echo>{$config.theme}</echo>"));
        Assert.Equal("eu\n", Run("<config env=\"stage\" export-env=\"no\" /><echo>{$config.region}</echo>"));
    }

    [Fact]
    public void Prefixed_environment_variables_win_and_nest_with_double_underscores()
    {
        string prefix = Unique("APP_");
        FileOf("config.yaml", "database:\n  host: localhost\n  port: 5432\n");
        Env(prefix + "DATABASE__HOST", "db.internal");
        Env(prefix + "DATABASE__POOL__SIZE", "20");
        Env(prefix + "FEATURE", "false");
        string output = Run("<echo>{$config.database.host} {$config.database.port} {$config.database.pool.size + 1} {$config.feature eq false}</echo>",
            new ConfigOptions { Prefix = prefix });
        Assert.Equal("db.internal 5432 21 true\n", output);
    }

    [Fact]
    public void Env_files_strip_the_prefix_and_values_can_refer_to_variables()
    {
        string prefix = Unique("APP_");
        string home = Unique("HOME");
        Env(home, "/srv");
        FileOf(".env", $"{prefix}DATA__DIR=${{{home}}}/data\nexport GREETING='Hello ${{NOT_EXPANDED}}'\nNAME=\"Ann\\nLee\" # a comment\nPLAIN=one two # comment\nEMPTY=\nFALLBACK=${{MISSING_{home}:-guest}}\n");
        FileOf("config.yaml", $"paths:\n  logs: ${{{home}}}/logs\n");
        string output = Run("<echo>{$config.data.dir}|{$config.greeting}|{$config.name}|{$config.plain}|{$config.empty}|{$config.fallback}|{$config.paths.logs}</echo>",
            new ConfigOptions { Prefix = prefix, ExportEnv = false });
        Assert.Equal("/srv/data|Hello ${NOT_EXPANDED}|Ann\nLee|one two||guest|/srv/logs\n", output);
    }

    [Fact]
    public void Env_file_values_become_environment_variables_unless_already_set()
    {
        string fresh = Unique("FRESH");
        string existing = Unique("EXISTING");
        Env(existing, "kept");
        variables.Add(fresh);
        FileOf(".env", $"{fresh}=from-file\n{existing}=replaced\n");
        Assert.Equal("from-file kept\nfrom-file replaced\n", Run($"<echo>{{$config.{fresh}}} {{@env('{existing}')}}</echo><echo>{{@env('{fresh}')}} {{$config.{existing}}}</echo>"));
        Assert.Equal("from-file", Environment.GetEnvironmentVariable(fresh));
    }

    [Fact]
    public void Config_element_changes_files_defaults_and_requirements()
    {
        FileOf("settings/app.yaml", "name: Shop\n");
        FileOf("settings/app.production.yaml", "name: Shop (live)\n");
        string output = Run("<config root=\"settings\" files=\"app, secrets.json?\" env=\"production\" defaults=\"{ name: 'x', port: 80 }\" require=\"name\" />" +
            "<echo>{$config.name} {$config.port} {@config('port')} {@config('missing', 'd')} {@config()->count()}</echo>");
        Assert.Equal("Shop (live) 80 80 d 2\n", output);
    }

    [Fact]
    public void Config_to_keeps_its_own_settings_and_leaves_config_alone()
    {
        FileOf("config.yaml", "who: main\n");
        FileOf("other.yaml", "who: other\n");
        Assert.Equal("other main\n", Run("<config files=\"other.yaml\" to=\"o\" /><echo>{$o.who} {$config.who}</echo>"));
    }

    [Fact]
    public void Methods_get_has_require_all_and_reload()
    {
        FileOf("config.yaml", "servers:\n  - name: a\n  - name: b\nzero: 0\n");
        string output = Run("<import package=\"KScript.IO\" />" +
            "<echo>{$config->get('servers.1.name')} {$config->get('servers.5.name', 'none')} {$config->get('servers.x', 'bad')} {$config->has('zero')} {$config->has('nope')} {$config->require('zero')} {$config->all()->keys()->count()} {$config.nothing ?? 'null'}</echo>" +
            "<do>@write_text('config.yaml', 'servers: []')</do><echo>{$config->reload()->get('servers')->count()}</echo>");
        Assert.Equal("b none bad true false 0 2 null\n0\n", output);
    }

    [Fact]
    public void Missing_settings_explain_where_to_set_them()
    {
        FileOf("config.yaml", "a: 1\n");
        Assert.Contains("'db.host' is required but not set. Add it to config.yaml or set APP_DB__HOST", Fails("<config prefix=\"APP_\" /><echo>{$config->require('db.host')}</echo>").Message);
        Assert.Contains("'db.host' is required but not set. Add it to config.yaml.", Fails("<echo>{$config->require('db.host')}</echo>").Message);
        Assert.Contains("These settings are required but not set: db.host, key. Add them to config.yaml, or set APP_DB__HOST, APP_KEY.", Fails("<config prefix=\"APP_\" require=\"db.host, key, a\" />").Message);
        File.Delete(Path.Combine(dir, "config.yaml"));
        Assert.Contains("Add them to config.yaml.", Fails("<config require=\"x\" />").Message);
    }

    [Fact]
    public void Bad_files_and_options_are_reported()
    {
        Assert.Contains("There is no config file 'app' or 'b.json'", Fails("<config files=\"app, b.json, , c?\" />").Message);
        Assert.Contains("can only use letters", Fails("<config env=\"../x\" />").Message);
        Assert.Contains("needs a map", Fails("<config defaults=\"[1]\" />").Message);
        FileOf("list.yaml", "- 1\n- 2\n");
        Assert.Contains("list.yaml should hold settings by name", Fails("<config files=\"list.yaml\" />").Message);
        FileOf("bad.json", "{ nope");
        Assert.Contains("bad.json: Not valid JSON", Fails("<config files=\"bad.json\" />").Message);
        FileOf("bad.yaml", "a: [1, 2\n");
        Assert.Contains("Not valid YAML (bad.yaml, line", Fails("<config files=\"bad.yaml\" />").Message);
        FileOf("bad.env", "this is not a setting\n");
        Assert.Contains("bad.env line 1: expected NAME=value", Fails("<config files=\"bad.env\" />").Message);
    }

    [Fact]
    public void Empty_files_and_a_whole_config_as_text()
    {
        FileOf("config.yaml", "");
        FileOf("config.json", "{ \"a\": { \"b\": 1 } }");
        Assert.Equal("{\n  \"a\": {\n    \"b\": 1\n  }\n}\n", Run("<echo>{$config}</echo>"));
        Assert.Equal("config.json config.yaml\n", Run("<echo>{ $config->sources()->map(fn(s) => $s.name)->join(' ') }</echo>"));
    }

    [Fact]
    public void Pages_and_other_sandboxes_read_config_from_their_folder_without_exporting()
    {
        string name = Unique("SANDBOXED");
        variables.Add(name);
        FileOf(".env", $"{name}=secret\n");
        Assert.Equal("secret\n", Run($"<echo>{{$config.{name}}}</echo>", setup: h => h.FileRoot = dir));
        Assert.Null(Environment.GetEnvironmentVariable(name));
        Assert.Contains("is outside", Fails("<config root=\"..\" />", setup: h => h.FileRoot = dir).Message);
    }

    [Fact]
    public void KSCRIPT_ENV_picks_the_environment_when_nothing_else_does()
    {
        FileOf("config.yaml", "env: base\n");
        FileOf("qa.yaml", "env: qa\n");
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
    public void Kscript_json_config_section()
    {
        FileOf("kscript.json", "{ \"config\": { \"environment\": \"prod\", \"files\": [\"app.yaml\"], \"prefix\": \"SHOP_\", \"root\": \"conf\", \"required\": [\"name\"], \"extra\": 1 } }");
        ProjectConfig project = ProjectConfig.Load(Path.Combine(dir, "kscript.json"));
        Assert.Equal("prod", project.Config.Environment);
        Assert.Contains("unknown setting 'config.extra' (expected environment, files, prefix, root, required or user).", project.Warnings);
        ConfigOptions options = project.ConfigOptions("prod");
        Assert.Equal(new[] { "app.yaml" }, options.Files);
        Assert.Equal("SHOP_", options.Prefix);
        Assert.Equal(Path.Combine(dir, "conf"), options.Root);
        Assert.Equal(new[] { "name" }, options.Required);

        FileOf("conf/app.yaml", "name: base\n");
        FileOf("conf/app.prod.yaml", "name: live\n");
        Assert.Equal("live\n", Run("<echo>{$config.name}</echo>", options));
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
        Assert.Equal("Ann 2\n", Run("<echo>{ @yaml('name: Ann').name } { @yaml('[1, 2]')->count() }</echo>"));
        Assert.Equal("a: 1\nb:\n  - x\n", Run("<echo>{ @to_yaml({ a: 1, b: ['x'] }) }</echo>"));
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
        FileOf("staging.yaml", "a: 1");
        FileOf("data.json", "{}");
        FileOf("prod.env", "A=1");
        var site = new KScript.Pages.PageSite(dir);
        Assert.Equal(KScript.Pages.PageRoute.RouteKind.NotFound, site.Resolve("/config.json").Kind);
        Assert.Equal(KScript.Pages.PageRoute.RouteKind.NotFound, site.Resolve("/config.staging.json").Kind);
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
