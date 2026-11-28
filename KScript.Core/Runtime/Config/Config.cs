using System.Globalization;
using System.Text.RegularExpressions;

using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// Where a script's settings come from. By default <c>$config</c> is the <c>config</c> section of kscript.json,
    /// with kscript.&lt;env&gt;.json's <c>config</c> section and then <c>config.kscript</c> laid over it; <c>.env</c>
    /// files feed <c>@env()</c>. A script outside a project reads <c>config.kscript</c> and <c>.env</c> from its own
    /// folder. <c>kscript --env</c> and the <c>config</c> statement change them.
    /// </summary>
    public sealed class ConfigOptions
    {
        /// <summary>The environment, such as staging or production: its files are layered over the base files. Null uses KSCRIPT_ENV.</summary>
        public string Environment { get; set; }

        /// <summary>
        /// Other files to read instead of the defaults, in order: .json files, config scripts (.kscript, .ks.xml) and
        /// .env files. A name without an extension means its .json file. A name ending in ? may be missing.
        /// </summary>
        public List<string> Files { get; set; }

        /// <summary>Environment variables starting with this prefix (APP_) override the files: APP_DATABASE__HOST sets database.host.</summary>
        public string Prefix { get; set; }

        /// <summary>The folder the files are in. Null means the script's folder.</summary>
        public string Root { get; set; }

        /// <summary>Settings used when no file sets them.</summary>
        public Dictionary<string, object> Defaults { get; set; }

        /// <summary>Settings that must be set, by path (database.host); a missing one is an error.</summary>
        public List<string> Required { get; set; } = new List<string>();

        /// <summary>Whether values from .env files become environment variables (for @env() and packages such as KScript.AI). Existing variables are kept.</summary>
        public bool ExportEnv { get; set; } = true;

        /// <summary>
        /// The app's own config folder (from KScript.Storage's folders, such as ~/.config/notes). Its config.json is
        /// layered over the project's settings, so a person's own settings win. Null: none.
        /// </summary>
        public string UserFolder { get; set; }

        public ConfigOptions Clone() => new ConfigOptions
        {
            Environment = Environment,
            Files = Files?.ToList(),
            Prefix = Prefix,
            Root = Root,
            Defaults = Defaults,
            Required = Required.ToList(),
            ExportEnv = ExportEnv,
            UserFolder = UserFolder,
        };
    }

    /// <summary>One file or source that a configuration was read from.</summary>
    public sealed class ConfigSource
    {
        public string Name { get; init; }
        public string Path { get; init; }

        /// <summary>file, env-file, environment or defaults.</summary>
        public string Kind { get; init; }
    }

    /// <summary>How a config file is read.</summary>
    internal enum ConfigLayerKind
    {
        /// <summary>A JSON file whose whole object is settings.</summary>
        Json,

        /// <summary>kscript.json or kscript.&lt;env&gt;.json: the settings are its <c>config</c> section.</summary>
        Manifest,

        /// <summary>A config script that defines configure(environment).</summary>
        Script,

        /// <summary>A .env file: NAME=value lines for @env(), not settings.</summary>
        EnvFile,
    }

    internal sealed record ConfigLayer(string Path, ConfigLayerKind Kind);

    /// <summary>Reads and layers configuration: kscript.json's config section, its environment's file, config.kscript, then prefixed environment variables.</summary>
    public static class ConfigLoader
    {
        /// <summary>The config scripts a folder can hold: config.kscript, or config.ks.xml in an XML project.</summary>
        public static readonly string[] ScriptNames = { "config.kscript", "config.ks.xml" };

        /// <summary>The settings and the sources they came from, later sources winning.</summary>
        public static (Dictionary<string, object> Values, List<ConfigSource> Sources, string Environment) Load(ConfigOptions options, KScriptHost host, string directory)
        {
            var loaded = LoadWithEnvironment(options, host, directory);
            return (loaded.Values, loaded.Sources, loaded.Environment);
        }

        internal static (Dictionary<string, object> Values, List<ConfigSource> Sources, string Environment, Dictionary<string, string> Env) LoadWithEnvironment(ConfigOptions options, KScriptHost host, string directory)
        {
            string environment = Clean(options.Environment) ?? Clean(System.Environment.GetEnvironmentVariable("KSCRIPT_ENV"));
            if (environment != null && !Regex.IsMatch(environment, @"^[A-Za-z0-9_.\-]+$"))
                throw new KScriptException($"The environment name '{environment}' can only use letters, digits, '.', '-' and '_'.", "Config");

            // A full path (the project folder) is used as it is; the files are still checked against FileRoot below.
            string root = !string.IsNullOrWhiteSpace(options.Root) && Path.IsPathRooted(options.Root)
                ? Path.GetFullPath(options.Root)
                : host.ResolvePath(string.IsNullOrWhiteSpace(options.Root) ? "." : options.Root, directory);
            var layers = ProjectLayers(options, root, environment, File.Exists, out var missing);
            if (missing.Count > 0)
                throw new KScriptException($"There is no config file {string.Join(" or ", missing.Select(m => $"'{m}'"))} in {root}. End a name with ? if it may be missing.", "Config");
            // The project's own settings files (kscript.json and the files beside it) may sit above a sandboxed site
            // folder; files a script names are checked against it.
            if (options.Files != null)
            {
                foreach (var layer in layers) host.CheckPath(layer.Path);
            }
            // The person's own settings, in the app's config folder: outside the project, so not checked against FileRoot.
            string user = string.IsNullOrWhiteSpace(options.UserFolder) ? null : Path.Combine(options.UserFolder, "config.json");
            if (user != null && File.Exists(user)) layers.Add(new ConfigLayer(user, ConfigLayerKind.Json));

            // .env files are read first, so @env() in a config script can use their values.
            var dotenv = new Dictionary<string, string>(StringComparer.Ordinal);
            string Lookup(string name) => host.EnvironmentVariable(name) ?? (dotenv.TryGetValue(name, out string v) ? v : null);
            foreach (var layer in layers.Where(l => l.Kind == ConfigLayerKind.EnvFile))
            {
                foreach (var pair in DotEnv.Parse(File.ReadAllText(layer.Path), Lookup, Path.GetFileName(layer.Path))) dotenv[pair.Key] = pair.Value;
            }
            if (options.ExportEnv && host.FileRoot == null)
            {
                foreach (var pair in dotenv)
                {
                    if (System.Environment.GetEnvironmentVariable(pair.Key) == null) System.Environment.SetEnvironmentVariable(pair.Key, pair.Value);
                }
            }

            var values = NewMap();
            var sources = new List<ConfigSource>();
            if (options.Defaults != null)
            {
                Merge(values, CopySettings(options.Defaults));
                sources.Add(new ConfigSource { Name = "defaults", Kind = "defaults" });
            }
            foreach (var layer in layers)
            {
                string fileName = Path.GetFileName(layer.Path);
                if (layer.Kind == ConfigLayerKind.EnvFile)
                {
                    sources.Add(new ConfigSource { Name = fileName, Path = layer.Path, Kind = "env-file" });
                    continue;
                }
                object parsed;
                if (layer.Kind == ConfigLayerKind.Script) parsed = ConfigEvaluation.RunScript(host, layer.Path, environment, dotenv);
                else if (layer.Kind == ConfigLayerKind.Manifest && IsProjectManifest(fileName))
                {
                    // A broken kscript.json reports itself wherever the project is read; $config just gets nothing from it.
                    try { parsed = Values.ParseJson(File.ReadAllText(layer.Path)); }
                    catch (KScriptException) { continue; }
                }
                else parsed = ParseJson(File.ReadAllText(layer.Path), fileName);
                if (layer.Kind == ConfigLayerKind.Manifest) parsed = ManifestSection(parsed, fileName);
                if (parsed == null) { sources.Add(new ConfigSource { Name = fileName, Path = layer.Path, Kind = "file" }); continue; }
                if (parsed is not Dictionary<string, object> map)
                    throw new KScriptException($"{Describe(layer, fileName)} should hold settings by name (a map), but holds {Values.Describe(parsed)}.", "Config");
                Merge(values, CopySettings(map));
                sources.Add(new ConfigSource { Name = fileName, Path = layer.Path, Kind = "file" });
            }
            if (!string.IsNullOrEmpty(options.Prefix))
            {
                var pairs = new List<KeyValuePair<string, string>>();
                foreach (System.Collections.DictionaryEntry e in host.HideEnvironment ? new System.Collections.Hashtable() : System.Environment.GetEnvironmentVariables())
                {
                    pairs.Add(new KeyValuePair<string, string>((string)e.Key, (string)e.Value));
                }
                // Only real environment variables here, not values exported from .env files.
                pairs = pairs.Where(p => !dotenv.ContainsKey(p.Key) || dotenv[p.Key] != p.Value).OrderBy(p => p.Key, StringComparer.Ordinal).ToList();
                Dictionary<string, object> fromEnvironment = FromPairs(pairs, options.Prefix);
                if (fromEnvironment.Count > 0)
                {
                    Merge(values, fromEnvironment);
                    sources.Add(new ConfigSource { Name = options.Prefix + "*", Kind = "environment" });
                }
            }

            List<string> absent = options.Required.Where(r => !TryGet(values, r, out _)).ToList();
            if (absent.Count > 0)
            {
                string hint = options.Prefix != null ? $", or set {string.Join(", ", absent.Select(a => EnvName(options.Prefix, a)))}" : string.Empty;
                throw new KScriptException($"These settings are required but not set: {string.Join(", ", absent)}. Add them to {SettingsHome(options)}{hint}.", "Config");
            }
            return (values, sources, environment, dotenv);
        }

        /// <summary>Where a missing setting should be added, for error messages.</summary>
        internal static string SettingsHome(ConfigOptions options) => options.Files == null
            ? "the \"config\" section of kscript.json (or config.kscript)"
            : string.Join(" or ", options.Files.Select(f => f.Trim().TrimEnd('?').Trim()).Where(f => f.Length > 0 && !IsEnvFile(f)).DefaultIfEmpty("a config file"));

        private static string Describe(ConfigLayer layer, string fileName) => layer.Kind switch
        {
            ConfigLayerKind.Manifest => $"The \"config\" section of {fileName}",
            ConfigLayerKind.Script => $"configure(environment) in {fileName}",
            _ => fileName,
        };

        /// <summary>The "config" section of kscript.json or kscript.&lt;env&gt;.json. An environment's file holds nothing else.</summary>
        private static object ManifestSection(object parsed, string fileName)
        {
            if (parsed is not Dictionary<string, object> manifest)
                throw new KScriptException($"{fileName} should hold an object, such as {{ \"config\": {{ \"app\": {{ \"name\": \"Shop\" }} }} }}.", "Config");
            if (!IsProjectManifest(fileName))
            {
                string other = manifest.Keys.FirstOrDefault(k => k is not ("config" or "$schema"));
                if (other != null)
                    throw new KScriptException($"{fileName} can only hold a \"config\" section, which is laid over kscript.json's; '{other}' belongs in kscript.json.", "Config");
            }
            return manifest.TryGetValue("config", out object section) ? section : null;
        }

        private static bool IsProjectManifest(string fileName) => fileName.Equals(KScript.Projects.ProjectConfig.FileName, StringComparison.OrdinalIgnoreCase);

        /// <summary>The environment's file for a project: kscript.staging.json for staging.</summary>
        public static string EnvironmentManifestName(string environment) => "kscript." + environment + ".json";

        /// <summary>The ordered sources. The editor uses these same candidates to link a setting to its key.</summary>
        internal static List<ConfigLayer> ProjectLayers(ConfigOptions options, string root, string environment,
            Func<string, bool> fileExists, out List<string> missing)
        {
            missing = new List<string>();
            var layers = new List<ConfigLayer>();
            if (options.Files == null)
            {
                // kscript.json, its environment's file, the config script, then .env and its environment's file.
                layers.Add(new ConfigLayer(Path.Combine(root, KScript.Projects.ProjectConfig.FileName), ConfigLayerKind.Manifest));
                if (environment != null) layers.Add(new ConfigLayer(Path.Combine(root, EnvironmentManifestName(environment)), ConfigLayerKind.Manifest));
                string script = ScriptNames.Select(n => Path.Combine(root, n)).FirstOrDefault(fileExists);
                if (script != null) layers.Add(new ConfigLayer(script, ConfigLayerKind.Script));
                layers.Add(new ConfigLayer(Path.Combine(root, ".env"), ConfigLayerKind.EnvFile));
                if (environment != null) layers.Add(new ConfigLayer(Path.Combine(root, ".env." + environment), ConfigLayerKind.EnvFile));
                return layers.Where(l => fileExists(l.Path)).ToList();
            }

            // The listed files, then each one's version for the environment.
            foreach (string entry in options.Files)
            {
                string name = entry.Trim();
                bool optional = name.EndsWith('?');
                name = name.TrimEnd('?').Trim();
                if (name.Length == 0) continue;
                string path = Path.Combine(root, Candidate(name));
                if (fileExists(path)) layers.Add(new ConfigLayer(path, KindOf(path)));
                else if (!optional) missing.Add(name);
            }
            if (environment != null)
            {
                foreach (string entry in options.Files)
                {
                    string name = entry.Trim().TrimEnd('?').Trim();
                    if (name.Length == 0) continue;
                    string path = Path.Combine(root, EnvironmentCandidate(Candidate(name), environment));
                    if (fileExists(path)) layers.Add(new ConfigLayer(path, KindOf(path)));
                }
            }
            return layers.Distinct().ToList();
        }

        private static ConfigLayerKind KindOf(string path)
        {
            if (IsEnvFile(path)) return ConfigLayerKind.EnvFile;
            if (IsScript(path)) return ConfigLayerKind.Script;
            string name = Path.GetFileName(path);
            bool manifest = name.Equals(KScript.Projects.ProjectConfig.FileName, StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("kscript.", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
            return manifest ? ConfigLayerKind.Manifest : ConfigLayerKind.Json;
        }

        /// <summary>The environment variable that sets a setting: APP_ + database.host is APP_DATABASE__HOST.</summary>
        public static string EnvName(string prefix, string path) => prefix + string.Join("__", path.Split('.')).ToUpperInvariant();

        private static string Clean(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private static bool IsEnvFile(string path)
        {
            string name = Path.GetFileName(path);
            return name == ".env" || name.StartsWith(".env.", StringComparison.Ordinal) || name.EndsWith(".env", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsScript(string path) =>
            path.EndsWith(".kscript", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".ks.xml", StringComparison.OrdinalIgnoreCase);

        /// <summary>True for the config script $config runs: config.kscript in the project folder, or one listed in config(files:).</summary>
        internal static bool IsConfiguredScript(ConfigOptions options, string path)
        {
            if (!IsScript(path) || options.Root == null) return false;
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            IEnumerable<string> files = options.Files?.Select(f => f.Trim().TrimEnd('?').Trim()) ?? ScriptNames;
            foreach (string file in files.Where(IsScript))
            {
                string source = Path.GetFullPath(Path.Combine(options.Root, file));
                if (path.Equals(source, comparison)) return true;
                if (options.Files == null) continue;
                string extension = source.EndsWith(".ks.xml", StringComparison.OrdinalIgnoreCase) ? ".ks.xml" : ".kscript";
                string stem = source.Substring(0, source.Length - extension.Length) + ".";
                if (path.StartsWith(stem, comparison) && path.EndsWith(extension, comparison)
                    && path.Length > stem.Length + extension.Length
                    && Regex.IsMatch(path.Substring(stem.Length, path.Length - stem.Length - extension.Length), @"^[A-Za-z0-9_.\-]+$"))
                    return true;
            }
            return false;
        }

        private static string Candidate(string name) => IsEnvFile(name) || Path.HasExtension(name) ? name : name + ".json";

        /// <summary>The environment's version of a listed file: .env.staging, settings.staging.json, config.staging.kscript.</summary>
        private static string EnvironmentCandidate(string name, string environment)
        {
            if (IsEnvFile(name)) return name + "." + environment;
            string extension = name.EndsWith(".ks.xml", StringComparison.OrdinalIgnoreCase) ? ".ks.xml" : Path.GetExtension(name);
            return name.Substring(0, name.Length - extension.Length) + "." + environment + extension;
        }

        private static object ParseJson(string text, string fileName)
        {
            try
            {
                return Values.ParseJson(text);
            }
            catch (KScriptException ex)
            {
                throw new KScriptException($"{fileName}: {ex.Message}", "Json");
            }
        }

        /// <summary>A new settings map: names are looked up without regard to case.</summary>
        internal static Dictionary<string, object> NewMap() => new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Copies settings into config maps, whose names are looked up without regard to case.</summary>
        internal static Dictionary<string, object> CopySettings(Dictionary<string, object> map) => (Dictionary<string, object>)Copied(map);

        private static object Copied(object value) => value switch
        {
            Dictionary<string, object> map => map.Aggregate(NewMap(), (m, kv) => { m[kv.Key] = Copied(kv.Value); return m; }),
            List<object> list => list.Select(Copied).ToList(),
            _ => value,
        };

        /// <summary>Environment variables as settings: the prefix is removed, __ nests (APP_DATABASE__HOST is database.host) and names are lower-cased.</summary>
        private static Dictionary<string, object> FromPairs(IEnumerable<KeyValuePair<string, string>> pairs, string prefix)
        {
            var map = NewMap();
            foreach (var pair in pairs)
            {
                if (!pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                string[] parts = pair.Key.Substring(prefix.Length).Split("__", StringSplitOptions.RemoveEmptyEntries).Select(p => p.ToLowerInvariant()).ToArray();
                if (parts.Length == 0) continue;
                Dictionary<string, object> target = map;
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    if (!(target.TryGetValue(parts[i], out object next) && next is Dictionary<string, object> nested))
                    {
                        nested = NewMap();
                        target[parts[i]] = nested;
                    }
                    target = nested;
                }
                target[parts[^1]] = Scalar(pair.Value);
            }
            return map;
        }

        private static readonly Regex Number = new Regex(@"^-?(0|[1-9][0-9]*)(\.[0-9]+)?$");

        /// <summary>Text from an environment variable or .env file: true/false and plain numbers become those types.</summary>
        public static object Scalar(string value)
        {
            if (value == "true") return true;
            if (value == "false") return false;
            if (Number.IsMatch(value) && value.Length < 16) return double.Parse(value, CultureInfo.InvariantCulture);
            return value;
        }

        /// <summary>Layers <paramref name="over"/> on <paramref name="target"/>: maps merge field by field, anything else replaces.</summary>
        internal static void Merge(Dictionary<string, object> target, Dictionary<string, object> over)
        {
            foreach (var kv in over)
            {
                if (kv.Value is Dictionary<string, object> m && target.TryGetValue(kv.Key, out object existing) && existing is Dictionary<string, object> e)
                    Merge(e, m);
                else
                    target[kv.Key] = kv.Value is Dictionary<string, object> fresh ? Copy(fresh) : kv.Value;
            }
        }

        private static Dictionary<string, object> Copy(Dictionary<string, object> map)
        {
            var copy = NewMap();
            Merge(copy, map);
            return copy;
        }

        /// <summary>A setting by path: database.host, servers.0.name.</summary>
        public static bool TryGet(object root, string path, out object value)
        {
            value = root;
            if (string.IsNullOrWhiteSpace(path)) return true;
            foreach (string part in path.Split('.'))
            {
                switch (Values.Unwrap(value))
                {
                    case Dictionary<string, object> map when map.TryGetValue(part, out object next):
                        value = next;
                        break;
                    case List<object> list when int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int index) && index < list.Count:
                        value = list[index];
                        break;
                    default:
                        value = null;
                        return false;
                }
            }
            return true;
        }
    }

    /// <summary>
    /// <c>$config</c>: the script's settings, read when first used. <c>$config.database.host</c>,
    /// <c>$config->get('database.port', 5432)</c>, <c>$config->environment()</c>.
    /// </summary>
    public sealed class ConfigValue : IKObject
    {
        internal const string HostKey = "KScript.Config";

        private readonly KScriptHost host;
        private readonly string directory;
        private ConfigOptions options;
        private Dictionary<string, object> values;
        private List<ConfigSource> sources;
        private string environment;
        private bool IsShared { get; init; }

        public ConfigValue(KScriptHost host, string directory, ConfigOptions options)
        {
            this.host = host;
            this.directory = directory;
            this.options = options ?? new ConfigOptions();
        }

        /// <summary>The shared $config of a run, made on first use from the host's options.</summary>
        public static ConfigValue For(KScriptHost host, string directory)
        {
            lock (host.Items)
            {
                if (host.Items.TryGetValue(HostKey, out object existing) && existing is ConfigValue c) return c;
                var created = new ConfigValue(host, directory, host.Config?.Clone() ?? ProjectOptions(directory)) { IsShared = true };
                host.Items[HostKey] = created;
                return created;
            }
        }

        internal static void PrepareProject(KScriptHost host, string directory)
        {
            if (host.EvaluatingConfig) return;
            ConfigValue config = For(host, directory);
            if (config.options.Root != null)
            {
                // A project's settings load before its scripts run, so @env() sees its .env files.
                config.EnsureLoaded();
            }
            else if (host.FileRoot == null && host.Config == null && host.ConfigEnvironment == null)
            {
                // A standalone script can use @env() before ever reading $config.
                var dotenv = new ConfigOptions { Files = new List<string> { ".env?" }, ExportEnv = false };
                host.ConfigEnvironment = ConfigLoader.LoadWithEnvironment(dotenv, host, directory).Env;
            }
        }

        /// <summary>A request gets the loaded settings and can reload its own copy.</summary>
        internal void CopyTo(KScriptHost target)
        {
            lock (this)
            {
                EnsureLoaded();
                target.ConfigEnvironment = host.ConfigEnvironment;
                target.Items[HostKey] = new ConfigValue(target, directory, options.Clone())
                {
                    IsShared = true,
                    values = ConfigLoader.CopySettings(values),
                    sources = sources,
                    environment = environment,
                };
            }
        }

        /// <summary>
        /// The config settings of the project around a folder, for hosts that weren't given any (routes, pages and
        /// tests): kscript.json's "config" section and the files beside it. Null outside a project.
        /// </summary>
        private static ConfigOptions ProjectOptions(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !System.IO.Directory.Exists(directory)) return null;
            try
            {
                KScript.Projects.ProjectConfig project = KScript.Projects.ProjectConfig.FindAndLoad(directory);
                if (project?.FilePath == null) return null;
                return project.ConfigOptions();
            }
            catch (KScript.Projects.ProjectConfigException)
            {
                return null;
            }
        }

        public string TypeName => KType.Config;

        public Dictionary<string, object> Values
        {
            get
            {
                EnsureLoaded();
                return values;
            }
        }

        public string Environment
        {
            get
            {
                EnsureLoaded();
                return environment;
            }
        }

        private void EnsureLoaded()
        {
            lock (this)
            {
                if (values == null) Load(options);
            }
        }

        private void Load(ConfigOptions next)
        {
            if (host.EvaluatingConfig)
                throw new KScriptException("A configuration expression or script cannot read or reload $config while it is being loaded.", "Config");
            var loaded = ConfigLoader.LoadWithEnvironment(next, host, directory);
            if (IsShared) MoneyExchangeRates.ValidateSettings(loaded.Values);
            if (IsShared) host.ConfigEnvironment = next.ExportEnv ? loaded.Env : null;
            options = next;
            (values, sources, environment) = (loaded.Values, loaded.Sources, loaded.Environment);
        }

        /// <summary>Reads the settings again with other options (the &lt;config&gt; element).</summary>
        public void Reload(ConfigOptions with)
        {
            lock (this) Load(with);
        }

        internal ConfigOptions Options => options;

        internal IEnumerable<string> FileSources => sources.Select(source => source.Path).Where(path => path != null);

        public string ToText() => Runtime.Values.ToJson(Values, indented: true);

        /// <summary>A setting that isn't there is null, so <c>$config.port ?? 8080</c> works.</summary>
        public bool TryGetMember(string name, out object value)
        {
            if (!Values.TryGetValue(name, out value)) value = null;
            return true;
        }

        public bool TryCallMethod(CallContext call, string name, out object result)
        {
            switch (name)
            {
                case "get":
                    result = ConfigLoader.TryGet(Values, call.Str(0), out object v) && v != null ? v : call.Arg(1, "default");
                    return true;
                case "has":
                    result = ConfigLoader.TryGet(Values, call.Str(0), out object h) && h != null;
                    return true;
                case "require":
                {
                    string path = call.Str(0);
                    if (!ConfigLoader.TryGet(Values, path, out object r) || r == null)
                    {
                        string where = options.Prefix != null ? $" or set {ConfigLoader.EnvName(options.Prefix, path)}" : string.Empty;
                        string source = ConfigLoader.SettingsHome(options);
                        throw new KScriptException($"The setting '{path}' is required but not set. Add it to {source}{where}.", "Config");
                    }
                    result = r;
                    return true;
                }
                case "environment":
                    result = Environment;
                    return true;
                case "all":
                    result = Values;
                    return true;
                case "sources":
                    EnsureLoaded();
                    result = sources.Select(s => (object)new Dictionary<string, object> { ["name"] = s.Name, ["path"] = s.Path, ["kind"] = s.Kind }).ToList();
                    return true;
                case "reload":
                    Reload(options);
                    result = this;
                    return true;
                default:
                    result = null;
                    return false;
            }
        }
    }
}
