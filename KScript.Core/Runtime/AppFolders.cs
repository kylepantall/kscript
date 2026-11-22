using KScript.Projects;

namespace KScript.Runtime
{
    /// <summary>
    /// Where an app keeps its files on each platform: settings, data, caches, state and logs, in the folders the
    /// operating system expects (Application Support and Caches on macOS, AppData on Windows, the XDG folders on
    /// Linux). KScript.Storage and KScript.Cache use these, and <c>config.user</c> reads the person's settings from
    /// the config folder.
    /// </summary>
    public static class AppFolders
    {
        /// <summary>The kinds of folder an app has.</summary>
        public static readonly string[] Kinds = { "config", "data", "cache", "state", "logs" };

        /// <summary>When set, every app's folders are under this folder instead (<c>&lt;root&gt;/&lt;app&gt;/&lt;kind&gt;</c>): handy for tests and portable installs.</summary>
        public const string RootVariable = "KSCRIPT_STORAGE_ROOT";

        /// <summary>Resolves a configured SQLite or auth data path, optionally under a private deployment volume.</summary>
        public static string ProjectDataPath(string project, string path)
        {
            if (Path.IsPathFullyQualified(path)) return Path.GetFullPath(path);
            string folder = Environment.GetEnvironmentVariable("KSCRIPT_PROJECT_DATA_ROOT");
            if (string.IsNullOrEmpty(folder)) return Path.GetFullPath(Path.Combine(project, path));
            if (!Path.IsPathFullyQualified(folder))
                throw new KScriptException("KSCRIPT_PROJECT_DATA_ROOT needs an absolute folder path.", "Config");
            string absolute = Path.GetFullPath(path, folder);
            string relative = Path.GetRelativePath(folder, absolute);
            if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative))
                throw new KScriptException($"The project data path '{path}' leaves KSCRIPT_PROJECT_DATA_ROOT.", "Config");
            return absolute;
        }

        /// <summary>The platform this process runs on: macos, windows or linux (other systems use the Linux folders).</summary>
        public static string CurrentPlatform => OperatingSystem.IsMacOS() ? "macos" : OperatingSystem.IsWindows() ? "windows" : "linux";

        /// <summary>The folder of one kind for an app on this computer. The folder isn't created.</summary>
        public static string Folder(string app, string kind) =>
            Folder(app, kind, CurrentPlatform, Environment.GetEnvironmentVariable, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

        /// <summary>The folder of one kind for an app on a platform, with its environment variables and home folder.</summary>
        public static string Folder(string app, string kind, string platform, Func<string, string> environment, string home)
        {
            string name = CleanName(app);
            kind = CheckKind(kind);
            if (Absolute(environment(RootVariable)) is string root) return Path.Combine(root, name, kind);

            switch (platform)
            {
                case "macos":
                {
                    string library = Path.Combine(home, "Library");
                    return kind switch
                    {
                        "cache" => Path.Combine(library, "Caches", name),
                        "logs" => Path.Combine(library, "Logs", name),
                        _ => Path.Combine(library, "Application Support", name),
                    };
                }
                case "windows":
                {
                    // Settings roam with the person; everything else stays on this computer.
                    string roaming = Absolute(environment("APPDATA")) ?? Path.Combine(home, "AppData", "Roaming");
                    string local = Absolute(environment("LOCALAPPDATA")) ?? Path.Combine(home, "AppData", "Local");
                    return kind switch
                    {
                        "config" => Path.Combine(roaming, name),
                        "data" => Path.Combine(local, name),
                        "cache" => Path.Combine(local, name, "Cache"),
                        "state" => Path.Combine(local, name, "State"),
                        _ => Path.Combine(local, name, "Logs"),
                    };
                }
                default:
                {
                    // The XDG base directories. Folder names are lower case with dashes: "Life Lab" is life-lab.
                    string slug = string.Join("-", name.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
                    string Xdg(string variable, params string[] fallback) => Absolute(environment(variable)) ?? Path.Combine(new[] { home }.Concat(fallback).ToArray());
                    return kind switch
                    {
                        "config" => Path.Combine(Xdg("XDG_CONFIG_HOME", ".config"), slug),
                        "data" => Path.Combine(Xdg("XDG_DATA_HOME", ".local", "share"), slug),
                        "cache" => Path.Combine(Xdg("XDG_CACHE_HOME", ".cache"), slug),
                        "state" => Path.Combine(Xdg("XDG_STATE_HOME", ".local", "state"), slug),
                        _ => Path.Combine(Xdg("XDG_STATE_HOME", ".local", "state"), slug, "logs"),
                    };
                }
            }
        }

        /// <summary>
        /// The app a script belongs to: <c>app.name</c> (else <c>name</c>) in the nearest kscript.json, else the
        /// script's name without its extension (notes.ks.xml is notes), else kscript.
        /// </summary>
        public static string AppNameFor(string directory, string scriptPath)
        {
            ProjectConfig project = null;
            try
            {
                project = directory == null ? null : ProjectConfig.FindAndLoad(directory);
            }
            catch (ProjectConfigException)
            {
                // A broken kscript.json is reported by the commands that use it; the script's name will do here.
            }
            if ((project?.App.Name ?? project?.Name) is string named && named.Trim().Length > 0) return named.Trim();
            if (string.IsNullOrEmpty(scriptPath)) return "kscript";
            string stem = Path.GetFileName(scriptPath);
            int dot = stem.IndexOf('.');
            return dot > 0 ? stem.Substring(0, dot) : stem;
        }

        /// <summary>An app name made safe for a folder name: separators and other characters files can't have become dashes.</summary>
        public static string CleanName(string app)
        {
            string name = new string((app ?? string.Empty).Trim().Select(c => Array.IndexOf(Unsafe, c) >= 0 || char.IsControl(c) ? '-' : c).ToArray()).Trim('.', ' ');
            if (name.Length == 0) throw new KScriptException($"'{app}' can't be used as an app name for its folders. Give a name such as 'Notes', or set app.name in kscript.json.", "Argument");
            return name;
        }

        private static readonly char[] Unsafe = { '/', '\\', ':', '*', '?', '"', '<', '>', '|' };

        /// <summary>A folder kind by name, or an error listing the kinds.</summary>
        public static string CheckKind(string kind)
        {
            string k = (kind ?? "data").Trim().ToLowerInvariant();
            if (Array.IndexOf(Kinds, k) < 0) throw new KScriptException($"'{kind}' isn't a kind of app folder. Use one of: {string.Join(", ", Kinds)}.", "Argument");
            return k;
        }

        private static string Absolute(string path) => !string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path) ? path : null;
    }
}
