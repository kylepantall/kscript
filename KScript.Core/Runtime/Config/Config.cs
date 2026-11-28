using System.Globalization;
using System.Text.RegularExpressions;

using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// Where a script's settings come from. The defaults read <c>config.json</c>, <c>config.yaml</c> (or
    /// <c>config.yml</c>) and <c>.env</c> from the script's folder; kscript.json's <c>config</c> section,
    /// <c>kscript --env</c> and the <c>&lt;config&gt;</c> element change them.
    /// </summary>
    public sealed class ConfigOptions
    {
        /// <summary>The environment, such as staging or production: its files are layered over the base files. Null uses KSCRIPT_ENV.</summary>
        public string Environment { get; set; }

        /// <summary>
        /// The base files, in order. A name without an extension means its .json, .yaml and .yml files.
        /// A name ending in ? may be missing; the default names may always be missing.
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
        /// The app's own config folder (from KScript.Storage's folders, such as ~/.config/notes). Its config.json,
        /// config.yaml or config.yml is layered over the project's files, so a person's own settings win. Null: none.
        /// </summary>
        public string UserFolder { get; set; }

        public static readonly string[] DefaultFiles = { "config", ".env" };
        public static readonly IReadOnlyList<string> ProjectDefaultFiles = new[] { ".env" };
        internal bool ExplicitProjectFiles { get; init; }

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
            ExplicitProjectFiles = ExplicitProjectFiles,
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

    /// <summary>Reads and layers configuration files: base files, then the environment's files, then prefixed environment variables.</summary>
    public static class ConfigLoader
    {
        private static readonly string[] Extensions = { ".json", ".yaml", ".yml" };

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

            // A full path (from kscript.json's config.root) is used as it is; the files are still checked against FileRoot below.
            string root = !string.IsNullOrWhiteSpace(options.Root) && Path.IsPathRooted(options.Root)
                ? Path.GetFullPath(options.Root)
                : host.ResolvePath(string.IsNullOrWhiteSpace(options.Root) ? "." : options.Root, directory);
            var layers = ProjectLayers(options, root, environment, File.Exists, out var missing);
            // The person's own settings, in the app's config folder: outside the project, so not checked against FileRoot.
            var user = string.IsNullOrWhiteSpace(options.UserFolder)
                ? new List<string>()
                : Extensions.Select(e => Path.Combine(options.UserFolder, "config" + e)).Where(File.Exists).ToList();
            if (missing.Count > 0)
                throw new KScriptException($"There is no config file {string.Join(" or ", missing.Select(m => $"'{m}'"))} in {root}. End a name with ? if it may be missing.", "Config");

            foreach (var layer in layers) host.CheckPath(layer.Path);
            layers.AddRange(user.Select(f => (f, false)));

            // .env files are read first, so ${NAME} in any file can use their values.
            var dotenv = new Dictionary<string, string>(StringComparer.Ordinal);
            var envValues = new Dictionary<string, List<KeyValuePair<string, string>>>();
            string Lookup(string name) => host.EnvironmentVariable(name) ?? (dotenv.TryGetValue(name, out string v) ? v : null);
            foreach (var layer in layers.Where(l => l.EnvFile))
            {
                var pairs = DotEnv.Parse(KScript.Runtime.Dependencies.DependencyTrace.ReadText(layer.Path), Lookup, Path.GetFileName(layer.Path));
                envValues[layer.Path] = pairs;
                foreach (var pair in pairs) dotenv[pair.Key] = pair.Value;
            }
            bool export = options.ExportEnv && host.FileRoot == null;
            if (export)
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
                Merge(values, ToConfig(options.Defaults, null));
                sources.Add(new ConfigSource { Name = "defaults", Kind = "defaults" });
            }
            foreach (var layer in layers)
            {
                string fileName = Path.GetFileName(layer.Path);
                if (layer.EnvFile)
                {
                    Merge(values, FromPairs(envValues[layer.Path], options.Prefix, requirePrefix: false));
                    sources.Add(new ConfigSource { Name = fileName, Path = layer.Path, Kind = "env-file" });
                    continue;
                }
                bool script = IsScript(layer.Path);
                bool json = layer.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
                bool expressions = !script && !json && options.Files != null && !user.Contains(layer.Path);
                string text = script ? null : KScript.Runtime.Dependencies.DependencyTrace.ReadText(layer.Path);
                object parsed = script ? ConfigEvaluation.RunScript(host, layer.Path, environment, dotenv)
                    : json ? ParseJson(text, fileName)
                    : expressions ? ConfigEvaluation.ReadYaml(host, layer.Path, text, environment, dotenv, Lookup)
                    : Yaml.Parse(text, fileName);
                if (parsed == null) { sources.Add(new ConfigSource { Name = fileName, Path = layer.Path, Kind = "file" }); continue; }
                if (parsed is not Dictionary<string, object> map)
                    throw new KScriptException($"{fileName} should hold settings by name (a map), but holds {Values.Describe(parsed)}.", "Config");
                Merge(values, ToConfig(map, script || expressions ? null : Lookup));
                sources.Add(new ConfigSource { Name = fileName, Path = layer.Path, Kind = "file" });
            }
            if (!string.IsNullOrEmpty(options.Prefix))
            {
                var pairs = new List<KeyValuePair<string, string>>();
                foreach (System.Collections.DictionaryEntry e in host.HideEnvironment ? new System.Collections.Hashtable() : System.Environment.GetEnvironmentVariables())
                {
                    pairs.Add(new KeyValuePair<string, string>((string)e.Key, (string)e.Value));
                }
                // Only real environment variables here: values exported from .env files were merged above.
                pairs = pairs.Where(p => !dotenv.ContainsKey(p.Key) || dotenv[p.Key] != p.Value).OrderBy(p => p.Key, StringComparer.Ordinal).ToList();
                Dictionary<string, object> fromEnvironment = FromPairs(pairs, options.Prefix, requirePrefix: true);
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
                string source = string.Join(" or ", sources.Where(s => s.Kind is "file" or "env-file").Select(s => s.Name));
                if (source.Length == 0) source = options.ExplicitProjectFiles ? "a file listed in kscript.json under config.files" : "config.yaml";
                throw new KScriptException($"These settings are required but not set: {string.Join(", ", absent)}. Add them to {source}{hint}.", "Config");
            }
            return (values, sources, environment, dotenv);
        }

        /// <summary>The ordered project sources. The editor uses these same candidates to link a setting to its key.</summary>
        internal static List<(string Path, bool EnvFile)> ProjectLayers(ConfigOptions options, string root, string environment,
            Func<string, bool> fileExists, out List<string> missing)
        {
            bool defaults = options.Files == null;
            IReadOnlyList<string> names = options.Files ?? (options.ExplicitProjectFiles ? ConfigOptions.ProjectDefaultFiles : ConfigOptions.DefaultFiles);

            // Every layer, in the order it applies: the base files, then the environment's files.
            var layers = new List<(string Path, bool EnvFile)>();
            missing = new List<string>();
            foreach (string entry in names)
            {
                string name = entry.Trim();
                bool optional = defaults || name.EndsWith('?');
                name = name.TrimEnd('?').Trim();
                if (name.Length == 0) continue;
                List<string> found = Candidates(name).Select(c => Path.Combine(root, c)).Where(fileExists).ToList();
                if (found.Count == 0 && !optional) missing.Add(name);
                layers.AddRange(found.Select(f => (f, IsEnvFile(f))));
            }
            if (environment != null)
            {
                foreach (string entry in names)
                {
                    string name = entry.Trim().TrimEnd('?').Trim();
                    if (name.Length == 0) continue;
                    layers.AddRange(EnvironmentCandidates(name, environment).Select(c => Path.Combine(root, c)).Where(fileExists).Select(f => (f, IsEnvFile(f))));
                }
                // staging.yaml / staging.json: a file named after the environment.
                if (!options.ExplicitProjectFiles)
                    layers.AddRange(Extensions.Select(e => Path.Combine(root, environment + e)).Where(fileExists).Select(f => (f, false)));
            }
            return layers.Distinct().ToList();
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

        internal static bool IsConfiguredScript(ConfigOptions options, string path)
        {
            if (!IsScript(path) || options.Files == null) return false;
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            foreach (string entry in options.Files)
            {
                string file = entry.Trim().TrimEnd('?').Trim();
                if (!IsScript(file)) continue;
                string source = Path.GetFullPath(Path.Combine(options.Root, file));
                if (path.Equals(source, comparison)) return true;
                string extension = source.EndsWith(".ks.xml", StringComparison.OrdinalIgnoreCase) ? ".ks.xml" : ".kscript";
                string stem = source.Substring(0, source.Length - extension.Length) + ".";
                if (path.StartsWith(stem, comparison) && path.EndsWith(extension, comparison)
                    && path.Length > stem.Length + extension.Length
                    && Regex.IsMatch(path.Substring(stem.Length, path.Length - stem.Length - extension.Length), @"^[A-Za-z0-9_.\-]+$"))
                    return true;
            }
            return false;
        }

        private static IEnumerable<string> Candidates(string name) =>
            IsEnvFile(name) || Path.HasExtension(name) ? new[] { name } : Extensions.Select(e => name + e);

        /// <summary>The environment's version of a base file: .env.staging, config.staging.json, settings.staging.yaml.</summary>
        private static IEnumerable<string> EnvironmentCandidates(string name, string environment)
        {
            if (name == ".env" || name.EndsWith("/.env", StringComparison.Ordinal)) return new[] { name + "." + environment };
            if (Path.HasExtension(name) && !IsEnvFile(name))
            {
                string extension = name.EndsWith(".ks.xml", StringComparison.OrdinalIgnoreCase) ? ".ks.xml" : Path.GetExtension(name);
                return new[] { name.Substring(0, name.Length - extension.Length) + "." + environment + extension };
            }
            if (IsEnvFile(name)) return new[] { name + "." + environment };
            return Extensions.Select(e => name + "." + environment + e);
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

        /// <summary>Copies a value into config maps (any case), expanding ${NAME} in text when <paramref name="lookup"/> is given.</summary>
        private static object ToConfig(object value, Func<string, string> lookup) => value switch
        {
            Dictionary<string, object> map => map.Aggregate(NewMap(), (m, kv) => { m[kv.Key] = ToConfig(kv.Value, lookup); return m; }),
            List<object> list => list.Select(i => ToConfig(i, lookup)).ToList(),
            string s when lookup != null => DotEnv.Expand(s, lookup),
            _ => value,
        };

        private static Dictionary<string, object> ToConfig(Dictionary<string, object> map, Func<string, string> lookup) => (Dictionary<string, object>)ToConfig((object)map, lookup);

        internal static Dictionary<string, object> CopySettings(Dictionary<string, object> map) => ToConfig(map, null);

        /// <summary>NAME=value pairs as settings: a prefix is removed, __ nests (DATABASE__HOST is database.host) and names are lower-cased.</summary>
        private static Dictionary<string, object> FromPairs(IEnumerable<KeyValuePair<string, string>> pairs, string prefix, bool requirePrefix)
        {
            var map = NewMap();
            foreach (var pair in pairs)
            {
                string name = pair.Key;
                if (!string.IsNullOrEmpty(prefix) && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) name = name.Substring(prefix.Length);
                else if (requirePrefix) continue;
                string[] parts = name.Split("__", StringSplitOptions.RemoveEmptyEntries).Select(p => p.ToLowerInvariant()).ToArray();
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
            if (config.options.ExplicitProjectFiles)
            {
                config.EnsureLoaded();
            }
            else if (host.FileRoot == null && host.Config == null && host.ConfigEnvironment == null)
            {
                // A standalone script can use @env() before ever reading $config.
                var dotenv = new ConfigOptions { ExplicitProjectFiles = true, ExportEnv = false };
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
        /// tests): its kscript.json "config" section, with files read from the project folder unless config.root
        /// says otherwise. Null outside a project.
        /// </summary>
        private static ConfigOptions ProjectOptions(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !System.IO.Directory.Exists(directory)) return null;
            try
            {
                KScript.Projects.ProjectConfig project = KScript.Projects.ProjectConfig.FindAndLoad(directory);
                if (project?.FilePath == null) return null;
                ConfigOptions options = project.ConfigOptions();
                options.Root ??= project.Directory;
                return options;
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
                        string source = options.ExplicitProjectFiles ? "a file listed in kscript.json under config.files" : "config.yaml";
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
