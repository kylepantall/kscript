using System.Text;

using KScript.Syntax.Markup;

namespace KScript.Docs
{
    /// <summary>The project page (/project) that <c>kscript serve --with-docs</c> adds: the project's own code and conventions.</summary>
    public sealed partial class DocsSite
    {
        public const string ProjectPath = "/project";

        public const string ProjectSection = "Your project";

        /// <summary>Adds the project page, or replaces it (and its search entries) when the project has changed.</summary>
        public void SetProject(ProjectGuide guide)
        {
            Pages.RemoveAll(p => p.Path == ProjectPath);
            Search.RemoveAll(s => s.Url == ProjectPath || s.Url.StartsWith(ProjectPath + "#", StringComparison.Ordinal));
            DocPage page = ProjectPage(guide);
            Pages.Insert(0, page);
            Search.Add(new SearchEntry { Title = page.Title, Url = page.Path, Section = page.Section, Kind = "page", Text = page.Description });
            foreach (ProjectGuide.ScriptFile file in guide.Files)
                foreach (ProjectGuide.Declaration d in file.Declarations)
                    Search.Add(new SearchEntry { Title = SearchTitle(d), Url = ProjectPath + "#" + Anchor(d), Section = file.Path, Kind = "project", Text = Shorten(d.Summary ?? string.Empty) });
            foreach (ProjectGuide.RouteRow r in guide.Routes)
                Search.Add(new SearchEntry { Title = r.Methods + " " + r.Path, Url = ProjectPath + "#routes", Section = "Routes", Kind = "project", Text = r.Target });
        }

        private static string SearchTitle(ProjectGuide.Declaration d) => d.Kind == "function" ? "@" + d.Name : d.Name;

        internal static string Anchor(ProjectGuide.Declaration d) => (d.Kind == "function" ? "fn-" : d.Kind + "-") + Html.Slug(d.Name);

        internal DocPage ProjectPage(ProjectGuide guide)
        {
            var page = new DocPage
            {
                Path = ProjectPath,
                Title = guide.Name,
                NavTitle = guide.Name,
                Section = ProjectSection,
                Description = $"How {guide.Name} is written: its conventions, routes, and the functions, types and constants its scripts declare.",
            };
            var sb = new StringBuilder();
            sb.Append($"<h1>{Html.H(guide.Name)}</h1>");
            sb.Append($"<p class=\"lead\">This page is built from the project in <code>{Html.H(guide.Directory)}</code> while <code>kscript serve</code> runs, and is rebuilt when a script changes. Its functions, types and constants come from the scripts themselves, with their <code>///</code> doc comments. Everything else in these docs is KScript itself.</p>");

            Conventions(guide, page, sb);
            Routes(guide, page, sb);
            Declarations(guide, page, sb);

            page.Body = sb.ToString();
            return page;
        }

        private static void ProjectHeading(DocPage page, StringBuilder sb, int level, string id, string text, string codeText = null)
        {
            sb.Append($"<h{level} id=\"{id}\"><a class=\"anchor\" href=\"#{id}\">#</a>{(codeText ?? Html.H(text))}</h{level}>");
            page.Toc.Add((level, id, text));
        }

        private static void Conventions(ProjectGuide guide, DocPage page, StringBuilder sb)
        {
            ProjectHeading(page, sb, 2, "conventions", "How code is written here");
            var rows = new List<(string, string)>();
            string ext = guide.Format == "xml" ? ".ks.xml" : ".kscript";
            rows.Add(("Format", $"New scripts, routes and tests are <code>{ext}</code> files{(guide.Project?.Make.Format == null ? " (the default; <code>make.syntax</code> in kscript.json changes it)" : " (<code>make.syntax</code> in kscript.json)")}. Examples on these pages show {(guide.Format == "xml" ? "XML" : ".kscript")} first."));
            var project = guide.Project;
            if (project == null)
            {
                rows.Add(("Settings", "There is no kscript.json, so the defaults apply: the <code>kscript:recommended</code> standard. <code>kscript make project</code> starts one."));
            }
            else
            {
                rows.Add(("Standard", string.Join(", ", project.Standard.Select(s => $"<code>{Html.H(s)}</code>")) + " (lint rules and format settings; see <a href=\"/guide/standards\">Standards</a>)"));
                var format = new List<string>();
                if (project.Format.UseTabs == true) format.Add("tabs");
                else if (project.Format.IndentSize != null) format.Add($"{project.Format.IndentSize} spaces per indent");
                if (project.Format.PrintWidth != null) format.Add($"lines up to {project.Format.PrintWidth} characters");
                if (format.Count > 0) rows.Add(("Layout", string.Join(", ", format) + ". <code>kscript fmt</code> lays files out this way."));
                if (project.Lint.Rules.Count > 0)
                    rows.Add(("Lint rules", string.Join(", ", project.Lint.Rules.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => $"<code>{Html.H(r.Key)}</code> {Html.H(r.Value)}")) + ". <code>kscript check</code> reports the rest as the standard says."));
                if (project.Packages.Count > 0)
                    rows.Add(("Packages", "Every script can use " + string.Join(", ", project.Packages.Select(p => $"<a href=\"{PackageLink(p)}\"><code>{Html.H(p)}</code></a>")) + " without importing it."));
                string[] kinds = { "routes", "controllers", "lib", "types", "views", "components", "tests" };
                rows.Add(("Folders", string.Join(", ", kinds.Select(k => $"{k} in <code>{Html.H(project.PathFor(k))}/</code>")) + ". <a href=\"/cli/make\"><code>kscript make</code></a> puts new files there."));
            }
            sb.Append("<div class=\"table\"><table><tbody>");
            foreach (var (name, value) in rows) sb.Append($"<tr><th>{Html.H(name)}</th><td>{value}</td></tr>");
            sb.Append("</tbody></table></div>");
        }

        private static string PackageLink(string package) => Mcp.DocsMarkdown.PackagePath(package);

        private static void Routes(ProjectGuide guide, DocPage page, StringBuilder sb)
        {
            if (guide.Routes.Count == 0 && guide.RoutesError == null) return;
            ProjectHeading(page, sb, 2, "routes", "Routes");
            if (guide.RoutesError != null)
            {
                sb.Append($"<p class=\"note\">The routes couldn't be read: {Html.H(guide.RoutesError)}</p>");
                return;
            }
            sb.Append("<p>What <code>kscript routes</code> lists: each route, what answers it and its middleware. Link to a named route with <code>@route('name')</code>.</p>");
            sb.Append("<div class=\"table\"><table><thead><tr><th>Method</th><th>Path</th><th>Name</th><th>Answered by</th><th>Middleware</th><th>File</th></tr></thead><tbody>");
            foreach (ProjectGuide.RouteRow r in guide.Routes)
                sb.Append($"<tr><td>{Html.H(r.Methods)}</td><td><code>{Html.H(r.Path)}</code></td><td>{(r.Name == null ? "" : $"<code>{Html.H(r.Name)}</code>")}</td><td>{Html.H(r.Target)}</td><td>{Html.H(r.Middleware)}</td><td>{Html.H(r.File ?? "")}</td></tr>");
            sb.Append("</tbody></table></div>");
        }

        private static void Declarations(ProjectGuide guide, DocPage page, StringBuilder sb)
        {
            ProjectHeading(page, sb, 2, "code", "Functions, types and constants");
            if (guide.Files.Count == 0)
            {
                sb.Append("<p>No script in the project declares a function, type, enum or constant at its top level yet.</p>");
                return;
            }
            sb.Append("<p>Everything the project's scripts declare at their top level, file by file. Another script uses them after <code>import</code>ing the file. A <code>///</code> comment above a declaration is its description here, in the editor's hover and in completion.</p>");
            if (guide.Truncated) sb.Append($"<p class=\"note\">The project has more than {ProjectGuide.MaxFiles} scripts; this page lists the first {ProjectGuide.MaxFiles}.</p>");
            foreach (ProjectGuide.ScriptFile file in guide.Files)
            {
                string fileId = "file-" + Html.Slug(file.Path);
                sb.Append($"<h2 id=\"{fileId}\" class=\"file-heading\"><a class=\"anchor\" href=\"#{fileId}\">#</a><code>{Html.H(file.Path)}</code></h2>");
                page.Toc.Add((2, fileId, file.Path));
                sb.Append(ImportExample(file.Path));
                foreach (ProjectGuide.Declaration d in file.Declarations) Entry(file, d, sb);
            }
        }

        /// <summary>How another script imports the file, in both formats.</summary>
        private static string ImportExample(string path) =>
            Html.Code($"<import src=\"{path}\" />", "xml");

        private static void Entry(ProjectGuide.ScriptFile file, ProjectGuide.Declaration d, StringBuilder sb)
        {
            string anchor = Anchor(d);
            bool deprecated = d.Doc?.Deprecated != null;
            string kind = d.Kind == "function" ? "fn" : d.Kind;
            string title = $"<code>{Html.H(d.Kind == "function" ? d.Signature : d.Name)}</code>";
            sb.Append($"<section class=\"entry{(deprecated ? " deprecated" : "")}\" id=\"{anchor}\"><h3 class=\"sig\"><a class=\"anchor\" href=\"#{anchor}\">#</a>{(deprecated ? "<del>" + title + "</del>" + DeprecatedBadge : title)} <span class=\"badge\">{Html.H(kind)}</span></h3>");
            if (deprecated) sb.Append($"<p class=\"deprecation\"><strong>Deprecated.</strong> {Html.Inline(d.Doc.Deprecated)}</p>");
            if (!string.IsNullOrWhiteSpace(d.Summary)) sb.Append($"<p>{Html.Inline(d.Summary)}</p>");
            if (d.Doc != null && d.Doc.Params.Count > 0)
            {
                sb.Append("<div class=\"table\"><table><thead><tr><th>Parameter</th><th>What it is</th></tr></thead><tbody>");
                foreach (var (name, text) in d.Doc.Params) sb.Append($"<tr><td><code>{Html.H(name)}</code></td><td>{Html.Inline(text)}</td></tr>");
                sb.Append("</tbody></table></div>");
            }
            if (!string.IsNullOrWhiteSpace(d.Doc?.Returns)) sb.Append($"<p><strong>Returns</strong> {Html.Inline(d.Doc.Returns)}</p>");
            if (d.Xml != null) sb.Append(Html.Code(d.Xml, "xml"));
            foreach (string example in d.Doc?.Examples ?? Array.Empty<string>())
                sb.Append(Html.Code(example, example.TrimStart().StartsWith("<", StringComparison.Ordinal) ? "xml" : "text"));
            sb.Append($"<p class=\"where\">In <code>{Html.H(file.Path)}</code>, line {d.Line}.</p></section>");
        }
    }
}
