using KScript.Http;
using KScript.Pages;
using KScript.Projects;
using KScript.Runtime;
using KScript.Syntax;
using KScript.Syntax.Markup;

namespace KScript.Language.Analysis
{
    public sealed partial class Analyzer
    {
        private bool? inHandlerFolder;
        private HashSet<string> routeHandlerNames;

        private string routeFilePrefix;

        private void CheckRouteWhere(MarkupElement route, MarkupAttribute where)
        {
            string path = route.GetAttributeValue("path");
            if (path != null)
            {
                foreach (MarkupElement group in route.Ancestors().Where(parent => ElementInfo.BareName(parent.Name) is "Routes" or "Group"))
                    path = RoutePattern.Join(group.GetAttributeValue("prefix"), path);
                path = RoutePattern.Join(routeFilePrefix ??= RouteFilePrefix(), path);
            }
            try
            {
                RoutePattern.ValidateWhere(path, where.ValueText);
            }
            catch (KScriptException error)
            {
                Report(TrimmedSpan(where.Value), DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue, error.Words);
            }
        }

        private string RouteFilePrefix()
        {
            if (result.Path == null || ProjectFor(out _) is not ProjectConfig project) return string.Empty;
            string current = Path.GetFullPath(result.Path);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var known = project.Http.Routes?.FirstOrDefault(route =>
                Path.GetFullPath(Path.Combine(project.SiteRoot, route.File)).Equals(current, comparison));
            return known?.Prefix ?? (known?.Api == true || ProjectConfig.ScriptName(current).Equals("api", StringComparison.OrdinalIgnoreCase)
                ? project.Http.ApiPrefix : string.Empty);
        }

        private bool InHttpFolder(params string[] names)
        {
            if (result.Path == null) return false;
            ProjectConfig project = ProjectFor(out _);
            string relative = (project == null ? result.Path : Path.GetRelativePath(project.SiteRoot, result.Path)).Replace('\\', '/');
            foreach (string name in names)
            {
                string folder = (name == "views" ? project?.Http.Views : project?.PathFor(name)) ?? name;
                folder = folder.Trim('/', '\\').Replace('\\', '/');
                if (folder.Length == 0) continue;
                if (project == null ? ("/" + relative).Contains("/" + folder + "/", StringComparison.OrdinalIgnoreCase)
                    : relative.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private IEnumerable<ValueFieldInfo> RequestContextFields()
        {
            // Route files run before the match. Handlers replace this symbol once a route has matched.
            if (Kind == DocumentKind.Script) return new[] { RouteRequestFields.Ip };
            if (InHttpFolder("views")) return RouteRequestFields.Matched;
            if (Path.GetFileName(result.Path) == "_404.html") return Array.Empty<ValueFieldInfo>();
            return new[] { RequestFields.Locale };
        }

        private bool IsRouteHandler(MarkupElement fn)
        {
            if (Kind != DocumentKind.Script) return false;
            if (inHandlerFolder ??= InHttpFolder("controllers", "middleware")) return true;
            if (!importedPackages.Contains("KScript.Http")) return false;
            // Local functions in route files also run after matching when named as a controller or middleware.
            routeHandlerNames ??= result.Markup.DescendantElements()
                .Where(e => catalog.GetElement(ElementInfo.BareName(e.Name))?.Package == "KScript.Http")
                .SelectMany(e => e.Attributes.Where(a => a.Name is "to" or "middleware" or "use"))
                .SelectMany(a => NameList.Names(a.ValueText)).Select(n => n.TrimStart('@')).ToHashSet(StringComparer.Ordinal);
            return routeHandlerNames.Contains(fn.GetAttributeValue("name"));
        }

        private TypeShape MatchedRequestParameter(bool declared) => ShapeWithFields(KType.Map,
            catalog.FieldsOf(KType.Request).Values.Concat(RouteRequestFields.Matched), declared);

        private static TypeShape ShapeWithFields(string type, IEnumerable<ValueFieldInfo> fields, bool declared = false)
        {
            var shape = new TypeShape(type) { Declared = declared };
            foreach (ValueFieldInfo field in fields)
                shape.Fields[field.Name] = new FieldInfo(field.Name, field.Type is null or KType.Any ? TypeShape.Unknown : new TypeShape(field.Type), null, field.Description);
            return shape;
        }

        private void DeclareRequestVariables(MarkupElement e, Scope inner)
        {
            int at = e.ContentSpan.Start;
            Symbol request = BuiltIn("request", RouteRequestFields.Matched);
            inner.Declare(request, at);
            foreach (string name in HttpApp.RequestParameters)
            {
                if (name == "request") continue;
                FieldInfo field = request.Shape.Fields[name];
                // The injected $body parameter prefers parsed JSON; $request.body always holds raw text.
                Symbol symbol = new Symbol
                {
                    Name = name,
                    Kind = SymbolKind.BuiltIn,
                    Shape = name == "body" ? TypeShape.Unknown : field.Shape,
                    Description = name == "body" ? "The JSON body, or the raw body when it isn't JSON." : field.Description,
                };
                result.Symbols.Add(symbol);
                inner.Declare(symbol, at);
            }
            string path = e.GetAttributeValue("path");
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(path ?? string.Empty, @"\{(\w+)[?*]?\}"))
            {
                Symbol parameter = new Symbol { Name = m.Groups[1].Value, Kind = SymbolKind.BuiltIn, Shape = new TypeShape(KType.String), Description = $"The {{{m.Groups[1].Value}}} part of the path." };
                result.Symbols.Add(parameter);
                inner.Declare(parameter, at);
            }
        }
    }
}
