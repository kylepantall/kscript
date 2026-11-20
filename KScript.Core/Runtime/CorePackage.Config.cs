using KScript.Metadata;

using A = KScript.Metadata.AttributeInfo;
using K = KScript.Metadata.AttributeKind;

namespace KScript.Runtime
{
    public sealed partial class CorePackage
    {
        /// <summary>Settings: $config, @config(), &lt;config&gt;, and YAML and .env text.</summary>
        private static void RegisterConfig(PackageBuilder p)
        {
            p.Variable("config", KType.Config,
                "The script's settings, from config.json, config.yaml and .env (plus staging.yaml, .env.staging and so on for the environment): $config.database.host.",
                (i, m) => ConfigValue.For(i.Host, m.Directory));

            p.Command("config", "path?, default?", "A setting by path, such as @config('database.port', 5432), or every setting without a path.", KType.Any, c =>
            {
                ConfigValue config = ConfigValue.For(c.Host, c.Module?.Directory ?? c.Host.WorkingDirectory);
                if (!c.Has(0, "path")) return config.Values;
                return ConfigLoader.TryGet(config.Values, Values.ToStr(c.Arg(0, "path")), out object v) && v != null ? v : c.Arg(1, "default");
            }, "<let name=\"port\" value=\"@config('server.port', 8080)\" />");

            p.Element(new ElementInfo("config",
                "Reads the script's settings with other options: an environment, other files, a prefix for environment variables, defaults and required settings. Without `to`, it changes $config.",
                ContentKind.Empty,
                new A("env", K.Template, "The environment, such as staging or production. Its files (staging.yaml, config.staging.json, .env.staging) are layered over the base files. Default: kscript --env, or KSCRIPT_ENV."),
                new A("files", K.Text, "The base files, comma separated, such as `settings.yaml, secrets.json?, .env`. A name without an extension means .json, .yaml and .yml. End a name with ? if it may be missing. Default: config, .env."),
                new A("prefix", K.Text, "Environment variables with this prefix override the files: with prefix=\"APP_\", APP_DATABASE__HOST sets database.host."),
                new A("root", K.Path, "The folder the files are in. Default: the script's folder."),
                new A("defaults", K.Expression, "A map of settings used when no file sets them."),
                new A("require", K.Text, "Settings that must be set, comma separated, such as `database.host, api.key`."),
                new A("export-env", K.Bool, "no stops values in .env files becoming environment variables (they always do for @env() unless this is no)."),
                new A("to", K.DeclareVariable, "Store the settings in this variable instead of changing $config."))
            {
                Context = ElementContext.Script,
                Example = "<config env=\"staging\" prefix=\"APP_\" require=\"database.host\" defaults=\"{ server: { port: 8080 } }\" />\n<echo>{$config.database.host}:{$config.server.port}</echo>",
            }, run =>
            {
                ConfigValue shared = ConfigValue.For(run.Host, run.Module.Directory);
                ConfigOptions options = run.Has("to") ? (run.Host.Config?.Clone() ?? new ConfigOptions()) : shared.Options.Clone();
                if (run.Has("env")) options.Environment = run.Template("env");
                if (run.Has("files")) options.Files = List(run.Raw("files"));
                if (run.Has("prefix")) options.Prefix = run.Raw("prefix");
                if (run.Has("root")) options.Root = run.Template("root");
                if (run.Has("defaults"))
                {
                    options.Defaults = run.Eval("defaults") as Dictionary<string, object>
                        ?? throw run.Error("<config defaults> needs a map, such as defaults=\"{ server: { port: 8080 } }\".", "defaults", "Argument");
                }
                if (run.Has("require")) options.Required = List(run.Raw("require"));
                options.ExportEnv = run.Flag("export-env", options.ExportEnv);

                if (run.Has("to"))
                {
                    var own = new ConfigValue(run.Host, run.Module.Directory, options);
                    _ = own.Values;
                    run.Scope.Declare(run.VariableName("to"), own);
                }
                else
                {
                    shared.Reload(options);
                }
                return null;
            });

            p.Method(KType.Config, "get", "path, default?", "A setting by path (database.port, servers.0.name), or default when it isn't set.", KType.Any, ConfigMethod("get"));
            p.Method(KType.Config, "has", "path", "True when the setting is set.", KType.Bool, ConfigMethod("has"));
            p.Method(KType.Config, "require", "path", "A setting that must be set: fails with a clear message when it isn't.", KType.Any, ConfigMethod("require"));
            p.Method(KType.Config, "environment", "", "The environment the settings are for (staging, production), or null.", KType.String, ConfigMethod("environment"));
            p.Method(KType.Config, "all", "", "Every setting, as a map.", KType.Map, ConfigMethod("all"));
            p.Method(KType.Config, "sources", "", "The files the settings came from, in order: a list of { name, path, kind }.", KType.List, ConfigMethod("sources"));
            p.Method(KType.Config, "reload", "", "Reads the files again.", KType.Config, ConfigMethod("reload"));

            p.Command("yaml", "text", "Parses YAML text into a value.", KType.Any, c => Yaml.Parse(c.Str(0)), "@yaml('name: Ann').name");
            p.Command("to_yaml", "value", "A value as YAML text.", KType.String, c => Yaml.Write(c.Arg(0)));
            p.Command("parse_env", "text", "Parses .env text (NAME=value lines) into a map. ${NAME} uses earlier lines or the environment.", KType.Map, c =>
            {
                var map = new Dictionary<string, object>();
                foreach (var pair in DotEnv.Parse(c.Str(0), Environment.GetEnvironmentVariable)) map[pair.Key] = pair.Value;
                return map;
            }, "@parse_env('PORT=8080').PORT");
        }

        private static List<string> List(string raw) => (raw ?? string.Empty).Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

        private static MethodHandler ConfigMethod(string name) => (t, c) => ((ConfigValue)t).TryCallMethod(c, name, out object r) ? r : null;
    }
}
