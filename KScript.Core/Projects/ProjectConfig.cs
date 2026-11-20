using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KScript.Projects
{
    /// <summary>A problem with a kscript.json file that makes it unusable, such as invalid JSON.</summary>
    public sealed class ProjectConfigException : Exception
    {
        public ProjectConfigException(string message) : base(message) { }
    }

    /// <summary>The <c>serve</c> section: what <c>kscript serve</c> uses when no options are given.</summary>
    public sealed class ServeSettings
    {
        /// <summary>The site folder, relative to kscript.json.</summary>
        public string Root { get; set; } = ".";
        public int Port { get; set; } = 8080;
        public string Host { get; set; } = "localhost";
    }

    /// <summary>The <c>app</c> section: what <c>kscript compile</c> builds and how the program is named.</summary>
    /// <summary>The "make" section: settings for <c>kscript make</c>.</summary>
    public sealed class MakeSettings
    {
        public const string KScriptFormat = "kscript";
        public const string XmlFormat = "xml";

        /// <summary>The format new scripts, tests and routes are written in: "kscript" (.kscript) or "xml" (.ks.xml). Null when unset.</summary>
        public string Format { get; set; }
    }

    public sealed class AppSettings
    {
        /// <summary>The script the app runs, relative to kscript.json (usually one whose root is &lt;KScript.Window&gt;).</summary>
        public string Main { get; set; }

        /// <summary>The app's name, as shown in the Dock, Start menu or launcher. Defaults to the project's name.</summary>
        public string Name { get; set; }

        /// <summary>A reverse-DNS id such as com.example.lifelab (macOS bundle id). Made from the name when missing.</summary>
        public string Id { get; set; }

        /// <summary>The app's version, such as 1.0.0.</summary>
        public string Version { get; set; }

        /// <summary>An icon file relative to kscript.json: .icns for macOS, .ico for Windows, .png for Linux.</summary>
        public string Icon { get; set; }
    }

    /// <summary>The <c>lint</c> section: used by <c>kscript check</c> and by the language server.</summary>
    public sealed class LintSettings
    {
        /// <summary>Files <c>kscript check</c> looks at when given the project folder. Globs relative to kscript.json.</summary>
        public List<string> Include { get; set; } = new List<string>(DefaultInclude);

        /// <summary>Files and folders that are never checked, by <c>kscript check</c> or the editor.</summary>
        public List<string> Exclude { get; set; } = new List<string>(DefaultExclude);

        /// <summary>Severity per diagnostic code (KS3003) or code prefix (KS3*): off, hint, info, warning or error.</summary>
        public Dictionary<string, string> Rules { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public bool WarningsAsErrors { get; set; }

        public static readonly string[] DefaultInclude = { "**/*.ks.xml", "**/*.ks", "**/*.kscript", "**/*.html", "**/*.md" };
        public static readonly string[] DefaultExclude = { "_templates", "node_modules", ".git" };
    }

    /// <summary>The <c>test</c> section: used by <c>kscript test</c>.</summary>
    public sealed class TestSettings
    {
        /// <summary>The test files <c>kscript test</c> runs when given a folder. Globs relative to kscript.json.</summary>
        public List<string> Include { get; set; } = new List<string>(DefaultInclude);

        /// <summary>Files and folders that are never run as tests.</summary>
        public List<string> Exclude { get; set; } = new List<string>(DefaultExclude);

        /// <summary>How long one test may run, in seconds, before it fails. 0 means no limit.</summary>
        public double Timeout { get; set; } = DefaultTimeout;

        /// <summary>Packages imported by every test file, on top of the project's packages.</summary>
        public List<string> Packages { get; set; } = new List<string>();

        public const double DefaultTimeout = 10;
        public static readonly string[] DefaultInclude = { "**/*.test.kscript", "**/*.test.ks.xml", "**/*.test.xml" };
        public static readonly string[] DefaultExclude = { "_templates", "node_modules", ".git" };

        /// <summary>True for a file named like a test: <c>name.test.kscript</c>, <c>name.test.ks.xml</c> or <c>name.test.xml</c>.</summary>
        public static bool IsTestFileName(string path)
        {
            string name = Path.GetFileName(path ?? string.Empty);
            return name.EndsWith(".test.kscript", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".test.ks.xml", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".test.xml", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>The <c>config</c> section: where <c>$config</c> reads a script's settings from.</summary>
    public sealed class ConfigSettings
    {
        /// <summary>The environment used when neither kscript --env nor KSCRIPT_ENV names one.</summary>
        public string Environment { get; set; }

        /// <summary>The base files (default: config, .env).</summary>
        public List<string> Files { get; set; }

        /// <summary>Environment variables with this prefix override the files.</summary>
        public string Prefix { get; set; }

        /// <summary>The folder the files are in, relative to kscript.json. Null means each script's own folder.</summary>
        public string Root { get; set; }

        /// <summary>Settings that must be set.</summary>
        public List<string> Required { get; set; } = new List<string>();

        /// <summary>True layers the app's own config file (config.json in its config folder) over the project's files.</summary>
        public bool User { get; set; }
    }

    /// <summary>
    /// The "format" section: how <c>kscript fmt</c> and the editor's Format Document lay files out. Unset values
    /// fall back to the formatter's defaults (or, in an editor, its tab settings).
    /// </summary>
    public sealed class FormatSettings
    {
        /// <summary>Spaces per indent level, from "indent": 2; null when not set.</summary>
        public int? IndentSize { get; set; }

        /// <summary>True for "indent": "tab"; null when not set.</summary>
        public bool? UseTabs { get; set; }

        public int? PrintWidth { get; set; }

        /// <summary>"auto", "lf" or "crlf".</summary>
        public string EndOfLine { get; set; } = "auto";

        public int? MaxBlankLines { get; set; }

        /// <summary>Elements whose content is kept exactly as written.</summary>
        public List<string> Preserve { get; set; } = new List<string>();

        /// <summary>Files <c>kscript fmt</c> formats in a project folder. Markdown pages are never reformatted.</summary>
        public List<string> Include { get; set; } = new List<string>(DefaultInclude);

        public List<string> Exclude { get; set; } = new List<string>(DefaultExclude);

        public static readonly string[] DefaultInclude = { "**/*.ks.xml", "**/*.ks", "**/*.kscript", "**/*.html" };
        public static readonly string[] DefaultExclude = { "_templates", "node_modules", ".git" };

        /// <summary>False turns formatting off for the whole project (kscript fmt skips it, the editor makes no changes).</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Rules turned on or off by name, from "rules": { "statementPerLine": false }. Unlisted rules are on.</summary>
        public Dictionary<string, bool> Rules { get; } = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The formatter's rules, each of which can be turned off in format.rules.</summary>
        public static readonly string[] RuleNames =
        {
            "indent", "statementPerLine", "tidyTags", "wrapAttributes", "trimTrailingWhitespace", "collapseBlankLines", "finalNewline",
        };
    }

    /// <summary>
    /// A KScript project's settings, read from <c>kscript.json</c> in the project folder. <c>kscript serve</c>,
    /// <c>kscript check</c>, <c>kscript make</c> and the language server look for it in the folder they work on and
    /// its parents, so commands run from a subfolder still find it. Comments and trailing commas are allowed.
    /// <code>
    /// {
    ///   "name": "my-site",
    ///   "standard": "kscript:recommended",
    ///   "serve": { "root": ".", "port": 8080, "host": "localhost" },
    ///   "app": { "main": "app.ks.xml", "name": "My App", "icon": "icon.icns" },
    ///   "lint": {
    ///     "exclude": ["_templates", "drafts/**"],
    ///     "rules": { "KS3003": "off", "KS4*": "error" },
    ///     "warningsAsErrors": false
    ///   }
    /// }
    /// </code>
    /// </summary>
    /// <summary>
    /// The "http" section: route files, middleware and its settings, for KScript.Http (routes, controllers and
    /// middleware served by <c>kscript serve</c>).
    /// </summary>
    public sealed class HttpSettings
    {
        /// <summary>A route file and how its routes are mounted.</summary>
        public sealed class RouteFile
        {
            public string File { get; set; }
            public string Prefix { get; set; } = string.Empty;

            /// <summary>True for API routes: errors and missing routes answer with JSON instead of HTML.</summary>
            public bool Api { get; set; }
        }

        /// <summary>The route files, relative to the site folder. Null means the defaults (routes.* and api.*).</summary>
        public List<RouteFile> Routes { get; set; }

        /// <summary>Middleware run for every route, by name, such as log or cors.</summary>
        public List<string> Middleware { get; set; } = new List<string>();

        /// <summary>Middleware groups: a name that stands for a list of middleware, such as "admin": ["auth", "log"].</summary>
        public Dictionary<string, List<string>> Groups { get; } = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The folder @view() looks in first, relative to the site folder. It is never served as pages.</summary>
        public string Views { get; set; } = "views";

        /// <summary>The prefix of api.* route files.</summary>
        public string ApiPrefix { get; set; } = "/api";

        /// <summary>False makes controllers read-only, like pages. Writes never leave the site folder.</summary>
        public bool WriteFiles { get; set; } = true;

        public List<string> CorsOrigins { get; set; } = new List<string> { "*" };
        public List<string> CorsMethods { get; set; } = new List<string> { "GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS" };
        public List<string> CorsHeaders { get; set; } = new List<string> { "Content-Type", "Authorization" };
        public bool CorsCredentials { get; set; }

        /// <summary>The environment variable that holds the bearer token the auth middleware accepts.</summary>
        public string AuthTokenEnv { get; set; } = "API_TOKEN";

        /// <summary>Requests per window for the throttle middleware, per client address.</summary>
        public int ThrottleLimit { get; set; } = 60;
        public double ThrottleSeconds { get; set; } = 60;

        public static readonly string[] DefaultRouteNames = { "routes", "api", "routes/web", "routes/api" };
    }

    public sealed class ProjectConfig
    {
        public const string FileName = "kscript.json";

        public static readonly string[] Severities = { "off", "hint", "info", "warning", "error" };

        /// <summary>The kscript.json file, or null for the defaults.</summary>
        public string FilePath { get; private set; }

        /// <summary>The project folder (where kscript.json is).</summary>
        public string Directory { get; private set; }

        public string Name { get; set; }
        public ServeSettings Serve { get; } = new ServeSettings();
        public LintSettings Lint { get; } = new LintSettings();
        public TestSettings Test { get; } = new TestSettings();
        public ConfigSettings Config { get; } = new ConfigSettings();

        /// <summary>
        /// The options $config uses for scripts in this project. <paramref name="environment"/> (from kscript --env)
        /// wins over KSCRIPT_ENV, which wins over config.environment.
        /// </summary>
        public KScript.Runtime.ConfigOptions ConfigOptions(string environment = null) => new KScript.Runtime.ConfigOptions
        {
            Environment = environment ?? System.Environment.GetEnvironmentVariable("KSCRIPT_ENV") ?? Config.Environment,
            Files = Config.Files,
            Prefix = Config.Prefix,
            Root = Config.Root == null ? null : Path.GetFullPath(Path.Combine(Directory, Config.Root)),
            Required = Config.Required.ToList(),
            UserFolder = Config.User ? KScript.Runtime.AppFolders.Folder(AppName, "config") : null,
        };

        /// <summary>The app's name for its folders (KScript.Storage): app.name, else name, else the project folder's name.</summary>
        public string AppName => App.Name ?? Name ?? Path.GetFileName(Directory?.TrimEnd(Path.DirectorySeparatorChar)) ?? "kscript";
        public FormatSettings Format { get; } = new FormatSettings();
        public AppSettings App { get; } = new AppSettings();
        public MakeSettings Make { get; } = new MakeSettings();
        public HttpSettings Http { get; } = new HttpSettings();

        /// <summary>
        /// The recommended folder for each kind of file, which <c>kscript make</c> writes to. kscript.json's "paths"
        /// section moves any of them, relative to the site folder: <c>"paths": { "controllers": "app/controllers" }</c>.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> DefaultPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["routes"] = "routes",
            ["controllers"] = "controllers",
            ["middleware"] = "middleware",
            ["requests"] = "requests",
            ["views"] = "views",
            ["components"] = "_components",
            ["layouts"] = "_layouts",
            ["partials"] = "_partials",
            ["styles"] = "styles",
            ["lib"] = "lib",
            ["types"] = "types",
            ["storage"] = "lib",
            ["jobs"] = "jobs",
            ["windows"] = ".",
            ["config"] = "config",
            ["locales"] = "locales",
            ["logging"] = "lib",
            ["data"] = "data",
            ["scripts"] = "scripts",
            ["tests"] = "tests",
        };

        /// <summary>The folders kscript.json's "paths" section sets, by name (see <see cref="DefaultPaths"/>).</summary>
        public Dictionary<string, string> Paths { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The folder for a kind of file: kscript.json's "paths" setting, or the recommended one.</summary>
        public string PathFor(string name) =>
            Paths.TryGetValue(name, out var path) ? path
            : string.Equals(name, "views", StringComparison.OrdinalIgnoreCase) ? Http.Views
            : DefaultPaths.TryGetValue(name, out var folder) ? folder : name;

        /// <summary>
        /// Packages every script in the project imports without an &lt;import&gt;, such as KScript.Terminal.
        /// Names are written as in &lt;import package="..."&gt;; the KScript. prefix is optional.
        /// </summary>
        public List<string> Packages { get; set; } = new List<string>();

        /// <summary>Unknown keys and values that were ignored.</summary>
        public List<string> Warnings { get; } = new List<string>();

        /// <summary>The standards kscript.json names in "standard", as written. Unset means kscript:recommended.</summary>
        public List<string> Standard { get; private set; } = new List<string> { Standards.Recommended };

        /// <summary>Every standard that was applied, including the ones they extend, in the order they were applied.</summary>
        public List<AppliedStandard> AppliedStandards { get; } = new List<AppliedStandard>();

        /// <summary>The standard files that were read (not built-in ones), so an editor can notice when one changes.</summary>
        public List<string> StandardFiles { get; } = new List<string>();

        /// <summary>
        /// Where each lint and format setting came from: a key such as <c>lint.rules.KS6*</c> or <c>format.indent</c>
        /// mapped to a standard's label or <c>kscript.json</c>. Settings missing here are the built-in defaults.
        /// </summary>
        public Dictionary<string, string> Sources { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The layer being read: a standard's label, or kscript.json.</summary>
        private string source = FileName;

        /// <summary>True while reading a standard file, where only shareable settings are allowed.</summary>
        private bool readingStandard;

        /// <summary>The site folder <c>kscript serve</c> serves.</summary>
        public string SiteRoot => Path.GetFullPath(Path.Combine(Directory, Serve.Root ?? "."));

        /// <summary>Settings for a folder without a kscript.json: the built-in defaults with kscript:recommended applied.</summary>
        public static ProjectConfig Default(string directory)
        {
            var config = new ProjectConfig { Directory = Path.GetFullPath(directory ?? ".") };
            config.ApplyStandards(config.Standard, config.Directory, new Stack<string>(), File.Exists, File.ReadAllText);
            config.source = FileName;
            return config;
        }

        private static ProjectConfig recommended;

        /// <summary>The settings files outside any project get: kscript:recommended on the built-in defaults.</summary>
        public static ProjectConfig Recommended => recommended ??= Default(".");

        /// <summary>The nearest kscript.json in <paramref name="start"/> (a file or folder) or a parent folder, or null.</summary>
        public static string Find(string start)
        {
            if (string.IsNullOrEmpty(start)) return null;
            string dir;
            try
            {
                string full = Path.GetFullPath(start);
                dir = System.IO.Directory.Exists(full) ? full : Path.GetDirectoryName(full);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return null;
            }

            while (!string.IsNullOrEmpty(dir))
            {
                string candidate = Path.Combine(dir, FileName);
                if (File.Exists(candidate)) return candidate;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        /// <summary>Loads the nearest kscript.json above <paramref name="start"/>, or returns null when there is none.</summary>
        public static ProjectConfig FindAndLoad(string start)
        {
            string file = Find(start);
            return file == null ? null : Load(file);
        }

        public static ProjectConfig Load(string file) => Parse(File.ReadAllText(file), file);

        /// <summary>
        /// Reads kscript.json text. The standards it names are read with <paramref name="fileExists"/> and
        /// <paramref name="readText"/> (the disk by default; an editor passes its open documents).
        /// </summary>
        public static ProjectConfig Parse(string json, string file, Func<string, bool> fileExists = null, Func<string, string> readText = null)
        {
            string full = Path.GetFullPath(file);
            var config = new ProjectConfig { FilePath = full, Directory = Path.GetDirectoryName(full) };
            fileExists ??= File.Exists;
            readText ??= File.ReadAllText;

            using (JsonDocument doc = ParseJson(json, full))
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    throw new ProjectConfigException($"{full}: should hold an object, e.g. {{ \"name\": \"my-site\" }}.");

                // The standards go first, so that everything kscript.json sets itself wins over them.
                if (doc.RootElement.TryGetProperty("standard", out JsonElement standard))
                {
                    List<string> named = StandardList(standard, "standard", config);
                    if (named != null) config.Standard = named;
                }
                try
                {
                    config.ApplyStandards(config.Standard, config.Directory, new Stack<string>(), fileExists, readText);
                }
                catch (ProjectConfigException ex) when (!ex.Message.StartsWith(full, StringComparison.Ordinal))
                {
                    throw new ProjectConfigException($"{full}: {ex.Message}");
                }
                config.source = FileName;

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    switch (prop.Name)
                    {
                        case "$schema":
                        case "standard":
                            break;
                        case "name":
                            config.Name = String(prop.Value, "name", config);
                            break;
                        case "serve":
                            ReadServe(prop.Value, config);
                            break;
                        case "lint":
                            ReadLint(prop.Value, config);
                            break;
                        case "test":
                            ReadTest(prop.Value, config);
                            break;
                        case "config":
                            ReadConfig(prop.Value, config);
                            break;
                        case "format":
                            ReadFormat(prop.Value, config);
                            break;
                        case "app":
                            ReadApp(prop.Value, config);
                            break;
                        case "make":
                            ReadMake(prop.Value, config);
                            break;
                        case "http":
                            ReadHttp(prop.Value, config);
                            break;
                        case "paths":
                            ReadPaths(prop.Value, config);
                            break;
                        case "packages":
                            config.Packages = Strings(prop.Value, "packages", config) ?? config.Packages;
                            foreach (string package in config.Packages)
                            {
                                if (!KScript.Runtime.PackageRegistry.Default.IsKnown(package))
                                    config.Warnings.Add($"packages: there is no package '{package}'. Packages: {string.Join(", ", KScript.Runtime.PackageRegistry.Default.Names.Where(n => n != "KScript.Core"))}.");
                            }
                            break;
                        default:
                            config.Warnings.Add($"unknown setting '{prop.Name}' (expected name, standard, packages, paths, serve, http, lint, test, config, format, make or app).");
                            break;
                    }
                }
            }
            return config;
        }

        private static JsonDocument ParseJson(string json, string full)
        {
            try
            {
                return JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            }
            catch (JsonException ex)
            {
                string where = ex.LineNumber != null ? $" (line {ex.LineNumber + 1})" : "";
                throw new ProjectConfigException($"{full}{where}: not valid JSON. {FirstSentence(ex.Message)}");
            }
        }

        /// <summary>"standard" or "extends": one name or path, or a list of them (applied in order, so later ones win).</summary>
        private static List<string> StandardList(JsonElement e, string name, ProjectConfig config)
        {
            if (e.ValueKind == JsonValueKind.String) return new List<string> { e.GetString() };
            if (e.ValueKind == JsonValueKind.Array && e.EnumerateArray().All(i => i.ValueKind == JsonValueKind.String))
                return e.EnumerateArray().Select(i => i.GetString()).ToList();
            config.Warnings.Add($"{name} should be a standard's name or path, such as \"{Standards.Recommended}\" or \"./standards/team.json\", or a list of them.");
            return null;
        }

        /// <summary>
        /// Applies standards in order: for each one, first the standards it extends, then its own settings.
        /// <paramref name="chain"/> holds the standards being applied, to report a standard that extends itself.
        /// </summary>
        private void ApplyStandards(IEnumerable<string> references, string baseDirectory, Stack<string> chain, Func<string, bool> fileExists, Func<string, string> readText)
        {
            foreach (string reference in references)
            {
                string resolved = Standards.Resolve(reference, baseDirectory, fileExists);
                if (chain.Contains(resolved, StringComparer.OrdinalIgnoreCase))
                {
                    string loop = string.Join(" -> ", chain.Reverse().Append(resolved).Select(LabelFor));
                    throw new ProjectConfigException($"the standards extend each other in a loop: {loop}.");
                }

                bool builtIn = Standards.IsBuiltInName(resolved);
                string text;
                if (builtIn)
                {
                    text = Standards.BuiltIn[resolved];
                }
                else
                {
                    try
                    {
                        text = readText(resolved);
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        throw new ProjectConfigException($"can't read the standard {resolved}: {ex.Message}");
                    }
                    if (!StandardFiles.Contains(resolved)) StandardFiles.Add(resolved);
                }

                chain.Push(resolved);
                ApplyStandard(text, resolved, builtIn, chain, fileExists, readText);
                chain.Pop();
            }
        }

        private void ApplyStandard(string text, string resolved, bool builtIn, Stack<string> chain, Func<string, bool> fileExists, Func<string, string> readText)
        {
            string label = LabelFor(resolved);
            using JsonDocument doc = ParseJson(text, resolved);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new ProjectConfigException($"{resolved}: a standard should hold an object, e.g. {{ \"extends\": \"{Standards.Recommended}\", \"lint\": {{ … }} }}.");

            List<string> extends = new List<string>();
            if (root.TryGetProperty("extends", out JsonElement e))
            {
                int before = Warnings.Count;
                extends = StandardList(e, "extends", this) ?? extends;
                PrefixWarnings(before, label);
            }
            string baseDirectory = builtIn ? Directory : Path.GetDirectoryName(resolved);
            ApplyStandards(extends, baseDirectory, chain, fileExists, readText);

            string name = null, description = null;
            int start = Warnings.Count;
            source = label;
            readingStandard = true;
            try
            {
                foreach (var prop in root.EnumerateObject())
                {
                    switch (prop.Name)
                    {
                        case "$schema":
                        case "extends":
                            break;
                        case "name":
                            name = String(prop.Value, "name", this);
                            break;
                        case "description":
                            description = String(prop.Value, "description", this);
                            break;
                        case "lint":
                            ReadLint(prop.Value, this);
                            break;
                        case "format":
                            ReadFormat(prop.Value, this);
                            break;
                        default:
                            Warnings.Add($"unknown setting '{prop.Name}' (a standard holds name, description, extends, lint and format; other settings belong in kscript.json).");
                            break;
                    }
                }
            }
            finally
            {
                readingStandard = false;
            }
            PrefixWarnings(start, label);
            AppliedStandards.Add(new AppliedStandard(label, name, description, builtIn ? null : resolved, extends));
        }

        private void PrefixWarnings(int from, string label)
        {
            for (int i = from; i < Warnings.Count; i++) Warnings[i] = $"standard {label}: {Warnings[i]}";
        }

        /// <summary>How a resolved standard is shown: its built-in name, or its path relative to the project folder.</summary>
        public string LabelFor(string resolved)
        {
            if (Standards.IsBuiltInName(resolved)) return resolved;
            string rel = Path.GetRelativePath(Directory, resolved).Replace('\\', '/');
            return Path.IsPathRooted(rel) ? resolved : rel;
        }

        /// <summary>Records that the layer being read set a setting.</summary>
        private void Set(string key) => Sources[key] = source;

        /// <summary>
        /// Sets a lint rule. A prefix such as KS6* replaces what earlier layers said about the codes it covers, so a
        /// project's "KS6*": "off" turns off a standard's "KS6001": "error" too. In one layer, the longest match wins.
        /// </summary>
        private void SetRule(string key, string severity)
        {
            if (key.EndsWith('*'))
            {
                string prefix = key.Substring(0, key.Length - 1);
                foreach (string other in Lint.Rules.Keys.ToList())
                {
                    if (string.Equals(other, key, StringComparison.OrdinalIgnoreCase) || !other.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    if (Sources.TryGetValue("lint.rules." + other, out string from) && from == source) continue;
                    Lint.Rules.Remove(other);
                    Sources.Remove("lint.rules." + other);
                }
            }
            Lint.Rules.Remove(key); // keep the spelling of the layer that set it last
            Lint.Rules[key] = severity;
            Set("lint.rules." + key);
        }

        private static void ReadPaths(JsonElement e, ProjectConfig config)
        {
            if (!Object(e, "paths", config)) return;
            foreach (var p in e.EnumerateObject())
            {
                if (!DefaultPaths.ContainsKey(p.Name))
                {
                    config.Warnings.Add($"paths: unknown folder '{p.Name}' (expected {string.Join(", ", DefaultPaths.Keys)}).");
                    continue;
                }
                string folder = String(p.Value, "paths." + p.Name, config);
                if (folder == null) continue;
                folder = folder.Replace('\\', '/').Trim().TrimEnd('/');
                if (folder.Length == 0) folder = ".";
                if (Path.IsPathRooted(folder) || folder.Split('/').Contains(".."))
                {
                    config.Warnings.Add($"paths.{p.Name} should be a folder inside the project, such as \"{DefaultPaths[p.Name]}\", not \"{folder}\".");
                    continue;
                }
                config.Paths[p.Name] = folder;
            }
            // @view() looks in paths.views unless http.views says otherwise.
            if (config.Paths.TryGetValue("views", out var views) && !config.httpViewsSet) config.Http.Views = views;
        }

        /// <summary>True once http.views is read, so paths.views doesn't replace it.</summary>
        private bool httpViewsSet;

        private static void ReadMake(JsonElement e, ProjectConfig config)
        {
            if (!Object(e, "make", config)) return;
            foreach (var p in e.EnumerateObject())
            {
                switch (p.Name)
                {
                    case "format":
                        string format = String(p.Value, "make.format", config);
                        if (format == MakeSettings.KScriptFormat || format == MakeSettings.XmlFormat) config.Make.Format = format;
                        else if (format != null) config.Warnings.Add($"make.format should be \"kscript\" or \"xml\", not \"{format}\".");
                        break;
                    default:
                        config.Warnings.Add($"unknown setting 'make.{p.Name}' (expected format).");
                        break;
                }
            }
        }

        private static void ReadApp(JsonElement e, ProjectConfig config)
        {
            if (!Object(e, "app", config)) return;
            foreach (var p in e.EnumerateObject())
            {
                switch (p.Name)
                {
                    case "main": config.App.Main = String(p.Value, "app.main", config); break;
                    case "name": config.App.Name = String(p.Value, "app.name", config); break;
                    case "id": config.App.Id = String(p.Value, "app.id", config); break;
                    case "version": config.App.Version = String(p.Value, "app.version", config); break;
                    case "icon": config.App.Icon = String(p.Value, "app.icon", config); break;
                    default:
                        config.Warnings.Add($"unknown setting 'app.{p.Name}' (expected main, name, id, version or icon).");
                        break;
                }
            }
        }

        private static void ReadHttp(JsonElement e, ProjectConfig config)
        {
            if (!Object(e, "http", config)) return;
            HttpSettings http = config.Http;
            foreach (var p in e.EnumerateObject())
            {
                switch (p.Name)
                {
                    case "routes":
                        if (p.Value.ValueKind != JsonValueKind.Array)
                        {
                            config.Warnings.Add("http.routes should be a list of route files, e.g. [\"routes.ks.xml\", { \"file\": \"api.ks.xml\", \"prefix\": \"/api\", \"api\": true }].");
                            break;
                        }
                        http.Routes = new List<HttpSettings.RouteFile>();
                        foreach (JsonElement item in p.Value.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                http.Routes.Add(new HttpSettings.RouteFile { File = item.GetString() });
                                continue;
                            }
                            if (item.ValueKind != JsonValueKind.Object)
                            {
                                config.Warnings.Add("http.routes: each entry is a file name or { \"file\", \"prefix\", \"api\" }.");
                                continue;
                            }
                            var route = new HttpSettings.RouteFile();
                            foreach (var f in item.EnumerateObject())
                            {
                                switch (f.Name)
                                {
                                    case "file": route.File = String(f.Value, "http.routes[].file", config); break;
                                    case "prefix": route.Prefix = String(f.Value, "http.routes[].prefix", config) ?? string.Empty; break;
                                    case "api": route.Api = Bool(f.Value, "http.routes[].api", config) ?? false; break;
                                    default: config.Warnings.Add($"unknown setting 'http.routes[].{f.Name}' (expected file, prefix or api)."); break;
                                }
                            }
                            if (string.IsNullOrWhiteSpace(route.File)) config.Warnings.Add("http.routes: an entry has no \"file\".");
                            else http.Routes.Add(route);
                        }
                        break;
                    case "middleware":
                        http.Middleware = Strings(p.Value, "http.middleware", config) ?? http.Middleware;
                        break;
                    case "groups":
                        if (!Object(p.Value, "http.groups", config)) break;
                        foreach (var g in p.Value.EnumerateObject())
                        {
                            List<string> names = Strings(g.Value, $"http.groups.{g.Name}", config);
                            if (names != null) http.Groups[g.Name] = names;
                        }
                        break;
                    case "views":
                        http.Views = String(p.Value, "http.views", config) ?? http.Views;
                        config.httpViewsSet = true;
                        break;
                    case "api_prefix":
                        http.ApiPrefix = String(p.Value, "http.api_prefix", config) ?? http.ApiPrefix;
                        break;
                    case "write_files":
                        http.WriteFiles = Bool(p.Value, "http.write_files", config) ?? http.WriteFiles;
                        break;
                    case "cors":
                        if (!Object(p.Value, "http.cors", config)) break;
                        foreach (var c in p.Value.EnumerateObject())
                        {
                            switch (c.Name)
                            {
                                case "origins": http.CorsOrigins = Strings(c.Value, "http.cors.origins", config) ?? http.CorsOrigins; break;
                                case "methods": http.CorsMethods = Strings(c.Value, "http.cors.methods", config) ?? http.CorsMethods; break;
                                case "headers": http.CorsHeaders = Strings(c.Value, "http.cors.headers", config) ?? http.CorsHeaders; break;
                                case "credentials": http.CorsCredentials = Bool(c.Value, "http.cors.credentials", config) ?? http.CorsCredentials; break;
                                default: config.Warnings.Add($"unknown setting 'http.cors.{c.Name}' (expected origins, methods, headers or credentials)."); break;
                            }
                        }
                        break;
                    case "auth":
                        if (!Object(p.Value, "http.auth", config)) break;
                        foreach (var a in p.Value.EnumerateObject())
                        {
                            if (a.Name == "token_env") http.AuthTokenEnv = String(a.Value, "http.auth.token_env", config) ?? http.AuthTokenEnv;
                            else config.Warnings.Add($"unknown setting 'http.auth.{a.Name}' (expected token_env).");
                        }
                        break;
                    case "throttle":
                        if (!Object(p.Value, "http.throttle", config)) break;
                        foreach (var t in p.Value.EnumerateObject())
                        {
                            switch (t.Name)
                            {
                                case "limit":
                                    if (t.Value.ValueKind == JsonValueKind.Number && t.Value.TryGetInt32(out int limit) && limit > 0) http.ThrottleLimit = limit;
                                    else config.Warnings.Add("http.throttle.limit should be a whole number above 0.");
                                    break;
                                case "seconds":
                                    if (t.Value.ValueKind == JsonValueKind.Number && t.Value.TryGetDouble(out double seconds) && seconds > 0) http.ThrottleSeconds = seconds;
                                    else config.Warnings.Add("http.throttle.seconds should be a number above 0.");
                                    break;
                                default: config.Warnings.Add($"unknown setting 'http.throttle.{t.Name}' (expected limit or seconds)."); break;
                            }
                        }
                        break;
                    default:
                        config.Warnings.Add($"unknown setting 'http.{p.Name}' (expected routes, middleware, groups, views, api_prefix, write_files, cors, auth or throttle).");
                        break;
                }
            }
        }

        private static bool? Bool(JsonElement e, string name, ProjectConfig config)
        {
            if (e.ValueKind == JsonValueKind.True) return true;
            if (e.ValueKind == JsonValueKind.False) return false;
            config.Warnings.Add($"{name} should be true or false.");
            return null;
        }

        private static void ReadServe(JsonElement e, ProjectConfig config)
        {
            if (!Object(e, "serve", config)) return;
            foreach (var p in e.EnumerateObject())
            {
                switch (p.Name)
                {
                    case "root":
                        config.Serve.Root = String(p.Value, "serve.root", config) ?? ".";
                        break;
                    case "port":
                        if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out int port) && port > 0 && port < 65536) config.Serve.Port = port;
                        else config.Warnings.Add($"serve.port should be a number from 1 to 65535.");
                        break;
                    case "host":
                        config.Serve.Host = String(p.Value, "serve.host", config) ?? "localhost";
                        break;
                    default:
                        config.Warnings.Add($"unknown setting 'serve.{p.Name}' (expected root, port or host).");
                        break;
                }
            }
        }

        private static void ReadLint(JsonElement e, ProjectConfig config)
        {
            if (!Object(e, "lint", config)) return;
            foreach (var p in e.EnumerateObject())
            {
                switch (p.Name)
                {
                    case "include" when config.readingStandard:
                    case "exclude" when config.readingStandard:
                        config.Warnings.Add($"lint.{p.Name} belongs in the project's kscript.json, not in a standard.");
                        break;
                    case "include":
                        config.Lint.Include = Strings(p.Value, "lint.include", config) ?? config.Lint.Include;
                        config.Set("lint.include");
                        break;
                    case "exclude":
                        config.Lint.Exclude = Strings(p.Value, "lint.exclude", config) ?? config.Lint.Exclude;
                        config.Set("lint.exclude");
                        break;
                    case "warningsAsErrors":
                        if (p.Value.ValueKind == JsonValueKind.True || p.Value.ValueKind == JsonValueKind.False)
                        {
                            config.Lint.WarningsAsErrors = p.Value.GetBoolean();
                            config.Set("lint.warningsAsErrors");
                        }
                        else config.Warnings.Add($"lint.warningsAsErrors should be true or false.");
                        break;
                    case "rules":
                        if (!Object(p.Value, "lint.rules", config)) break;
                        foreach (var rule in p.Value.EnumerateObject())
                        {
                            string value = rule.Value.ValueKind == JsonValueKind.String ? rule.Value.GetString().Trim().ToLowerInvariant() : null;
                            if (value == "information") value = "info";
                            if (value == null || !Severities.Contains(value))
                                config.Warnings.Add($"lint.rules.{rule.Name} should be one of {string.Join(", ", Severities)}.");
                            else if (!Regex.IsMatch(rule.Name, @"^KS\d{0,4}\*?$", RegexOptions.IgnoreCase))
                                config.Warnings.Add($"lint.rules key '{rule.Name}' should be a code such as KS3003, or a prefix such as KS3*.");
                            else
                                config.SetRule(rule.Name, value);
                        }
                        break;
                    default:
                        config.Warnings.Add($"unknown setting 'lint.{p.Name}' (expected include, exclude, rules or warningsAsErrors).");
                        break;
                }
            }
        }

        private static void ReadTest(JsonElement e, ProjectConfig config)
        {
            if (!Object(e, "test", config)) return;
            foreach (var p in e.EnumerateObject())
            {
                switch (p.Name)
                {
                    case "include":
                        config.Test.Include = Strings(p.Value, "test.include", config) ?? config.Test.Include;
                        break;
                    case "exclude":
                        config.Test.Exclude = Strings(p.Value, "test.exclude", config) ?? config.Test.Exclude;
                        break;
                    case "timeout":
                        if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetDouble(out double seconds) && seconds >= 0) config.Test.Timeout = seconds;
                        else config.Warnings.Add("test.timeout should be a number of seconds (0 for no limit).");
                        break;
                    case "packages":
                        config.Test.Packages = Strings(p.Value, "test.packages", config) ?? config.Test.Packages;
                        foreach (string package in config.Test.Packages)
                        {
                            if (!KScript.Runtime.PackageRegistry.Default.IsKnown(package))
                                config.Warnings.Add($"test.packages: there is no package '{package}'.");
                        }
                        break;
                    default:
                        config.Warnings.Add($"unknown setting 'test.{p.Name}' (expected include, exclude, timeout or packages).");
                        break;
                }
            }
        }

        private static void ReadConfig(JsonElement e, ProjectConfig config)
        {
            if (!Object(e, "config", config)) return;
            foreach (var p in e.EnumerateObject())
            {
                switch (p.Name)
                {
                    case "environment":
                        config.Config.Environment = String(p.Value, "config.environment", config);
                        break;
                    case "files":
                        config.Config.Files = Strings(p.Value, "config.files", config);
                        break;
                    case "prefix":
                        config.Config.Prefix = String(p.Value, "config.prefix", config);
                        break;
                    case "root":
                        config.Config.Root = String(p.Value, "config.root", config);
                        break;
                    case "required":
                        config.Config.Required = Strings(p.Value, "config.required", config) ?? config.Config.Required;
                        break;
                    case "user":
                        config.Config.User = Bool(p.Value, "config.user", config) ?? false;
                        break;
                    default:
                        config.Warnings.Add($"unknown setting 'config.{p.Name}' (expected environment, files, prefix, root, required or user).");
                        break;
                }
            }
        }


        private static void ReadFormat(JsonElement e, ProjectConfig config)
        {
            if (!Object(e, "format", config)) return;
            foreach (var p in e.EnumerateObject())
            {
                switch (p.Name)
                {
                    case "indent":
                        if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out int size) && size >= 1 && size <= 16)
                        {
                            config.Format.IndentSize = size;
                            config.Format.UseTabs = false;
                            config.Set("format.indent");
                        }
                        else if (p.Value.ValueKind == JsonValueKind.String && string.Equals(p.Value.GetString().Trim(), "tab", StringComparison.OrdinalIgnoreCase))
                        {
                            config.Format.UseTabs = true;
                            config.Set("format.indent");
                        }
                        else config.Warnings.Add("format.indent should be a number of spaces from 1 to 16, or \"tab\".");
                        break;
                    case "tabWidth":
                        if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out int tab) && tab >= 1 && tab <= 16) { config.Format.IndentSize = tab; config.Set("format.tabWidth"); }
                        else config.Warnings.Add("format.tabWidth should be a number from 1 to 16.");
                        break;
                    case "printWidth":
                        if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out int width) && width >= 20) { config.Format.PrintWidth = width; config.Set("format.printWidth"); }
                        else config.Warnings.Add("format.printWidth should be a number of columns (20 or more).");
                        break;
                    case "maxBlankLines":
                        if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out int blank) && blank >= 0) { config.Format.MaxBlankLines = blank; config.Set("format.maxBlankLines"); }
                        else config.Warnings.Add("format.maxBlankLines should be 0 or more.");
                        break;
                    case "endOfLine":
                        string eol = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString().Trim().ToLowerInvariant() : null;
                        if (eol == "auto" || eol == "lf" || eol == "crlf") { config.Format.EndOfLine = eol; config.Set("format.endOfLine"); }
                        else config.Warnings.Add("format.endOfLine should be \"auto\", \"lf\" or \"crlf\".");
                        break;
                    case "enabled":
                        if (p.Value.ValueKind == JsonValueKind.True || p.Value.ValueKind == JsonValueKind.False) { config.Format.Enabled = p.Value.GetBoolean(); config.Set("format.enabled"); }
                        else config.Warnings.Add("format.enabled should be true or false.");
                        break;
                    case "rules":
                        if (!Object(p.Value, "format.rules", config)) break;
                        foreach (var rule in p.Value.EnumerateObject())
                        {
                            bool? on = rule.Value.ValueKind switch
                            {
                                JsonValueKind.True => true,
                                JsonValueKind.False => false,
                                JsonValueKind.String when string.Equals(rule.Value.GetString().Trim(), "on", StringComparison.OrdinalIgnoreCase) => true,
                                JsonValueKind.String when string.Equals(rule.Value.GetString().Trim(), "off", StringComparison.OrdinalIgnoreCase) => false,
                                _ => null,
                            };
                            string known = FormatSettings.RuleNames.FirstOrDefault(n => string.Equals(n, rule.Name, StringComparison.OrdinalIgnoreCase));
                            if (known == null)
                                config.Warnings.Add($"format.rules: there is no rule '{rule.Name}'. Rules: {string.Join(", ", FormatSettings.RuleNames)}.");
                            else if (on == null)
                                config.Warnings.Add($"format.rules.{rule.Name} should be true or false (or \"on\" / \"off\").");
                            else
                            {
                                config.Format.Rules[known] = on.Value;
                                config.Set("format.rules." + known);
                            }
                        }
                        break;
                    case "preserve":
                        config.Format.Preserve = Strings(p.Value, "format.preserve", config) ?? config.Format.Preserve;
                        config.Set("format.preserve");
                        break;
                    case "include" when config.readingStandard:
                    case "exclude" when config.readingStandard:
                        config.Warnings.Add($"format.{p.Name} belongs in the project's kscript.json, not in a standard.");
                        break;
                    case "include":
                        config.Format.Include = Strings(p.Value, "format.include", config) ?? config.Format.Include;
                        config.Set("format.include");
                        break;
                    case "exclude":
                        config.Format.Exclude = Strings(p.Value, "format.exclude", config) ?? config.Format.Exclude;
                        config.Set("format.exclude");
                        break;
                    default:
                        config.Warnings.Add($"unknown setting 'format.{p.Name}' (expected enabled, rules, indent, tabWidth, printWidth, maxBlankLines, endOfLine, preserve, include or exclude).");
                        break;
                }
            }
        }

        /// <summary>True when the file matches format.include and not format.exclude.</summary>
        public bool IsFormatIncluded(string path)
        {
            string rel = Relative(path);
            return rel != null && !IsGenerated(rel) && Format.Include.Any(g => GlobMatches(g, rel)) && !Format.Exclude.Any(g => GlobMatches(g, rel));
        }

        /// <summary>True when the file or folder is under the project folder and matches format.exclude.</summary>
        public bool IsFormatExcluded(string path)
        {
            string rel = Relative(path);
            return rel != null && Format.Exclude.Any(g => GlobMatches(g, rel));
        }

        /// <summary>True when the file matches test.include and not test.exclude.</summary>
        public bool IsTestIncluded(string path)
        {
            string rel = Relative(path);
            return rel != null && !IsGenerated(rel) && Test.Include.Any(g => GlobMatches(g, rel)) && !Test.Exclude.Any(g => GlobMatches(g, rel));
        }

        /// <summary>True when the file or folder is under the project folder and matches test.exclude.</summary>
        public bool IsTestExcluded(string path)
        {
            string rel = Relative(path);
            return rel != null && Test.Exclude.Any(g => GlobMatches(g, rel));
        }

        private static bool Object(JsonElement e, string name, ProjectConfig config)
        {
            if (e.ValueKind == JsonValueKind.Object) return true;
            config.Warnings.Add($"{name} should be an object {{ … }}.");
            return false;
        }

        private static string String(JsonElement e, string name, ProjectConfig config)
        {
            if (e.ValueKind == JsonValueKind.String) return e.GetString();
            config.Warnings.Add($"{name} should be a string.");
            return null;
        }

        private static List<string> Strings(JsonElement e, string name, ProjectConfig config)
        {
            if (e.ValueKind == JsonValueKind.Array && e.EnumerateArray().All(i => i.ValueKind == JsonValueKind.String))
                return e.EnumerateArray().Select(i => i.GetString()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            config.Warnings.Add($"{name} should be a list of paths, e.g. [\"drafts/**\"].");
            return null;
        }

        private static string FirstSentence(string message)
        {
            int i = message.IndexOf(". ", StringComparison.Ordinal);
            return i < 0 ? message : message.Substring(0, i + 1);
        }

        // ---- rules and paths ---------------------------------------------------------------------------------------

        /// <summary>The configured severity for a diagnostic code (off, hint, info, warning, error), or null to keep the default.</summary>
        public string SeverityFor(string code)
        {
            if (string.IsNullOrEmpty(code) || Lint.Rules.Count == 0) return null;
            if (Lint.Rules.TryGetValue(code, out string exact)) return exact;

            // The longest matching prefix wins, so KS30* beats KS3*.
            return Lint.Rules
                .Where(r => r.Key.EndsWith('*') && code.StartsWith(r.Key.Substring(0, r.Key.Length - 1), StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => r.Key.Length)
                .Select(r => r.Value)
                .FirstOrDefault();
        }

        /// <summary>
        /// The folder kscript writes generated files into, such as the agent docs in .kscript/agents. It is never
        /// checked, formatted or tested, whatever lint.include says.
        /// </summary>
        public const string GeneratedFolder = ".kscript";

        private static bool IsGenerated(string relative) => relative.StartsWith(GeneratedFolder + "/", StringComparison.Ordinal) || IsAgentFile(relative);

        /// <summary>
        /// True for the project's notes for coding agents (AGENTS.md and CLAUDE.md at the root): Markdown for tools,
        /// not pages, so they are never checked or served.
        /// </summary>
        public static bool IsAgentFile(string relative) => relative is "AGENTS.md" or "CLAUDE.md";

        /// <summary>True when the file is under the project folder and matches lint.exclude.</summary>
        public bool IsExcluded(string path)
        {
            string rel = Relative(path);
            return rel != null && Lint.Exclude.Any(g => GlobMatches(g, rel));
        }

        /// <summary>True when the file matches lint.include and not lint.exclude.</summary>
        public bool IsIncluded(string path)
        {
            string rel = Relative(path);
            return rel != null && !IsGenerated(rel) && Lint.Include.Any(g => GlobMatches(g, rel)) && !Lint.Exclude.Any(g => GlobMatches(g, rel));
        }

        /// <summary>The path relative to the project folder with / separators, or null when it is outside it.</summary>
        public string Relative(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string rel = Path.GetRelativePath(Directory, Path.GetFullPath(path)).Replace('\\', '/');
            return rel == ".." || rel.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(rel) ? null : rel;
        }

        /// <summary>
        /// gitignore-style matching: a pattern without a / (such as <c>_templates</c> or <c>*.draft.html</c>) matches a
        /// file or folder of that name anywhere; otherwise it is matched against the whole relative path, where
        /// <c>*</c> matches within one folder, <c>**</c> matches any number of folders, and a folder matches everything in it.
        /// </summary>
        public static bool GlobMatches(string pattern, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrEmpty(relativePath)) return false;
            string p = pattern.Trim().Replace('\\', '/');
            if (p.StartsWith("./", StringComparison.Ordinal)) p = p.Substring(2);
            p = p.TrimStart('/').TrimEnd('/');
            if (p.Length == 0) return false;

            var options = OperatingSystem.IsLinux() ? RegexOptions.None : RegexOptions.IgnoreCase;
            if (!p.Contains('/'))
            {
                var segment = new Regex("^" + SegmentRegex(p) + "$", options);
                return relativePath.Split('/').Any(s => segment.IsMatch(s));
            }

            var whole = new Regex("^" + PathRegex(p) + "(/.*)?$", options);
            return whole.IsMatch(relativePath);
        }

        private static string SegmentRegex(string p)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in p)
            {
                sb.Append(c switch { '*' => "[^/]*", '?' => "[^/]", _ => Regex.Escape(c.ToString()) });
            }
            return sb.ToString();
        }

        private static string PathRegex(string p)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < p.Length; i++)
            {
                if (p[i] == '*' && i + 1 < p.Length && p[i + 1] == '*')
                {
                    bool slashAfter = i + 2 < p.Length && p[i + 2] == '/';
                    sb.Append(slashAfter ? "(.*/)?" : ".*");
                    i += slashAfter ? 2 : 1;
                }
                else if (p[i] == '*') sb.Append("[^/]*");
                else if (p[i] == '?') sb.Append("[^/]");
                else sb.Append(Regex.Escape(p[i].ToString()));
            }
            return sb.ToString();
        }
    }
}
