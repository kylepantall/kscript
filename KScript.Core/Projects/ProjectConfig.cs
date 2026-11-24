using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KScript.Projects
{
    /// <summary>A problem with a kscript.json file that makes it unusable, such as invalid JSON.</summary>
    public sealed class ProjectConfigException : Exception, KScript.Runtime.IKScriptError
    {
        public ProjectConfigException(string message) : base(message) { }

        public KScript.Runtime.KScriptException ToKScriptError() => new KScript.Runtime.KScriptException(Message, "Config", this);
    }

    /// <summary>The <c>serve</c> section: what <c>kscript serve</c> uses when no options are given.</summary>
    public sealed class ServeSettings
    {
        /// <summary>The site folder, relative to kscript.json.</summary>
        public string Root { get; set; } = ".";
        public int Port { get; set; } = 8080;
        public string Host { get; set; } = "localhost";

        /// <summary>True to serve the docs, with a page about this project, next to the site (as <c>--with-docs</c> does).</summary>
        public bool Docs { get; set; }

        /// <summary>The port the docs are served on; null picks 4180, or a free port if that is in use.</summary>
        public int? DocsPort { get; set; }
        public DockerServeSettings Docker { get; } = new DockerServeSettings();
    }

    /// <summary>How kscript serve launches the project's Docker Compose service.</summary>
    public sealed class DockerServeSettings
    {
        public bool Enabled { get; set; }
        public string ComposeFile { get; set; }
        public string Service { get; set; } = "app";
        public bool Build { get; set; } = true;

        /// <summary>Container directory for the read-only project mount. Null uses the image's files.</summary>
        public string Mount { get; set; } = "/app";
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

        public static readonly string[] DefaultInclude = { "**/*.kscript", "**/*.ks.xml", "**/*.html", "**/*.md" };
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

        /// <summary>
        /// The environment tests run in (KSCRIPT_ENV), such as test, so config/config.test.yaml can point the database
        /// at memory. Null leaves the environment as it is. kscript --env and a KSCRIPT_ENV that is already set win.
        /// </summary>
        public string Env { get; set; }

        /// <summary>
        /// The database KScript.Data gives a test file: memory (a new SQLite database in memory for each file, made
        /// from the migrations, or from the models when there are none) or config (the one the config names). Either
        /// way each test's changes are undone when it ends.
        /// </summary>
        public string Database { get; set; } = "memory";

        public static readonly string[] DatabaseChoices = { "memory", "config" };

        public const double DefaultTimeout = 10;
        public static readonly string[] DefaultInclude = { "**/*.test.kscript", "**/*.test.ks.xml" };
        public static readonly string[] DefaultExclude = { "_templates", "node_modules", ".git" };

        /// <summary>True for a file named like a test: <c>name.test.kscript</c> or <c>name.test.ks.xml</c>.</summary>
        public static bool IsTestFileName(string path) => KScript.Syntax.Markup.ScriptFiles.IsTest(path);
    }

    /// <summary>The <c>config</c> section: where <c>$config</c> reads a script's settings from.</summary>
    public sealed class ConfigSettings
    {
        /// <summary>The environment used when neither kscript --env nor KSCRIPT_ENV names one.</summary>
        public string Environment { get; set; }

        /// <summary>The base files (default: optional .env).</summary>
        public List<string> Files { get; set; }

        /// <summary>Environment variables with this prefix override the files.</summary>
        public string Prefix { get; set; }

        /// <summary>The folder the files are in, relative to kscript.json. Null means the project folder.</summary>
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

        public static readonly string[] DefaultInclude = { "**/*.kscript", "**/*.ks.xml", "**/*.html" };
        public static readonly string[] DefaultExclude = { "_templates", "node_modules", ".git" };

        /// <summary>False turns formatting off for the whole project (kscript fmt skips it, the editor makes no changes).</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Rules turned on or off by name, from "rules": { "statement_per_line": false }. Unlisted rules are on.</summary>
        public Dictionary<string, bool> Rules { get; } = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The formatter's rules, each of which can be turned off in format.rules.</summary>
        public static readonly string[] RuleNames = KScript.Runtime.Conventions.Settings("format.rules",
            "indent", "statement_per_line", "tidy_tags", "wrap_attributes", "trim_trailing_whitespace", "collapse_blank_lines", "final_newline",
            "spacing", "brace_style", "wrap_strings", "wrap_chains", "wrap_bodies", "wrap_comments");
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
    ///   "app": { "main": "app.kscript", "name": "My App", "icon": "icon.icns" },
    ///   "lint": {
    ///     "exclude": ["_templates", "drafts/**"],
    ///     "rules": { "KS3003": "off", "KS4*": "error" },
    ///     "warnings_as_errors": false
    ///   }
    /// }
    /// </code>
    /// </summary>
    /// <summary>
    /// The queue section of kscript.json (KScript.Queue): which connection jobs go to by default, the connections,
    /// the order a worker checks its queues in, how long failed jobs are kept, and the worker's defaults.
    /// <code>
    /// "queue": {
    ///   "default": "file",
    ///   "connections": { "file": { "driver": "file" }, "fast": { "driver": "memory" } },
    ///   "queues": ["high", "default", "low"],
    ///   "failed": { "keep": "7d" },
    ///   "worker": { "concurrency": 2, "sleep": "1s" },
    ///   "env": { "test": { "default": "sync" } }
    /// }
    /// </code>
    /// </summary>
    public sealed class QueueSettings
    {
        /// <summary>One connection: a driver and its options.</summary>
        public sealed class Connection
        {
            public string Name { get; set; }

            /// <summary>sync, memory, file or database (redis comes later).</summary>
            public string Driver { get; set; }

            /// <summary>For the file driver: the folder, relative to the project. Default: the app's data folder.</summary>
            public string Path { get; set; }

            /// <summary>For the database driver: the database connection's name (database.connections). Default: the default database.</summary>
            public string Database { get; set; }

            /// <summary>For the redis driver: the environment variable holding the URL.</summary>
            public string UrlEnv { get; set; }
        }

        public static readonly string[] Drivers = { "sync", "memory", "file", "database" };

        public static readonly string[] SettingNames = KScript.Runtime.Conventions.Settings("queue", "default", "connections", "queues", "failed", "worker", "env");

        /// <summary>The connection jobs use unless they name one.</summary>
        public string Default { get; set; } = "file";

        /// <summary>The default connection per environment: "env": { "test": { "default": "sync" } }.</summary>
        public Dictionary<string, string> DefaultByEnvironment { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The connections by name. sync, memory, file and database always exist with their own driver.</summary>
        public Dictionary<string, Connection> Connections { get; } = new Dictionary<string, Connection>(StringComparer.OrdinalIgnoreCase)
        {
            ["sync"] = new Connection { Name = "sync", Driver = "sync" },
            ["memory"] = new Connection { Name = "memory", Driver = "memory" },
            ["file"] = new Connection { Name = "file", Driver = "file" },
            ["database"] = new Connection { Name = "database", Driver = "database" },
        };

        /// <summary>
        /// The queues a worker checks, first to last, when it isn't given --queue. Unless queue.queues lists them
        /// (<see cref="QueuesListed"/>), a worker checks these and then every other queue that has jobs.
        /// </summary>
        public List<string> Queues { get; set; } = new List<string> { "default" };

        /// <summary>True when kscript.json lists queue.queues, so a worker checks only those.</summary>
        public bool QueuesListed { get; set; }

        /// <summary>How long failed jobs are kept, in seconds (7 days).</summary>
        public double FailedKeep { get; set; } = 7 * 86400;

        /// <summary>How many jobs a worker runs at once.</summary>
        public int WorkerConcurrency { get; set; } = 1;

        /// <summary>How long a worker waits, in seconds, when every queue is empty.</summary>
        public double WorkerSleep { get; set; } = 1;

        /// <summary>The default connection in an environment (KSCRIPT_ENV or --env), else <see cref="Default"/>.</summary>
        public string DefaultFor(string environment) =>
            environment != null && DefaultByEnvironment.TryGetValue(environment, out string name) ? name : Default;
    }

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

        /// <summary>Sign-in settings for KScript.Auth (http.auth in kscript.json).</summary>
        public AuthSettings Auth { get; } = new AuthSettings();

        /// <summary>Requests per window for the throttle middleware, per client address.</summary>
        public int ThrottleLimit { get; set; } = 60;
        public double ThrottleSeconds { get; set; } = 60;

        public static readonly string[] DefaultRouteNames = { "routes", "api", "routes/web", "routes/api" };

        /// <summary>The <c>http.openapi</c> section: the OpenAPI document made from the routes.</summary>
        public OpenApiSettings OpenApi { get; } = new OpenApiSettings();
    }

    /// <summary>
    /// The OpenAPI document <c>kscript serve</c> makes from the routes (at /_openapi.json, /_openapi.yaml and the
    /// /_api page) and <c>kscript openapi</c> writes. <c>"openapi": false</c> turns the served copies off.
    /// </summary>
    public sealed class OpenApiSettings
    {
        /// <summary>False stops kscript serve answering /_openapi.json, /_openapi.yaml and /_api.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>info.title. Defaults to the project's name.</summary>
        public string Title { get; set; }

        /// <summary>info.version. Defaults to app.version, then 1.0.0.</summary>
        public string Version { get; set; }

        /// <summary>info.description.</summary>
        public string Description { get; set; }

        /// <summary>Which routes: "api" (from api.* files), "all", or "auto" (the API routes if there are any, otherwise all).</summary>
        public string Routes { get; set; } = "auto";

        /// <summary>
        /// A file, relative to kscript.json, to keep the document in: kscript watch, kscript serve and the editor (on save)
        /// rewrite it as the routes change. A .yaml or .yml name means YAML, anything else JSON. Null writes no file.
        /// </summary>
        public string File { get; set; }

        public static readonly string[] RouteChoices = { "auto", "api", "all" };
    }

    /// <summary>
    /// How KScript.Auth signs people in: http.auth in kscript.json. Every setting has a safe default, so an app only
    /// sets what it wants to change.
    /// </summary>
    public sealed class AuthSettings
    {
        /// <summary>Where the auth middleware sends people who aren't signed in.</summary>
        public string Login { get; set; } = "/login";

        /// <summary>Where the guest middleware sends people who are already signed in, and where @intended() goes by default.</summary>
        public string Home { get; set; } = "/";

        /// <summary>Where sessions are kept: file (the app's state folder, so they last across restarts) or memory.</summary>
        public string Sessions { get; set; } = "file";

        /// <summary>A folder for users, sessions and tokens, relative to the site folder. Null means the app's own data and state folders.</summary>
        public string Folder { get; set; }

        /// <summary>
        /// A KScript.Data model (in the models folder) that holds the users, such as "User". Null keeps them as JSON
        /// files in the app's data folder.
        /// </summary>
        public string Users { get; set; }

        /// <summary>Seconds a session lasts without a request before it ends.</summary>
        public double Lifetime { get; set; } = 2 * 60 * 60;

        /// <summary>Seconds a "remember me" sign-in lasts.</summary>
        public double Remember { get; set; } = 30 * 24 * 60 * 60;

        /// <summary>The session cookie's name.</summary>
        public string Cookie { get; set; } = "ks_session";

        /// <summary>True or false forces the cookie's Secure flag; null sets it for https requests.</summary>
        public bool? Secure { get; set; }

        /// <summary>The cookie's SameSite: Lax (the default), Strict or None.</summary>
        public string SameSite { get; set; } = "Lax";

        /// <summary>False turns off the CSRF check that web routes get when their file imports KScript.Auth.</summary>
        public bool Csrf { get; set; } = true;

        /// <summary>Failed sign-ins allowed per email and address before @attempt() waits.</summary>
        public int Attempts { get; set; } = 5;

        /// <summary>Seconds the failed sign-ins are counted over, and how long @attempt() then waits.</summary>
        public double Lockout { get; set; } = 60;

        /// <summary>Seconds a password reset token works for.</summary>
        public double ResetLifetime { get; set; } = 60 * 60;

        /// <summary>The public origin allowed to register and use passkeys. Null disables passkeys.</summary>
        public string PasskeyOrigin { get; set; }

        /// <summary>Enables creation of new accounts using a passkey as their first credential.</summary>
        public bool PasskeySignup { get; set; }

        /// <summary>The relying party domain. Null uses the origin's host.</summary>
        public string PasskeyRpId { get; set; }

        public static readonly string[] SameSiteValues = { "Lax", "Strict", "None" };

        public static readonly string[] SettingNames = KScript.Runtime.Conventions.Settings("auth", "token_env", "login", "home", "sessions", "folder", "users", "lifetime", "remember", "cookie", "secure", "same_site", "csrf", "attempts", "lockout", "reset_lifetime", "passkey_origin", "passkey_rp_id", "passkey_signup");
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
            ExplicitProjectFiles = FilePath != null,
            Prefix = Config.Prefix,
            Root = Config.Root == null ? (FilePath == null ? null : Directory) : Path.GetFullPath(Path.Combine(Directory, Config.Root)),
            Required = Config.Required.ToList(),
            UserFolder = Config.User ? KScript.Runtime.AppFolders.Folder(AppName, "config") : null,
        };

        /// <summary>The app's name for its folders (KScript.Storage): app.name, else name, else the project folder's name.</summary>
        public string AppName => App.Name ?? Name ?? Path.GetFileName(Directory?.TrimEnd(Path.DirectorySeparatorChar)) ?? "kscript";
        public FormatSettings Format { get; } = new FormatSettings();
        public AppSettings App { get; } = new AppSettings();
        public MakeSettings Make { get; } = new MakeSettings();
        public HttpSettings Http { get; } = new HttpSettings();
        public QueueSettings Queue { get; } = new QueueSettings();

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
            ["models"] = "models",
            ["migrations"] = "database/migrations",
            ["seeders"] = "database/seeders",
            ["errors"] = "errors",
            ["storage"] = "lib",
            ["jobs"] = "jobs",
            ["windows"] = ".",
            ["documents"] = "documents",
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

        /// <summary>Source package metadata when this project is published as an installable library.</summary>
        public LibraryManifest Library { get; private set; }

        /// <summary>The relative folder with the package manifest when this project publishes a nested library.</summary>
        public string LibraryFolder { get; private set; }

        /// <summary>The source libraries installed by this project, keyed by directory name.</summary>
        public IReadOnlyDictionary<string, LibraryDependency> Libraries { get; private set; } = System.Collections.ObjectModel.ReadOnlyDictionary<string, LibraryDependency>.Empty;

        /// <summary>
        /// Where the project's extension files are: folders (everything inside, subfolders too) or globs, relative
        /// to kscript.json. Their <c>extend date { fn ... }</c> blocks apply to every script in the project, as if
        /// each one imported them. Null (the setting left out) means the <c>extensions</c> folder.
        /// </summary>
        public List<string> Extensions { get; set; }

        /// <summary>The folder the project reads extensions from when kscript.json doesn't say.</summary>
        public const string DefaultExtensionsFolder = "extensions";

        /// <summary>
        /// Where the project's own native types are: folders or globs, relative to kscript.json, like
        /// <see cref="Extensions"/>. The types, enums and records they declare work in every script in the project
        /// without an import. Null (the setting left out) means the <c>natives</c> folder, where kscript make native writes.
        /// </summary>
        public List<string> Natives { get; set; }

        /// <summary>The folder the project reads native types from when kscript.json doesn't say.</summary>
        public const string DefaultNativesFolder = "natives";

        /// <summary>The project's native type files, full paths in a stable order (see <see cref="Natives"/>).</summary>
        public List<string> NativeFiles(Func<string, bool> directoryExists = null, Func<string, bool> fileExists = null,
            Func<string, IEnumerable<string>> listFiles = null, Func<string, IEnumerable<string>> listDirectories = null) =>
            FilesIn(Natives ?? new List<string> { DefaultNativesFolder }, directoryExists, fileExists, listFiles, listDirectories);

        /// <summary>
        /// The scripts in the models folder (paths.models), full paths in a stable order. KScript.Data finds a model
        /// that another model names there, so two models that link to each other don't have to import each other.
        /// </summary>
        public List<string> ModelFiles(Func<string, bool> directoryExists = null, Func<string, bool> fileExists = null,
            Func<string, IEnumerable<string>> listFiles = null, Func<string, IEnumerable<string>> listDirectories = null) =>
            FilesIn(new List<string> { PathFor("models") }, directoryExists, fileExists, listFiles, listDirectories);

        /// <summary>A script's file name without its .kscript, .ks.xml or .ks ending.</summary>
        public static string ScriptName(string path)
        {
            string name = Path.GetFileName(path);
            string suffix = KScript.Syntax.Markup.ScriptFiles.Extensions.FirstOrDefault(x => name.EndsWith(x, StringComparison.OrdinalIgnoreCase));
            return suffix == null ? name : name.Substring(0, name.Length - suffix.Length);
        }

        /// <summary>
        /// The model file a model's name points to, by the file's name (blog-post.kscript, blog_post.ks.xml and
        /// BlogPost.kscript are all BlogPost), or null.
        /// </summary>
        public static string ModelFileFor(IEnumerable<string> files, string model)
        {
            static string Plain(string s) => new string(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
            string wanted = Plain(model);
            return files.FirstOrDefault(f =>
            {
                string name = Path.GetFileName(f);
                string suffix = KScript.Syntax.Markup.ScriptFiles.Extensions.FirstOrDefault(x => name.EndsWith(x, StringComparison.OrdinalIgnoreCase));
                return suffix != null && !TestSettings.IsTestFileName(name) && Plain(name.Substring(0, name.Length - suffix.Length)) == wanted;
            });
        }

        private static readonly string[] SkippedFolders = { "node_modules", ".git", "_templates", "bin", "obj" };

        /// <summary>True for a file name that can hold a script's extend blocks (and isn't a test).</summary>
        public static bool IsExtensionFileName(string path)
        {
            string name = Path.GetFileName(path ?? string.Empty);
            return KScript.Syntax.Markup.ScriptFiles.IsScript(name) && !TestSettings.IsTestFileName(name);
        }

        /// <summary>
        /// The project's extension files, full paths in a stable order: every script under each folder of
        /// <see cref="Extensions"/>, and every project file a glob there matches.
        /// </summary>
        public List<string> ExtensionFiles(Func<string, bool> directoryExists = null, Func<string, bool> fileExists = null,
            Func<string, IEnumerable<string>> listFiles = null, Func<string, IEnumerable<string>> listDirectories = null) =>
            FilesIn(Extensions ?? new List<string> { DefaultExtensionsFolder }, directoryExists, fileExists, listFiles, listDirectories);

        private List<string> FilesIn(List<string> entries, Func<string, bool> directoryExists, Func<string, bool> fileExists,
            Func<string, IEnumerable<string>> listFiles, Func<string, IEnumerable<string>> listDirectories)
        {
            directoryExists ??= System.IO.Directory.Exists;
            fileExists ??= File.Exists;
            listFiles ??= System.IO.Directory.EnumerateFiles;
            listDirectories ??= System.IO.Directory.EnumerateDirectories;
            var found = new List<string>();
            if (Directory == null || !directoryExists(Directory)) return found;

            IEnumerable<string> ScriptsUnder(string folder)
            {
                var files = new List<string>();
                var pending = new Stack<string>();
                pending.Push(folder);
                while (pending.Count > 0)
                {
                    string dir = pending.Pop();
                    try
                    {
                        files.AddRange(listFiles(dir).Where(IsExtensionFileName));
                        foreach (string sub in listDirectories(dir))
                        {
                            if (!SkippedFolders.Contains(Path.GetFileName(sub.TrimEnd('/', '\\')), StringComparer.OrdinalIgnoreCase)) pending.Push(sub);
                        }
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        // A folder that can't be read has no extensions to give.
                    }
                }
                return files.Select(Path.GetFullPath).OrderBy(f => f, StringComparer.Ordinal);
            }

            foreach (string entry in entries)
            {
                string clean = entry.Trim().Replace('\\', '/').TrimEnd('/');
                if (clean.StartsWith("./", StringComparison.Ordinal)) clean = clean.Substring(2);
                if (clean.Length == 0) continue;
                string full = Path.GetFullPath(Path.Combine(Directory, clean));
                if (directoryExists(full))
                {
                    found.AddRange(ScriptsUnder(full));
                }
                else if (clean.IndexOfAny(new[] { '*', '?' }) >= 0)
                {
                    var whole = new Regex("^" + PathRegex(clean) + "$", OperatingSystem.IsLinux() ? RegexOptions.None : RegexOptions.IgnoreCase);
                    found.AddRange(ScriptsUnder(Directory).Where(f => whole.IsMatch(Path.GetRelativePath(Directory, f).Replace('\\', '/'))));
                }
                else if (fileExists(full))
                {
                    found.Add(full);
                }
            }
            return found.Distinct(StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// The "types" section: defaults for built-in types, used wherever the type is used without arguments of its
        /// own, such as <c>"uuid": { "v": 4 }</c> or <c>"password": { "min": 12 }</c>. Arguments where a type is used
        /// (<c>password(min: 8)</c>) replace these.
        /// </summary>
        public Dictionary<string, Dictionary<string, object>> Types { get; } = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);

        private static void ReadTypes(JsonElement e, ProjectConfig config)
        {
            if (e.ValueKind != JsonValueKind.Object)
            {
                config.Warnings.Add("types should be an object, such as \"types\": { \"uuid\": { \"v\": 4 } }.");
                return;
            }
            foreach (JsonProperty type in e.EnumerateObject())
            {
                string name = type.Name.Trim().ToLowerInvariant();
                if (!KScript.Runtime.Values.IsDeclarable(name) || name is "any" or "fn")
                {
                    config.Warnings.Add($"types: '{type.Name}' isn't a built-in type. Defaults can be set for: {string.Join(", ", KScript.Runtime.Values.DeclarableTypes.Where(t => t is not ("any" or "fn")))}.");
                    continue;
                }
                if (type.Value.ValueKind != JsonValueKind.Object)
                {
                    config.Warnings.Add($"types.{type.Name} should be an object of rules, such as {{ \"min\": 12 }}.");
                    continue;
                }
                var options = (Dictionary<string, object>)KScript.Runtime.Values.FromJson(type.Value);
                KScript.Runtime.TypeRules rules = KScript.Runtime.TypeRules.FromOptions(options, out string error);
                string problem = error ?? rules.ProblemFor(name, $"types.{name}");
                if (problem != null)
                {
                    config.Warnings.Add($"types.{type.Name}: {problem}");
                    continue;
                }
                config.Types[name] = options;
            }
        }

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
                        case "libraries":
                            config.Libraries = LibraryDependency.ReadAll(doc.RootElement);
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
                        case "library":
                            config.LibraryFolder = LibraryManifest.ReferencedFolder(doc.RootElement);
                            if (config.LibraryFolder == null) config.Library = LibraryManifest.Read(doc.RootElement);
                            break;
                        case "make":
                            ReadMake(prop.Value, config);
                            break;
                        case "http":
                            ReadHttp(prop.Value, config);
                            break;
                        case "queue":
                            ReadQueue(prop.Value, config);
                            break;
                        case "types":
                            ReadTypes(prop.Value, config);
                            break;
                        case "paths":
                            ReadPaths(prop.Value, config);
                            break;
                        case "extensions":
                            config.Extensions = Strings(prop.Value, "extensions", config) ?? config.Extensions;
                            break;
                        case "natives":
                            config.Natives = Strings(prop.Value, "natives", config) ?? config.Natives;
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
                            config.Warnings.Add($"unknown setting '{prop.Name}' (expected name, standard, packages, extensions, paths, types, serve, http, queue, lint, test, config, format, make, app, library or libraries).");
                            break;
                    }
                }
            }
            return config;
        }

        internal static JsonDocument ParseJson(string json, string full)
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
                    case "syntax":
                        string format = String(p.Value, "make.syntax", config);
                        if (format == MakeSettings.KScriptFormat || format == MakeSettings.XmlFormat) config.Make.Format = format;
                        else if (format != null) config.Warnings.Add($"make.syntax should be \"kscript\" or \"xml\", not \"{format}\".");
                        break;
                    default:
                        RefuseRetired("make." + p.Name);
                        config.Warnings.Add($"unknown setting 'make.{p.Name}' (expected syntax).");
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

        private static void ReadQueue(JsonElement e, ProjectConfig config)
        {
            if (!Object(e, "queue", config)) return;
            QueueSettings queue = config.Queue;
            foreach (var p in e.EnumerateObject())
            {
                string name = "queue." + p.Name;
                switch (p.Name)
                {
                    case "default":
                        queue.Default = String(p.Value, name, config) ?? queue.Default;
                        break;
                    case "queues":
                        List<string> names = p.Value.ValueKind == JsonValueKind.Array && p.Value.EnumerateArray().All(i => i.ValueKind == JsonValueKind.String)
                            ? p.Value.EnumerateArray().Select(i => i.GetString().Trim()).Where(q => q.Length > 0).ToList() : null;
                        if (names == null || names.Count == 0) config.Warnings.Add($"{name} should be a list of queue names, first checked first, e.g. [\"high\", \"default\"].");
                        else
                        {
                            queue.Queues = names;
                            queue.QueuesListed = true;
                        }
                        break;
                    case "connections":
                        if (!Object(p.Value, name, config)) break;
                        foreach (var c in p.Value.EnumerateObject())
                        {
                            string where = $"{name}.{c.Name}";
                            if (!Object(c.Value, where, config)) continue;
                            var connection = new QueueSettings.Connection { Name = c.Name };
                            foreach (var o in c.Value.EnumerateObject())
                            {
                                switch (o.Name)
                                {
                                    case "driver": connection.Driver = String(o.Value, where + ".driver", config)?.Trim().ToLowerInvariant(); break;
                                    case "path": connection.Path = String(o.Value, where + ".path", config); break;
                                    case "connection": connection.Database = String(o.Value, where + ".connection", config); break;
                                    case "url_env": connection.UrlEnv = String(o.Value, where + ".url_env", config); break;
                                    default: config.Warnings.Add($"unknown setting '{where}.{o.Name}' (expected driver, path, connection or url_env)."); break;
                                }
                            }
                            if (connection.Driver == null)
                            {
                                config.Warnings.Add($"{where} needs a driver: {string.Join(", ", QueueSettings.Drivers)}.");
                                continue;
                            }
                            if (!QueueSettings.Drivers.Contains(connection.Driver))
                            {
                                config.Warnings.Add(connection.Driver switch
                                {
                                    "redis" => $"{where}: the redis driver isn't available yet. Drivers: {string.Join(", ", QueueSettings.Drivers)}.",
                                    "sqlite" => $"{where}: use the database driver, with \"connection\" naming a sqlite database in database.connections.",
                                    _ => $"{where}: there is no queue driver '{connection.Driver}'. Drivers: {string.Join(", ", QueueSettings.Drivers)}.",
                                });
                                continue;
                            }
                            queue.Connections[c.Name] = connection;
                        }
                        break;
                    case "failed":
                        if (!Object(p.Value, name, config)) break;
                        foreach (var f in p.Value.EnumerateObject())
                        {
                            if (f.Name == "keep") queue.FailedKeep = Duration(f.Value, name + ".keep", config) ?? queue.FailedKeep;
                            else config.Warnings.Add($"unknown setting '{name}.{f.Name}' (expected keep).");
                        }
                        break;
                    case "worker":
                        if (!Object(p.Value, name, config)) break;
                        foreach (var w in p.Value.EnumerateObject())
                        {
                            switch (w.Name)
                            {
                                case "concurrency":
                                    if (w.Value.ValueKind == JsonValueKind.Number && w.Value.TryGetInt32(out int n) && n >= 1 && n <= 64) queue.WorkerConcurrency = n;
                                    else config.Warnings.Add($"{name}.concurrency should be a whole number from 1 to 64.");
                                    break;
                                case "sleep":
                                    queue.WorkerSleep = Duration(w.Value, name + ".sleep", config) ?? queue.WorkerSleep;
                                    break;
                                default:
                                    config.Warnings.Add($"unknown setting '{name}.{w.Name}' (expected concurrency or sleep).");
                                    break;
                            }
                        }
                        break;
                    case "env":
                        if (!Object(p.Value, name, config)) break;
                        foreach (var env in p.Value.EnumerateObject())
                        {
                            if (!Object(env.Value, $"{name}.{env.Name}", config)) continue;
                            foreach (var o in env.Value.EnumerateObject())
                            {
                                if (o.Name == "default" && String(o.Value, $"{name}.{env.Name}.default", config) is string d) queue.DefaultByEnvironment[env.Name] = d;
                                else if (o.Name != "default") config.Warnings.Add($"unknown setting '{name}.{env.Name}.{o.Name}' (expected default).");
                            }
                        }
                        break;
                    default:
                        config.Warnings.Add($"unknown setting '{name}' (expected {string.Join(", ", QueueSettings.SettingNames)}).");
                        break;
                }
            }
            if (!queue.Connections.ContainsKey(queue.Default))
                config.Warnings.Add($"queue.default names the connection '{queue.Default}', which isn't in queue.connections.");
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
                            config.Warnings.Add("http.routes should be a list of route files, e.g. [\"routes.kscript\", { \"file\": \"api.kscript\", \"prefix\": \"/api\", \"api\": true }].");
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
                        foreach (var a in p.Value.EnumerateObject()) ReadAuth(a, http, config);
                        CheckPasskeySettings(http.Auth, config);
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
                    case "openapi":
                        ReadOpenApi(p.Value, http.OpenApi, config);
                        break;
                    default:
                        config.Warnings.Add($"unknown setting 'http.{p.Name}' (expected routes, middleware, groups, views, api_prefix, write_files, cors, auth, throttle or openapi).");
                        break;
                }
            }
        }

        private static void ReadOpenApi(JsonElement e, OpenApiSettings openApi, ProjectConfig config)
        {
            if (e.ValueKind == JsonValueKind.True || e.ValueKind == JsonValueKind.False)
            {
                openApi.Enabled = e.ValueKind == JsonValueKind.True;
                return;
            }
            if (!Object(e, "http.openapi", config)) return;
            foreach (var o in e.EnumerateObject())
            {
                switch (o.Name)
                {
                    case "enabled": openApi.Enabled = Bool(o.Value, "http.openapi.enabled", config) ?? openApi.Enabled; break;
                    case "title": openApi.Title = String(o.Value, "http.openapi.title", config) ?? openApi.Title; break;
                    case "version": openApi.Version = String(o.Value, "http.openapi.version", config) ?? openApi.Version; break;
                    case "description": openApi.Description = String(o.Value, "http.openapi.description", config) ?? openApi.Description; break;
                    case "file": openApi.File = String(o.Value, "http.openapi.file", config) ?? openApi.File; break;
                    case "routes":
                        string routes = String(o.Value, "http.openapi.routes", config);
                        if (routes != null && OpenApiSettings.RouteChoices.Contains(routes)) openApi.Routes = routes;
                        else if (routes != null) config.Warnings.Add($"http.openapi.routes should be {string.Join(", ", OpenApiSettings.RouteChoices)}, not '{routes}'.");
                        break;
                    default:
                        config.Warnings.Add($"unknown setting 'http.openapi.{o.Name}' (expected enabled, title, version, description, routes or file).");
                        break;
                }
            }
        }

        private static void CheckPasskeySettings(AuthSettings auth, ProjectConfig config)
        {
            if (auth.PasskeyOrigin == null)
            {
                if (auth.PasskeyRpId != null) config.Warnings.Add("http.auth.passkey_rp_id needs http.auth.passkey_origin.");
                return;
            }
            if (!Uri.TryCreate(auth.PasskeyOrigin, UriKind.Absolute, out var origin) ||
                !(origin.Scheme == "https" || (origin.Scheme == "http" && origin.Host == "localhost")) ||
                origin.UserInfo.Length > 0 || origin.HostNameType != UriHostNameType.Dns ||
                origin.AbsolutePath != "/" || origin.Query.Length > 0 || origin.Fragment.Length > 0)
            {
                config.Warnings.Add("http.auth.passkey_origin must be an HTTPS origin such as https://accounts.example.com, or http://localhost with an optional port.");
                auth.PasskeyOrigin = null;
                return;
            }
            auth.PasskeyOrigin = origin.GetLeftPart(UriPartial.Authority);
            if (auth.PasskeyRpId == null) return;
            string rp = auth.PasskeyRpId.ToLowerInvariant();
            if (Uri.CheckHostName(rp) != UriHostNameType.Dns ||
                !(origin.Host == rp || (rp.Contains('.') && origin.Host.EndsWith("." + rp, StringComparison.Ordinal))))
            {
                config.Warnings.Add("http.auth.passkey_rp_id must be the origin's domain or a parent domain.");
                auth.PasskeyOrigin = null;
                return;
            }
            auth.PasskeyRpId = rp;
        }

        private static void ReadAuth(JsonProperty a, HttpSettings http, ProjectConfig config)
        {
            AuthSettings auth = http.Auth;
            string name = "http.auth." + a.Name;
            switch (a.Name)
            {
                case "token_env": http.AuthTokenEnv = String(a.Value, name, config) ?? http.AuthTokenEnv; break;
                case "login": auth.Login = String(a.Value, name, config) ?? auth.Login; break;
                case "home": auth.Home = String(a.Value, name, config) ?? auth.Home; break;
                case "folder": auth.Folder = String(a.Value, name, config) ?? auth.Folder; break;
                case "users": auth.Users = String(a.Value, name, config) ?? auth.Users; break;
                case "passkey_origin": auth.PasskeyOrigin = String(a.Value, name, config); break;
                case "passkey_signup": auth.PasskeySignup = Bool(a.Value, name, config) ?? false; break;
                case "passkey_rp_id": auth.PasskeyRpId = String(a.Value, name, config); break;
                case "cookie": auth.Cookie = String(a.Value, name, config) ?? auth.Cookie; break;
                case "secure": auth.Secure = Bool(a.Value, name, config) ?? auth.Secure; break;
                case "csrf": auth.Csrf = Bool(a.Value, name, config) ?? auth.Csrf; break;
                case "sessions":
                    string kind = String(a.Value, name, config);
                    if (kind == "file" || kind == "memory") auth.Sessions = kind;
                    else if (kind != null) config.Warnings.Add($"{name} should be file or memory.");
                    break;
                case "same_site":
                    string site = String(a.Value, name, config);
                    string known = AuthSettings.SameSiteValues.FirstOrDefault(v => string.Equals(v, site, StringComparison.OrdinalIgnoreCase));
                    if (known != null) auth.SameSite = known;
                    else if (site != null) config.Warnings.Add($"{name} should be Lax, Strict or None.");
                    break;
                case "attempts":
                    if (a.Value.ValueKind == JsonValueKind.Number && a.Value.TryGetInt32(out int attempts) && attempts > 0) auth.Attempts = attempts;
                    else config.Warnings.Add($"{name} should be a whole number above 0.");
                    break;
                case "lifetime": auth.Lifetime = Duration(a.Value, name, config) ?? auth.Lifetime; break;
                case "remember": auth.Remember = Duration(a.Value, name, config) ?? auth.Remember; break;
                case "lockout": auth.Lockout = Duration(a.Value, name, config) ?? auth.Lockout; break;
                case "reset_lifetime": auth.ResetLifetime = Duration(a.Value, name, config) ?? auth.ResetLifetime; break;
                default:
                    config.Warnings.Add($"unknown setting '{name}' (expected {string.Join(", ", AuthSettings.SettingNames)}).");
                    break;
            }
        }

        /// <summary>A length of time in seconds: a number of seconds, or text such as "90s", "15m", "2h", "30d" or "1w".</summary>
        private static double? Duration(JsonElement e, string name, ProjectConfig config)
        {
            if (e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out double n) && n > 0) return n;
            if (e.ValueKind == JsonValueKind.String && Seconds(e.GetString()) is double seconds) return seconds;
            config.Warnings.Add($"{name} should be a number of seconds, or text such as \"15m\", \"2h\" or \"30d\".");
            return null;
        }

        /// <summary>Seconds in text such as "90", "90s", "15m", "2h", "30d" or "1w"; null if it isn't one.</summary>
        public static double? Seconds(string text)
        {
            System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(text ?? string.Empty, @"^\s*(\d+(?:\.\d+)?)\s*(s|m|h|d|w)?\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            double amount = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            double seconds = amount * (m.Groups[2].Value.ToLowerInvariant() switch { "m" => 60, "h" => 3600, "d" => 86400, "w" => 604800, _ => 1 });
            return seconds > 0 ? seconds : null;
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
                    case "docs":
                        config.Serve.Docs = Bool(p.Value, "serve.docs", config) ?? false;
                        break;
                    case "docs_port":
                        if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out int docsPort) && docsPort > 0 && docsPort < 65536) config.Serve.DocsPort = docsPort;
                        else config.Warnings.Add($"serve.docs_port should be a number from 1 to 65535.");
                        break;
                    case "docker":
                        ReadDockerServe(p.Value, config);
                        break;
                    default:
                        config.Warnings.Add($"unknown setting 'serve.{p.Name}' (expected root, port, host, docs, docs_port or docker).");
                        break;
                }
            }
        }

        private static void ReadDockerServe(JsonElement value, ProjectConfig config)
        {
            var docker = config.Serve.Docker;
            if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                docker.Enabled = value.GetBoolean();
                return;
            }
            if (!Object(value, "serve.docker", config)) return;
            docker.Enabled = true;
            foreach (var property in value.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "enabled":
                        docker.Enabled = Bool(property.Value, "serve.docker.enabled", config) ?? false;
                        break;
                    case "build":
                        docker.Build = Bool(property.Value, "serve.docker.build", config) ?? true;
                        break;
                    case "compose_file":
                        docker.ComposeFile = String(property.Value, "serve.docker.compose_file", config);
                        break;
                    case "service":
                        docker.Service = String(property.Value, "serve.docker.service", config) ?? "app";
                        break;
                    case "mount":
                        docker.Mount = property.Value.ValueKind == JsonValueKind.Null
                            ? null : String(property.Value, "serve.docker.mount", config) ?? "/app";
                        break;
                    default:
                        config.Warnings.Add($"unknown setting 'serve.docker.{property.Name}' (expected enabled, build, compose_file, service or mount).");
                        break;
                }
            }
        }

        /// <summary>
        /// Settings that were renamed (kscript.json keys are snake_case). An old name is an error that says what to
        /// write instead, never a second spelling.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> RetiredSettings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["lint.warningsAsErrors"] = "lint.warnings_as_errors",
            ["format.tabWidth"] = "format.tab_width",
            ["format.printWidth"] = "format.print_width",
            ["format.maxBlankLines"] = "format.max_blank_lines",
            ["format.endOfLine"] = "format.end_of_line",
            ["format.rules.statementPerLine"] = "format.rules.statement_per_line",
            ["format.rules.tidyTags"] = "format.rules.tidy_tags",
            ["format.rules.wrapAttributes"] = "format.rules.wrap_attributes",
            ["format.rules.trimTrailingWhitespace"] = "format.rules.trim_trailing_whitespace",
            ["format.rules.collapseBlankLines"] = "format.rules.collapse_blank_lines",
            ["format.rules.finalNewline"] = "format.rules.final_newline",
            ["format.rules.braceStyle"] = "format.rules.brace_style",
            ["format.rules.wrapStrings"] = "format.rules.wrap_strings",
            ["format.rules.wrapChains"] = "format.rules.wrap_chains",
            ["format.rules.wrapBodies"] = "format.rules.wrap_bodies",
            ["format.rules.wrapComments"] = "format.rules.wrap_comments",
            ["make.format"] = "make.syntax",
        };

        /// <summary>Fails with "x was renamed to y" when <paramref name="setting"/> is an old name.</summary>
        private static void RefuseRetired(string setting)
        {
            if (RetiredSettings.TryGetValue(setting, out string now))
                throw new ProjectConfigException($"'{setting}' was renamed to '{now}'. Settings in kscript.json are written in snake_case.");
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
                    case "warnings_as_errors":
                        if (p.Value.ValueKind == JsonValueKind.True || p.Value.ValueKind == JsonValueKind.False)
                        {
                            config.Lint.WarningsAsErrors = p.Value.GetBoolean();
                            config.Set("lint.warnings_as_errors");
                        }
                        else config.Warnings.Add($"lint.warnings_as_errors should be true or false.");
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
                        RefuseRetired("lint." + p.Name);
                        config.Warnings.Add($"unknown setting 'lint.{p.Name}' (expected include, exclude, rules or warnings_as_errors).");
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
                    case "env":
                        if (p.Value.ValueKind == JsonValueKind.String && p.Value.GetString().Trim().Length > 0) config.Test.Env = p.Value.GetString().Trim();
                        else config.Warnings.Add("test.env should be the name of an environment, such as \"test\".");
                        break;
                    case "database":
                        string database = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString().Trim() : null;
                        if (TestSettings.DatabaseChoices.Contains(database)) config.Test.Database = database;
                        else config.Warnings.Add("test.database should be memory or config.");
                        break;
                    default:
                        config.Warnings.Add($"unknown setting 'test.{p.Name}' (expected include, exclude, timeout, packages, env or database).");
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
                    case "tab_width":
                        if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out int tab) && tab >= 1 && tab <= 16) { config.Format.IndentSize = tab; config.Set("format.tab_width"); }
                        else config.Warnings.Add("format.tab_width should be a number from 1 to 16.");
                        break;
                    case "print_width":
                        if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out int width) && width >= 20) { config.Format.PrintWidth = width; config.Set("format.print_width"); }
                        else config.Warnings.Add("format.print_width should be a number of columns (20 or more).");
                        break;
                    case "max_blank_lines":
                        if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out int blank) && blank >= 0) { config.Format.MaxBlankLines = blank; config.Set("format.max_blank_lines"); }
                        else config.Warnings.Add("format.max_blank_lines should be 0 or more.");
                        break;
                    case "end_of_line":
                        string eol = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString().Trim().ToLowerInvariant() : null;
                        if (eol == "auto" || eol == "lf" || eol == "crlf") { config.Format.EndOfLine = eol; config.Set("format.end_of_line"); }
                        else config.Warnings.Add("format.end_of_line should be \"auto\", \"lf\" or \"crlf\".");
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
                            if (known == null) RefuseRetired("format.rules." + rule.Name);
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
                        RefuseRetired("format." + p.Name);
                        config.Warnings.Add($"unknown setting 'format.{p.Name}' (expected enabled, rules, indent, tab_width, print_width, max_blank_lines, end_of_line, preserve, include or exclude).");
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

        /// <summary>
        /// True when the file is under the project folder and matches lint.exclude, or is one kscript generates
        /// (.kscript/, AGENTS.md, CLAUDE.md): the editor and <c>kscript check</c> report nothing for it.
        /// </summary>
        public bool IsExcluded(string path)
        {
            string rel = Relative(path);
            return rel != null && (IsGenerated(rel) || Lint.Exclude.Any(g => GlobMatches(g, rel)));
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
