using KScript.Pages;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Language.Analysis
{
    internal static class TemplateContracts
    {
        internal static IEnumerable<MarkupElement> Declarations(MarkupDocument document) =>
            document.RootElements.Where(e => e.Name.Equals("prop", StringComparison.OrdinalIgnoreCase));

        internal static IEnumerable<(string Name, bool Required)> Read(IFileSystem files, string path)
        {
            string text = path == null ? null : files.ReadText(path);
            if (text == null) yield break;
            var document = MarkupParser.Parse(new SourceText(text), MarkupParser.KindFromPath(path), path);
            if (ScriptFiles.IsScript(path))
            {
                var type = ComponentScript.FindType(document, ComponentScript.Tag(path));
                if (type == null) yield break;
                foreach ((string name, bool required) in Inputs(type)) yield return (name.Replace('_', '-'), required);
                yield break;
            }
            foreach (MarkupElement declaration in Declarations(document))
            {
                string name = declaration.GetAttributeValue("name");
                if (TemplateContract.IsPropName(name))
                    yield return (name, TemplateContract.IsRequired(declaration.GetAttribute("required") != null, declaration.GetAttributeValue("required")));
            }
        }

        /// <summary>What a component type takes from whoever draws it: each input's field name, and whether it must be passed.</summary>
        internal static IEnumerable<(string Name, bool Required)> Inputs(MarkupElement type)
        {
            foreach (var field in TypeBody.Inputs(type))
            {
                string name = field.GetAttributeValue("name")?.Trim();
                if (string.IsNullOrEmpty(name)) continue;
                bool optional = name.EndsWith('?') || field.GetAttribute("optional") != null || field.GetAttribute("default") != null ||
                    TemplateContract.HasImplicitFieldDefault(KScript.Runtime.TypeRef.Parse(field.GetAttributeValue("type")));
                yield return (name.TrimEnd('?'), !optional);
            }
        }

        /// <summary>The fields a script component keeps to itself: those its construct block doesn't list.</summary>
        internal static IEnumerable<string> OwnState(IFileSystem files, string path)
        {
            if (path == null || !ScriptFiles.IsScript(path) || files.ReadText(path) is not string text) return Array.Empty<string>();
            var type = ComponentScript.FindType(MarkupParser.Parse(new SourceText(text), MarkupParser.KindFromPath(path), path), ComponentScript.Tag(path));
            return TypeBody.Fields(type).Where(f => f.GetAttribute("value") == null && !TypeBody.IsInput(f))
                .Select(f => f.GetAttributeValue("name")?.Trim().TrimEnd('?')).Where(n => !string.IsNullOrEmpty(n)).ToList();
        }

        private static readonly string[] SiteMarkers = { "kscript.json", "_layout.html", "_components", "_layouts" };

        internal static string SiteRoot(IFileSystem files, string directory)
        {
            for (string dir = directory; dir != null; dir = Path.GetDirectoryName(dir))
                if (SiteMarkers.Any(m => files.FileExists(Path.Combine(dir, m)) || files.DirectoryExists(Path.Combine(dir, m))))
                    return dir;
            return directory;
        }

        internal static string ComponentFile(IFileSystem files, string directory, string tag)
        {
            return ComponentScript.FindFile(directory, tag, files.FileExists, files.ReadText);
        }

        internal static string CompanionFile(IFileSystem files, string template)
        {
            string path = TemplateContract.CompanionPath(template);
            return path != null && files.FileExists(path) ? path : null;
        }
    }

    public sealed partial class Analyzer
    {
        private void DeclareTemplateContract(Scope root)
        {
            if (Kind == DocumentKind.Script) return;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (MarkupElement prop in TemplateContracts.Declarations(result.Markup))
            {
                MarkupAttribute attr = prop.GetAttribute("name");
                string name = attr?.ValueText;
                if (!TemplateContract.IsPropName(name))
                {
                    if (attr != null) Report(attr.Span, DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue,
                        "Write a prop name such as title or page-title.");
                    continue;
                }

                string key = TemplateContract.VariableName(name);
                if (!seen.Add(key))
                {
                    Report(attr.Span, DiagnosticSeverity.Error, DiagnosticCodes.DuplicateDeclaration, $"The prop '{name}' is declared more than once.");
                    continue;
                }
                DeclareImplicit(root, key, TypeShape.Unknown, $"The '{name}' prop supplied by the caller.", prop);
            }

            string companion = TemplateContracts.CompanionFile(files, result.Path);
            if (companion == null) return;
            AddValue("props", new TypeShape(KType.Map), "The props supplied to this component.");
            AddValue("data", TypeShape.Unknown, "The result of this component's load function.");

            void AddValue(string name, TypeShape shape, string description)
            {
                var symbol = new Symbol { Name = name, Kind = SymbolKind.BuiltIn, Shape = shape, Description = description };
                result.Symbols.Add(symbol);
                root.Declare(symbol, 0);
            }

            foreach (var export in ReadExports(companion) ?? new Dictionary<string, (FunctionParameter[] Params, ExternalLocation Location, string Description)>())
            {
                var fn = new Symbol
                {
                    Name = export.Key, Kind = SymbolKind.Function, SourcePath = companion,
                    Description = export.Value.Description ?? "A function from this component's companion script.",
                };
                fn.Parameters.AddRange(export.Value.Params);
                result.Symbols.Add(fn);
                result.ExternalDeclarations[fn] = export.Value.Location;
                root.Declare(fn, 0);
            }

            var constants = ReadConstantExports(companion);
            if (constants == null) return;
            foreach (var constant in constants)
                root.Declare(ImportedConstant(constant.Key, default, null, companion, Path.GetFileName(companion), constant.Value), 0);
        }

        private static bool IsNullTemplateValue(string text)
        {
            string value = text?.Trim();
            return value is { Length: >= 2 } && value[0] == '{' && value[^1] == '}' && value[1..^1].Trim() == "null";
        }

        private void CheckLoadCallback(MarkupElement component, MarkupAttribute attribute, Scope scope)
        {
            Template template = TemplateParser.Parse(attribute.ValueText, allowBareReferences: false);
            if (attribute.Value == null || template.Segments.Count != 1 || template.Segments[0].Expression == null
                || template.Segments[0].Node is LiteralNode or LambdaNode { Parameters.Count: not 1 })
            {
                Report(attribute.Span, DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue,
                    "onLoad needs a function with one parameter, such as {fn(data) => @note_loaded($data)}.");
                return;
            }
            AnalyzeTemplate(attribute.Value, scope, component, attribute, allowBare: false);
        }

        private void CheckTemplateInputs(MarkupElement caller, string template)
        {
            if (template == null) return;
            var passed = new Dictionary<string, MarkupAttribute>(StringComparer.Ordinal);
            foreach (MarkupAttribute attribute in caller.Attributes) passed.TryAdd(TemplateContract.VariableName(attribute.Name), attribute);
            foreach (string own in TemplateContracts.OwnState(files, template))
            {
                if (passed.TryGetValue(own, out MarkupAttribute set))
                    Report(set.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.UnknownParameter,
                        $"'{set.Name}' is the component's own state and can't be passed in. Its construct block lists what it takes.");
            }
            foreach ((string name, bool required) in TemplateContracts.Read(files, template).Distinct())
            {
                if (!required) continue;
                if (!passed.TryGetValue(TemplateContract.VariableName(name), out MarkupAttribute given) || IsNullTemplateValue(given.ValueText))
                    Report(given?.Span ?? caller.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.MissingAttribute,
                        $"Pass the required '{name}' prop to {Path.GetFileName(template)}.");
            }
        }
    }
}
