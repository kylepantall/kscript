using System.Text;

using KScript.Language.Outputs;
using KScript.Projects;

namespace KScript.Docs
{
    /// <summary>
    /// The project's own docs as Markdown, in <c>.kscript/docs/project.md</c>: the same conventions, routes, functions,
    /// types and constants as the /project page of <c>kscript serve --with-docs</c>. Turned on by the folder
    /// existing (<c>kscript docs --project</c> makes it), then kept up to date by <c>kscript watch</c>,
    /// <c>kscript serve</c> and saving in the editor.
    /// </summary>
    public sealed class ProjectDocsOutput : IProjectOutput
    {
        public const string Folder = ".kscript/docs";

        public const string File = Folder + "/project.md";

        public string Name => "docs";

        public bool IsEnabled(string root) => root != null && Directory.Exists(Path.Combine(root, Folder));

        public bool Owns(string relative) => relative.StartsWith(Folder + "/", StringComparison.Ordinal);

        public IReadOnlyList<string> Write(string root, bool check)
        {
            ProjectConfig project = ProjectConfig.FindAndLoad(root);
            string markdown = Markdown(ProjectGuide.Read(root, project));
            string path = Path.Combine(root, File);
            string current = System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : null;
            if (current == markdown) return Array.Empty<string>();
            if (!check)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                System.IO.File.WriteAllText(path, markdown);
            }
            return new[] { File };
        }

        /// <summary>
        /// <c>kscript docs --project</c>: writes the docs of the project the folder is in, which turns them on, so
        /// kscript watch, kscript serve and the editor keep them up to date from then on.
        /// </summary>
        public static int Start(string folder, TextWriter output, TextWriter error)
        {
            string config = ProjectConfig.Find(Path.GetFullPath(folder));
            string root = config != null ? Path.GetDirectoryName(config) : Path.GetFullPath(folder);
            try
            {
                Directory.CreateDirectory(Path.Combine(root, Folder));
                bool changed = new ProjectDocsOutput().Write(root, check: false).Count > 0;
                output.WriteLine($"{(changed ? "Wrote" : "Up to date:")} {Path.Combine(root, File)}. kscript watch, kscript serve and saving in the editor keep it up to date; delete {Folder} to stop.");
                return 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ProjectConfigException)
            {
                KScript.Runtime.ErrorReport.Write(error, "Could not write the project's docs.", ex);
                return 1;
            }
        }

        /// <summary>The project's docs as Markdown, with code in the project's format. The same project gives the same text.</summary>
        public static string Markdown(ProjectGuide guide)
        {
            var sb = new StringBuilder();
            sb.Append($"# {guide.Name}\n\n");
            sb.Append("The docs of this project's own code, written by kscript from its scripts and kept up to date by `kscript watch`, `kscript serve` and saving in the editor. Don't edit it: change the `///` doc comments in the scripts instead. `kscript serve --with-docs` shows the same as a page, next to the KScript docs.\n\n");

            sb.Append("## How code is written here\n\n");
            ProjectConfig project = guide.Project;
            sb.Append($"- New scripts, routes and tests are `{(guide.Format == "xml" ? ".ks.xml" : ".kscript")}` files.\n");
            if (project != null)
            {
                sb.Append($"- Standard: {string.Join(", ", project.Standard.Select(s => $"`{s}`"))}.\n");
                if (project.Lint.Rules.Count > 0)
                    sb.Append($"- Lint rules: {string.Join(", ", project.Lint.Rules.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => $"`{r.Key}` {r.Value}"))}.\n");
                if (project.Packages.Count > 0)
                    sb.Append($"- Every script can use {string.Join(", ", project.Packages.Select(p => $"`{p}`"))} without importing it.\n");
                string[] kinds = { "routes", "controllers", "lib", "types", "views", "components", "tests" };
                sb.Append($"- Folders: {string.Join(", ", kinds.Select(k => $"{k} in `{project.PathFor(k)}/`"))}.\n");
            }
            sb.Append('\n');

            if (guide.RoutesError != null)
            {
                sb.Append($"## Routes\n\nThe routes couldn't be read: {guide.RoutesError}\n\n");
            }
            else if (guide.Routes.Count > 0)
            {
                sb.Append("## Routes\n\n| Method | Path | Name | Answered by | Middleware | File |\n| --- | --- | --- | --- | --- | --- |\n");
                foreach (ProjectGuide.RouteRow r in guide.Routes)
                    sb.Append($"| {Cell(r.Methods)} | `{Cell(r.Path)}` | {(r.Name == null ? "" : $"`{Cell(r.Name)}`")} | {Cell(r.Target)} | {Cell(r.Middleware)} | {Cell(r.File ?? "")} |\n");
                sb.Append('\n');
            }

            if (guide.Files.Count == 0) sb.Append("No script declares a function, type, enum or constant at its top level yet.\n");
            if (guide.Truncated) sb.Append($"The project has more than {ProjectGuide.MaxFiles} scripts; these are the first {ProjectGuide.MaxFiles}.\n\n");
            string language = guide.Format == "xml" ? "xml" : "kscript";
            foreach (ProjectGuide.ScriptFile file in guide.Files)
            {
                sb.Append($"## {file.Path}\n\n");
                sb.Append($"```{language}\n{Shown($"<import path=\"{file.Path}\" />", guide.Format)}\n```\n\n");
                foreach (ProjectGuide.Declaration d in file.Declarations)
                {
                    sb.Append($"### {(d.Kind == "function" ? d.Signature : d.Name)} ({(d.Kind == "function" ? "fn" : d.Kind)})\n\n");
                    if (d.Doc?.Deprecated != null) sb.Append($"**Deprecated.** {d.Doc.Deprecated}\n\n");
                    if (!string.IsNullOrWhiteSpace(d.Summary)) sb.Append(d.Summary.Trim()).Append("\n\n");
                    foreach (var (name, text) in d.Doc?.Params ?? Array.Empty<(string, string)>()) sb.Append($"- `{name}`: {text}\n");
                    if (d.Doc != null && d.Doc.Params.Count > 0) sb.Append('\n');
                    if (!string.IsNullOrWhiteSpace(d.Doc?.Returns)) sb.Append($"Returns {d.Doc.Returns.Trim()}\n\n");
                    if (d.Xml != null) sb.Append($"```{language}\n{Shown(d.Xml, guide.Format)}\n```\n\n");
                    foreach (string example in d.Doc?.Examples ?? Array.Empty<string>())
                        sb.Append(example.Contains('\n') ? $"Example:\n\n```\n{example.Trim()}\n```\n\n" : $"Example: `{example.Trim()}`\n\n");
                    sb.Append($"In `{file.Path}`, line {d.Line}.\n\n");
                }
            }
            return sb.ToString().TrimEnd('\n') + "\n";
        }

        /// <summary>A declaration (written as XML) in the project's format.</summary>
        private static string Shown(string xml, string format) =>
            (format == "xml" ? xml : Playground.Convert(xml, "kscript") ?? xml).TrimEnd('\n');

        private static string Cell(string text) => text.Replace("|", "\\|");
    }
}
