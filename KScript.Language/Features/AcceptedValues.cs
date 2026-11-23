using KScript.Http;
using KScript.Language.Analysis;
using KScript.Projects;
using KScript.Runtime;
using KScript.Syntax;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Language.Features
{
    /// <summary>
    /// What a type accepts, for completion, hover and signature help: an enum's values, true and false, and what a
    /// native type suggests through <see cref="NativeType.Suggest"/> (every time zone, the project's routes...).
    /// </summary>
    public sealed partial class LanguageService
    {
        /// <summary>The most values one type offers at once, so a long list can't flood the editor.</summary>
        internal const int MaxAcceptedValues = 1000;

        /// <summary>
        /// The values <paramref name="type"/> accepts where it is written in <paramref name="analysis"/>'s file, or
        /// an empty list. The type is written as in code: <c>timezone</c>, <c>Size</c>, <c>bool?</c>, <c>path(exists: route)</c>.
        /// </summary>
        internal List<NativeValueSuggestion> AcceptedValues(DocumentAnalysis analysis, string type, string typed = "")
        {
            var values = new List<NativeValueSuggestion>();
            (string name, Dictionary<string, string> options) = SplitType(type);
            if (name.Length == 0)
            {
                return values;
            }
            if (EnumValuesOf(analysis, name) is IReadOnlyList<string> members)
            {
                values.AddRange(members.Select(m => new NativeValueSuggestion(m, name)));
                return values;
            }
            if (FiniteValueChoices(analysis, type) is IReadOnlyList<string> choices)
            {
                values.AddRange(choices.Take(MaxAcceptedValues).Select(value => new NativeValueSuggestion(value, name)));
                return values;
            }
            if (name == KType.Bool)
            {
                values.Add(new NativeValueSuggestion("true", KType.Bool));
                values.Add(new NativeValueSuggestion("false", KType.Bool));
                return values;
            }
            if (NativeTypeRegistry.Get(name) is NativeType native)
            {
                string root = ProjectFolder(analysis.Path);
                NativeValueContext context = new NativeValueContext
                {
                    DocumentPath = analysis.Path != null ? Path.GetFullPath(analysis.Path) : null,
                    ProjectRoot = root,
                    Typed = typed ?? string.Empty,
                    Options = options,
                    Routes = () => ProjectRoutes(root),
                    ListFolder = folder => Files.ListFiles(folder, "*").Concat(Files.ListDirectories(folder)),
                };
                try
                {
                    values.AddRange(native.Suggest(context).Where(v => v?.Value != null).Take(MaxAcceptedValues));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ProjectConfigException)
                {
                    // A provider that can't read the project suggests nothing rather than breaking completion.
                }
            }
            return values;
        }

        private static IReadOnlyList<string> FiniteValueChoices(DocumentAnalysis analysis, string type)
        {
            string name = SplitType(type).Name;
            TypeShape shape = analysis.RootScope?.Lookup(name, int.MaxValue)?.Shape;
            var named = shape?.ValueChoices;
            if (!type.Contains('(')) return named;
            TypeRef parsed = TypeRef.Parse(type);
            if (parsed == null || parsed.ListDepth != 0 || parsed.ArgBase?.ListDepth > 0) return null;
            var rules = new List<TypeRules>();
            for (TypeRef level = parsed; level?.Args != null; level = level.ArgBase)
                rules.Add(TypeRules.FromOptions(level.Args, out _));
            TypeRules finite = rules.FirstOrDefault(rule => rule?.OneOf != null);
            if (finite == null) return named;
            var choices = Analyzer.ChoicesFromRules(finite, rules.Concat(shape?.LiteralRules ?? Array.Empty<TypeRules>()), shape?.Instance?.Type ?? name);
            return named == null ? choices : choices.Intersect(named, StringComparer.Ordinal).ToArray();
        }

        /// <summary>A literal KScript expression for textual suggestions, including apostrophes and template text.</summary>
        internal static string QuotedValue(string value)
        {
            if (!value.Contains('\'')) return "'" + value + "'";
            if (!value.Contains('$') && !value.Contains('{'))
                return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            return string.Join(" + \"'\" + ", value.Split('\'').Select(part => "'" + part + "'"));
        }

        /// <summary>The values of the enum called <paramref name="name"/> (declared here or imported), or null.</summary>
        internal static IReadOnlyList<string> EnumValuesOf(DocumentAnalysis analysis, string name) =>
            analysis.Symbols.FirstOrDefault(x => x.Name == name && x.EnumValues != null)?.EnumValues
            ?? (analysis.RootScope?.Lookup(name, int.MaxValue) is Symbol { EnumValues: List<string> values } ? values : null);

        /// <summary><c>path(exists: route)</c> as path and { exists: route }; <c>bool?</c> as bool.</summary>
        internal static (string Name, Dictionary<string, string> Options) SplitType(string type)
        {
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            string t = (type ?? string.Empty).Trim().TrimEnd('?').Trim();
            int open = t.IndexOf('(');
            if (open < 0)
            {
                return (t, options);
            }
            string inside = t.Substring(open + 1).TrimEnd(')');
            foreach (string part in inside.Split(','))
            {
                int colon = part.IndexOf(':');
                if (colon > 0)
                {
                    options[part.Substring(0, colon).Trim()] = part.Substring(colon + 1).Trim().Trim('\'', '"');
                }
            }
            return (t.Substring(0, open).Trim(), options);
        }

        /// <summary>The folder with the nearest kscript.json above the file, or the file's own folder.</summary>
        private string ProjectFolder(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }
            string folder = Path.GetDirectoryName(Path.GetFullPath(path));
            for (string dir = folder; !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
            {
                if (Files.FileExists(Path.Combine(dir, ProjectConfig.FileName)))
                {
                    return dir;
                }
            }
            return folder;
        }

        /// <summary>
        /// The routes of the project in <paramref name="root"/>, read from its route files without running them: each
        /// Get, Post, Put, Patch, Delete, Any and Resource with its path (behind its file's and groups' prefixes).
        /// </summary>
        internal List<(string Method, string Path, string Handler)> ProjectRoutes(string root)
        {
            var routes = new List<(string, string, string)>();
            if (root == null || !Files.FileExists(Path.Combine(root, ProjectConfig.FileName)))
            {
                return routes;
            }
            ProjectConfig project = ProjectConfig.Parse(Files.ReadText(Path.Combine(root, ProjectConfig.FileName)) ?? "{}", Path.Combine(root, ProjectConfig.FileName),
                Files.FileExists, Files.ReadText);
            foreach (HttpApp.RouteFileInfo file in new HttpApp(project.SiteRoot, project).Files)
            {
                string text = Files.ReadText(file.Path);
                if (text == null)
                {
                    continue;
                }
                MarkupDocument doc = MarkupParser.Parse(new SourceText(text), MarkupParser.KindFromPath(file.Path), file.Path);
                CodeArguments.Normalise(doc, Catalog.GetElement);
                foreach (MarkupElement e in doc.DescendantElements())
                {
                    string method = e.Name switch
                    {
                        "Get" or "Post" or "Put" or "Patch" or "Delete" => e.Name.ToUpperInvariant(),
                        "Any" => "ANY",
                        "Resource" => "RESOURCE",
                        _ => null,
                    };
                    string path = e.GetAttributeValue("path")?.Trim();
                    if (method == null || string.IsNullOrEmpty(path))
                    {
                        continue;
                    }
                    string prefix = file.Prefix ?? string.Empty;
                    foreach (MarkupElement group in e.Ancestors().Reverse().Where(a => a.Name == "Group" || a.Name == "Routes"))
                    {
                        prefix = RoutePattern.Join(prefix, group.GetAttributeValue("prefix")?.Trim() ?? string.Empty);
                    }
                    routes.Add((method, RoutePattern.Join(prefix, path), e.GetAttributeValue("to")?.Trim()));
                }
            }
            return routes;
        }
    }
}
