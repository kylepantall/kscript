using KScript.Http;
using KScript.Language.Catalog;
using KScript.Metadata;
using KScript.Projects;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Language.Agents
{
    public static partial class AgentDocs
    {
        /// <summary>Lists explicit route declarations without running startup configuration, imports, or route bodies.</summary>
        private static string RoutesOf(string site, ProjectConfig config, LanguageCatalog catalog, ISet<string> skip)
        {
            var rows = new List<string[]>();
            HttpApp app;
            try { app = new HttpApp(site, config); }
            catch (FileNotFoundException) { return null; }

            foreach (HttpApp.RouteFileInfo file in app.Files)
            {
                string path = file.Path;
                if (skip?.Contains(path) == true && File.Exists(ScriptConverter.ConvertedPath(path)))
                    path = ScriptConverter.ConvertedPath(path);
                var source = new SourceText(File.ReadAllText(path));
                MarkupDocument document = MarkupParser.Parse(source, DocumentKind.Script, path);
                CodeArguments.Normalise(document, catalog.GetElement);
                foreach (MarkupElement element in document.DescendantElements())
                {
                    string name = ElementInfo.BareName(element.Name);
                    string method = name switch
                    {
                        "Route" => string.Join("|", NameList.Names(element.GetAttributeValue("methods")).Select(m => m.ToUpperInvariant())),
                        "Resource" => "RESOURCE",
                        _ => HttpPackage.MethodElements.FirstOrDefault(item => item.Element == name) is (string _, string verb) ? verb ?? "ANY" : null,
                    };
                    if (string.IsNullOrEmpty(method)) continue;

                    MarkupElement[] groups = element.Ancestors().Reverse()
                        .Where(parent => ElementInfo.BareName(parent.Name) != "kscript").ToArray();
                    if (groups.Length == 0 || ElementInfo.BareName(groups[0].Name) != "Routes"
                        || groups.Skip(1).Any(parent => ElementInfo.BareName(parent.Name) != "Group"))
                        continue;

                    string pattern = RouteText(element, "path");
                    if (string.IsNullOrEmpty(pattern)) continue;
                    string prefix = file.Prefix ?? string.Empty;
                    var names = new System.Text.StringBuilder();
                    foreach (MarkupElement group in groups)
                    {
                        prefix = RoutePattern.Join(prefix, RouteText(group, "prefix") ?? string.Empty);
                        names.Append(RouteText(group, "name"));
                    }

                    string routeName = RouteText(element, "name");
                    string target = RouteText(element, "to")?.TrimStart('@')
                        ?? RouteText(element, "view") ?? RouteText(element, "redirect") ?? "inline";
                    if (name == "Resource") routeName ??= target;
                    var middleware = (config?.Http.Middleware ?? new List<string>()).Concat(groups.Append(element)
                        .SelectMany(group => NameList.Names(RouteText(group, "middleware"))));
                    string relative = Path.GetRelativePath(site, path).Replace('\\', '/');
                    int line = source.GetPosition(element.Span.Start).Line + 1;
                    rows.Add(new[] { method, RoutePattern.Join(prefix, pattern), routeName == null ? string.Empty : names + routeName,
                        target, string.Join(", ", middleware), $"{relative}:{line}" });
                }
            }
            if (rows.Count == 0) return null;

            var header = new[] { "METHOD", "PATH", "NAME", "ANSWERED BY", "MIDDLEWARE", "FILE" };
            int[] widths = header.Select((cell, i) => Math.Max(cell.Length, rows.Max(row => row[i].Length))).ToArray();
            string Line(string[] row) => string.Join("  ", row.Select((cell, i) => i == row.Length - 1 ? cell : cell.PadRight(widths[i]))).TrimEnd();
            return string.Join("\n", rows.Prepend(header).Select(Line));
        }

        private static string RouteText(MarkupElement element, string attribute) => element.GetAttributeValue(attribute)?.Trim();
    }
}
