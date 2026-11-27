using KScript.Language.Catalog;
using KScript.Projects;
using KScript.Syntax.Markup;

namespace KScript.Language.Analysis
{
    /// <summary>What the rest of a project uses from one script: the names other files call, and how many import it.</summary>
    public sealed class ProjectUses
    {
        /// <summary>Functions and constants of the script that another file in the project uses.</summary>
        public HashSet<string> Names { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>The files in the project that import the script.</summary>
        public List<string> Importers { get; } = new List<string>();
    }

    /// <summary>
    /// Finds the files in a project that use a script's functions, so that a function only another file calls (a
    /// library function, a controller named in a route file) doesn't count as unused. The project is the folder with
    /// the nearest kscript.json and everything under it; a script outside any project is checked against the other
    /// files in its own folder. Files that can't mention the script by name are skipped without being analysed, and
    /// the uses found in each file are kept until its text (or the script's) changes.
    /// </summary>
    public static class ProjectUsage
    {
        /// <summary>The most files looked at in one project, so a huge folder can't stall the editor.</summary>
        internal static int MaxFiles = 2000;

        private static readonly string[] Extensions = { ".kscript", ".ks.xml", ".html", ".htm", ".md" };

        private static readonly HashSet<string> SkippedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bin", "obj", "node_modules" };

        internal static int CacheLimit = 512;

        private static readonly object Gate = new object();

        // By importer path: the texts its uses were worked out from, and what it uses from where.
        private static readonly Dictionary<string, (string Text, string TargetText, HashSet<(string Path, string Name)> Uses, HashSet<string> Imports)> Cache =
            new Dictionary<string, (string, string, HashSet<(string, string)>, HashSet<string>)>(StringComparer.Ordinal);

        /// <summary>
        /// What the other files of <paramref name="path"/>'s project use from it. <paramref name="text"/> is its
        /// current text (which may not be saved yet).
        /// </summary>
        public static ProjectUses Find(string path, string text, IFileSystem files, LanguageCatalog catalog = null)
        {
            ProjectUses found = new ProjectUses();
            if (string.IsNullOrEmpty(path))
            {
                return found;
            }
            files ??= DiskFileSystem.Instance;
            string target = Path.GetFullPath(path);
            string folder = Path.GetDirectoryName(target);
            string root = ProjectRoot(folder, files);
            string stem = Path.GetFileName(target).Split('.')[0];

            var texts = new List<(string File, string Text)>();
            foreach (string file in ProjectFiles(root ?? folder, recursive: root != null, files))
            {
                if (!SamePath(file, target) && files.ReadText(file) is string other)
                {
                    texts.Add((file, other));
                }
            }
            HashSet<string> reaching = Reaching(texts, stem);

            foreach ((string file, string other) in texts)
            {
                // Component templates import their companion automatically and may never spell its filename.
                bool companion = MarkupParser.KindFromPath(file) != DocumentKind.Script
                    && SamePath(TemplateContracts.CompanionFile(files, file), target);
                if (!companion && !reaching.Contains(file))
                {
                    continue;
                }

                var (uses, imports) = UsesOf(file, other, target, text, files, catalog);
                if (companion || imports.Any(i => SamePath(i, target)))
                {
                    found.Importers.Add(file);
                }
                foreach ((string usedPath, string name) in uses)
                {
                    if (SamePath(usedPath, target))
                    {
                        found.Names.Add(name);
                    }
                }
            }
            // Scripts first, then pages, so a message names a script that imports it.
            List<string> scripts = found.Importers.Where(f => MarkupParser.KindFromPath(f) == DocumentKind.Script).ToList();
            List<string> pages = found.Importers.Except(scripts).ToList();
            found.Importers.Clear();
            found.Importers.AddRange(scripts.Concat(pages));
            return found;
        }

        /// <summary>
        /// The files that may use the script: those that mention its name, and those that mention a file that does, and
        /// so on. A value's type can come from a script the caller never names: main imports lib/zoo, whose make()
        /// returns a Zebra from types/zebra, so main's ->shown() is a use of zebra's method. Only files found this way are
        /// analysed, so the search stays a text scan for the rest.
        /// </summary>
        private static HashSet<string> Reaching(List<(string File, string Text)> texts, string stem)
        {
            var reaching = new HashSet<string>(StringComparer.Ordinal);
            var stems = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { stem };
            var pending = new Queue<string>();
            pending.Enqueue(stem);
            while (pending.Count > 0)
            {
                string name = pending.Dequeue();
                foreach ((string file, string text) in texts)
                {
                    if (reaching.Contains(file) || text.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }
                    reaching.Add(file);
                    string next = Path.GetFileName(file).Split('.')[0];
                    if (next.Length > 0 && stems.Add(next))
                    {
                        pending.Enqueue(next);
                    }
                }
            }
            return reaching;
        }

        /// <summary>Forgets every file's cached uses (for tests).</summary>
        internal static void ClearCache()
        {
            lock (Gate)
            {
                Cache.Clear();
            }
        }

        private static (HashSet<(string Path, string Name)> Uses, HashSet<string> Imports) UsesOf(string file, string text, string target, string targetText, IFileSystem files, LanguageCatalog catalog)
        {
            string key = Path.GetFullPath(file);
            lock (Gate)
            {
                if (Cache.TryGetValue(key, out var cached) && cached.Text == text && cached.TargetText == targetText)
                {
                    return (cached.Uses, cached.Imports);
                }
            }

            DocumentAnalysis analysis = Analyzer.Analyze(text, file, files, catalog, kind: null, projectWide: false);
            HashSet<string> imports = new HashSet<string>(analysis.Links.Where(l => l.Kind == "import").Select(l => l.TargetPath), StringComparer.Ordinal);
            lock (Gate)
            {
                if (Cache.Count >= CacheLimit)
                {
                    Cache.Clear();
                }
                Cache[key] = (text, targetText, analysis.ExternalUses, imports);
            }
            return (analysis.ExternalUses, imports);
        }

        /// <summary>The KScript files in a folder and the folders under it, for searching a whole workspace.</summary>
        public static IEnumerable<string> FilesUnder(string folder, IFileSystem files) => ProjectFiles(folder, recursive: true, files ?? DiskFileSystem.Instance);

        /// <summary>The folder holding the nearest kscript.json at or above <paramref name="folder"/>, or null.</summary>
        internal static string ProjectRoot(string folder, IFileSystem files)
        {
            for (string dir = folder; !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
            {
                if (files.FileExists(Path.Combine(dir, ProjectConfig.FileName)))
                {
                    return dir;
                }
            }
            return null;
        }

        /// <summary>The KScript files of a folder (and, for a project, the folders under it, except hidden ones, bin, obj and node_modules).</summary>
        internal static IEnumerable<string> ProjectFiles(string folder, bool recursive, IFileSystem files)
        {
            int count = 0;
            Stack<string> pending = new Stack<string>();
            pending.Push(folder);
            while (pending.Count > 0)
            {
                string dir = pending.Pop();
                foreach (string file in files.ListFiles(dir, "*").OrderBy(f => f, StringComparer.Ordinal))
                {
                    if (!Extensions.Any(e => file.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }
                    if (++count > MaxFiles)
                    {
                        yield break;
                    }
                    yield return file;
                }
                if (!recursive)
                {
                    continue;
                }
                foreach (string sub in files.ListDirectories(dir).OrderByDescending(d => d, StringComparer.Ordinal))
                {
                    string name = Path.GetFileName(sub);
                    if (name.StartsWith(".", StringComparison.Ordinal) || SkippedFolders.Contains(name))
                    {
                        continue;
                    }
                    pending.Push(sub);
                }
            }
        }

        private static bool SamePath(string a, string b) =>
            a != null && b != null && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
