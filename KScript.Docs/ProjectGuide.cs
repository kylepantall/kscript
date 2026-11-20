using System.Text;

using KScript.Language.Analysis;
using KScript.Projects;
using KScript.Runtime;
using KScript.Syntax.Markup;

namespace KScript.Docs
{
    /// <summary>
    /// What a project has written for itself, for the docs' project page (<c>kscript serve --with-docs</c>): each
    /// script's functions, types, enums and constants with their doc comments, the site's routes, and the project's
    /// own conventions (its format, standard and folders).
    /// </summary>
    public sealed class ProjectGuide
    {
        /// <summary>The most scripts read; a bigger project lists the first ones and says so.</summary>
        public static int MaxFiles { get; internal set; } = 400;

        public sealed record Declaration(string Kind, string Name, string Signature, string Summary, DocComment Doc, string Xml, int Line);

        public sealed record ScriptFile(string Path, string Format, IReadOnlyList<Declaration> Declarations);

        public sealed record RouteRow(string Methods, string Path, string Name, string Target, string Middleware, string File);

        /// <summary>The project folder (or the site folder, without a kscript.json).</summary>
        public string Directory { get; private init; }

        public string Name { get; private init; }

        /// <summary>The project's kscript.json, or null.</summary>
        public ProjectConfig Project { get; private init; }

        /// <summary>"kscript" or "xml": the project's make.format, else .kscript.</summary>
        public string Format { get; private init; }

        public List<ScriptFile> Files { get; } = new List<ScriptFile>();

        public List<RouteRow> Routes { get; } = new List<RouteRow>();

        /// <summary>Why the routes couldn't be read, or null.</summary>
        public string RoutesError { get; private set; }

        /// <summary>True when the project has more scripts than <see cref="MaxFiles"/>.</summary>
        public bool Truncated { get; private set; }

        /// <summary>How many scripts there were and when the newest changed, when it was read: see <see cref="Changes"/>.</summary>
        public string Stamp { get; private init; }

        /// <summary>Reads a project: <paramref name="folder"/> is the project folder, or the site folder without a kscript.json.</summary>
        public static ProjectGuide Read(string folder, ProjectConfig project)
        {
            string directory = Path.GetFullPath(project?.Directory ?? folder);
            List<string> scripts = Scripts(directory, project).ToList();
            var guide = new ProjectGuide
            {
                Directory = directory,
                Project = project,
                Name = project?.Name ?? Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar)),
                Format = DocsCommand.NormalizeFormat(project?.Make.Format) ?? "kscript",
                Stamp = Changes(scripts, project),
            };
            guide.Truncated = scripts.Count > MaxFiles;
            foreach (string file in scripts.Take(MaxFiles))
            {
                ScriptFile script = ReadScript(file, directory);
                if (script.Declarations.Count > 0) guide.Files.Add(script);
            }
            guide.ReadRoutes(project?.SiteRoot ?? directory, project);
            return guide;
        }

        /// <summary>
        /// How many scripts the project has and when the newest of them (or kscript.json) changed. When it differs
        /// from <see cref="Stamp"/>, a script was added, removed or changed since the guide was read.
        /// </summary>
        public static string Changes(string folder, ProjectConfig project)
        {
            string directory = Path.GetFullPath(project?.Directory ?? folder);
            return Changes(Scripts(directory, project).ToList(), project);
        }

        private static string Changes(List<string> files, ProjectConfig project)
        {
            DateTime newest = project?.FilePath != null && File.Exists(project.FilePath) ? File.GetLastWriteTimeUtc(project.FilePath) : DateTime.MinValue;
            foreach (string file in files)
            {
                DateTime t = File.GetLastWriteTimeUtc(file);
                if (t > newest) newest = t;
            }
            return files.Count + ":" + newest.Ticks;
        }

        private static readonly string[] Skipped = { "node_modules", ".git", "_templates", "bin", "obj" };

        /// <summary>The project's scripts (not tests, pages or templates), in path order.</summary>
        internal static IEnumerable<string> Scripts(string directory, ProjectConfig project)
        {
            var found = new List<string>();
            if (!System.IO.Directory.Exists(directory)) return found;
            var folders = new Stack<string>();
            folders.Push(directory);
            while (folders.Count > 0 && found.Count <= MaxFiles)
            {
                string dir = folders.Pop();
                var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden };
                IEnumerable<string> files = System.IO.Directory.EnumerateFiles(dir, "*", options);
                IEnumerable<string> subfolders = System.IO.Directory.EnumerateDirectories(dir, "*", options);
                foreach (string file in files)
                {
                    if (!IsScript(file) || TestSettings.IsTestFileName(file)) continue;
                    if (project != null && project.IsExcluded(file)) continue;
                    found.Add(file);
                }
                foreach (string sub in subfolders.OrderByDescending(s => s, StringComparer.Ordinal))
                {
                    string name = Path.GetFileName(sub);
                    if (name.StartsWith(".", StringComparison.Ordinal) || Skipped.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                    if (project != null && project.IsExcluded(sub)) continue;
                    folders.Push(sub);
                }
            }
            return found.OrderBy(f => f, StringComparer.Ordinal);
        }

        internal static bool IsScript(string path)
        {
            string p = path.ToLowerInvariant();
            return p.EndsWith(".kscript") || p.EndsWith(".ks.xml") || p.EndsWith(".ks");
        }

        internal static ScriptFile ReadScript(string file, string directory)
        {
            string relative = Path.GetRelativePath(directory, file).Replace('\\', '/');
            string format = file.EndsWith(".kscript", StringComparison.OrdinalIgnoreCase) ? "kscript" : "xml";
            var declarations = new List<Declaration>();
            string text = DiskFileSystem.Instance.ReadText(file);
            if (text == null) return new ScriptFile(relative, format, declarations);

            DocumentAnalysis analysis = Analyzer.AnalyzeFile(text, file, DiskFileSystem.Instance);
            MarkupElement root = analysis.Markup.RootElements.FirstOrDefault();
            foreach (Symbol s in analysis.Symbols)
            {
                MarkupElement e = s.DeclaringElement;
                // Only what this file declares itself: not names it imports from another file.
                if (e == null || e.Parent != root || !Declares.Contains(e.Name) || analysis.ExternalDeclarations.ContainsKey(s)) continue;
                string kind = s.Kind == SymbolKind.Function ? "function"
                    : e.Name == "type" ? "type"
                    : e.Name == "enum" || s.EnumValues != null ? "enum"
                    : s.Kind == SymbolKind.Constant ? "constant"
                    : null;
                if (kind == null) continue;
                DocComment doc = DocComment.Parse(e.Documentation);
                string signature = kind == "function" ? s.TypedSignature : s.Name;
                string xml = kind == "function" ? null : ToXml(e);
                int line = analysis.Source.GetPosition(s.DeclarationSpan.Start).Line + 1;
                declarations.Add(new Declaration(kind, s.Name, signature, doc?.Summary, doc, xml, line));
            }
            declarations.Sort((a, b) => a.Line.CompareTo(b.Line));
            return new ScriptFile(relative, format, declarations);
        }

        private static readonly HashSet<string> Declares = new HashSet<string>(StringComparer.Ordinal) { "fn", "type", "enum", "const", "let" };

        private void ReadRoutes(string siteRoot, ProjectConfig project)
        {
            if (!System.IO.Directory.Exists(siteRoot)) return;
            try
            {
                var app = new KScript.Http.HttpApp(siteRoot, project);
                if (!app.HasRoutes) return;
                foreach (KScript.Http.Route r in app.LoadRoutes().Routes)
                {
                    Routes.Add(new RouteRow(
                        r.Methods.Count == 0 ? "ANY" : string.Join(" ", r.Methods),
                        r.Pattern.Path,
                        r.Name,
                        r.Target,
                        string.Join(", ", r.Middleware),
                        r.File != null ? Path.GetRelativePath(Directory, r.File).Replace('\\', '/') + ":" + r.Line : null));
                }
            }
            catch (Exception ex) when (ex is KScriptException or FileNotFoundException)
            {
                RoutesError = ex is KScriptException k && k.Location != null ? k.Location + ": " + k.Message : ex.Message;
                Routes.Clear();
            }
        }

        /// <summary>A declaration written back out as XML (the docs page shows it as .kscript too), without its doc comment.</summary>
        internal static string ToXml(MarkupElement e, int depth = 0)
        {
            var sb = new StringBuilder();
            string indent = new string(' ', depth * 2);
            sb.Append(indent).Append('<').Append(e.Name);
            foreach (MarkupAttribute a in e.Attributes)
            {
                if (a.Name == "description") continue;
                sb.Append(' ').Append(a.Name).Append("=\"").Append(Escape(a.ValueText ?? string.Empty)).Append('"');
            }
            List<MarkupElement> children = e.ChildElements.ToList();
            string inner = string.Concat(e.Children.OfType<MarkupText>().Select(t => t.Value)).Trim();
            if (children.Count == 0 && inner.Length == 0) return sb.Append(" />").ToString();
            sb.Append('>');
            if (children.Count == 0) return sb.Append(Escape(inner)).Append("</").Append(e.Name).Append('>').ToString();
            sb.Append('\n');
            foreach (MarkupElement child in children) sb.Append(ToXml(child, depth + 1)).Append('\n');
            return sb.Append(indent).Append("</").Append(e.Name).Append('>').ToString();
        }

        private static string Escape(string text) =>
            text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }
}
