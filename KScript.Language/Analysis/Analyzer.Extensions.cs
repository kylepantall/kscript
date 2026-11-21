using KScript.Language.Catalog;
using KScript.Projects;
using KScript.Syntax;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Language.Analysis
{
    /// <summary>
    /// <c>extend date { fn json() => ... }</c>: methods a file adds to a type. The editor offers and checks them
    /// wherever they are in effect: in the file, in files that import it, and (for a project's extension files,
    /// the extensions folder or kscript.json's "extensions") in every file of the project.
    /// </summary>
    public sealed partial class Analyzer
    {
        /// <summary>The built-in types an extend block can name, as well as the types packages' methods are for.</summary>
        private static readonly string[] ExtendableTypes = { "string", "number", "bool", "list", "map", "fn", "date", "email", "password", "handle", "uuid", "regex" };

        private static string ExtensionReceiver(string type) => type is "int" or "float" ? KType.Number : type;

        /// <summary>Finds every extension method in effect before the walk, so calls above the extend block are known.</summary>
        private void HoistExtensions(Scope root)
        {
            foreach (MarkupElement e in result.Markup.DescendantElements().Where(x => IsKScriptElementName(x, "extend")))
            {
                string receiver = ExtendReceiver(e, root);
                if (receiver == null)
                {
                    continue;
                }
                foreach (MarkupElement fn in e.ChildElements.Where(c => IsKScriptElementName(c, "fn")))
                {
                    MarkupAttribute nameAttr = fn.GetAttribute("name");
                    string name = nameAttr?.ValueText?.Trim().TrimStart('@');
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }
                    TextSpan span = TrimmedSpan(nameAttr.Value);
                    Symbol method = new Symbol
                    {
                        Name = name,
                        Kind = SymbolKind.Function,
                        DeclarationSpan = span,
                        DeclaringElement = fn,
                        Description = fn.Documentation,
                        ExtensionOf = receiver,
                    };
                    if (fn.GetAttribute("params")?.Value is DecodedText ps)
                    {
                        method.Parameters.AddRange(ParseParameters(ps, report: false));
                    }
                    result.Symbols.Add(method);
                    result.References.Add(new SymbolReference(method, span, true, true));
                    if (!result.ExtensionMethods.TryGetValue(receiver, out Dictionary<string, Symbol> methods))
                    {
                        result.ExtensionMethods[receiver] = methods = new Dictionary<string, Symbol>(StringComparer.Ordinal);
                    }
                    if (methods.TryGetValue(name, out Symbol earlier) && earlier.SourcePath == null)
                    {
                        Report(span, DiagnosticSeverity.Error, DiagnosticCodes.DuplicateDeclaration, $"->{name}() is already added to {receiver} in this file.");
                        continue;
                    }
                    methods[name] = method;
                }
            }

            var visited = new HashSet<string>(StringComparer.Ordinal);
            if (result.Path != null)
            {
                visited.Add(System.IO.Path.GetFullPath(result.Path));
            }
            foreach (MarkupElement import in result.Markup.DescendantElements().Where(x => IsKScriptElementName(x, "import") && x.GetAttribute("src") != null))
            {
                AddExternalExtensions(ResolvePath(import.GetAttributeValue("src")?.Trim()), visited, project: false);
            }
            List<string> projectFiles = ProjectExtensionFiles();
            projectExtensionFile = result.Path != null && projectFiles.Contains(System.IO.Path.GetFullPath(result.Path), StringComparer.Ordinal);
            foreach (string file in projectFiles)
            {
                AddExternalExtensions(file, visited, project: true);
            }
        }

        /// <summary>The project's extension files, from the nearest kscript.json (none outside a project).</summary>
        private List<string> ProjectExtensionFiles()
        {
            for (string dir = directory; dir != null; dir = System.IO.Path.GetDirectoryName(dir))
            {
                string file = System.IO.Path.Combine(dir, ProjectConfig.FileName);
                if (!files.FileExists(file))
                {
                    continue;
                }
                try
                {
                    string text = files.ReadText(file);
                    return text == null ? new List<string>() : ProjectConfig.Parse(text, file).ExtensionFiles(files.DirectoryExists, files.FileExists, d => files.ListFiles(d, "*"), files.ListDirectories);
                }
                catch (ProjectConfigException)
                {
                    return new List<string>();
                }
            }
            return new List<string>();
        }

        /// <summary>Adds the extension methods another file declares (and, for an import, those of the files it imports).</summary>
        private void AddExternalExtensions(string path, HashSet<string> visited, bool project)
        {
            if (path == null || !visited.Add(System.IO.Path.GetFullPath(path)))
            {
                return;
            }
            string text = files.ReadText(path);
            if (text == null)
            {
                return;
            }
            SourceText other = new SourceText(text);
            MarkupDocument doc = MarkupParser.Parse(other, MarkupParser.KindFromPath(path), path);
            HashSet<string> declaredHere = new HashSet<string>(doc.DescendantElements().Where(x => x.Name is "type" or "model").Select(x => x.GetAttributeValue("name")?.Trim()).Where(n => n != null), StringComparer.Ordinal);
            foreach (MarkupElement e in doc.DescendantElements().Where(x => x.Name == "extend"))
            {
                string type = e.GetAttributeValue("type")?.Trim();
                if (string.IsNullOrEmpty(type))
                {
                    continue;
                }
                string receiver = declaredHere.Contains(type) ? type : ExtensionReceiver(type);
                if (!result.ExtensionMethods.TryGetValue(receiver, out Dictionary<string, Symbol> methods))
                {
                    result.ExtensionMethods[receiver] = methods = new Dictionary<string, Symbol>(StringComparer.Ordinal);
                }
                foreach (MarkupElement fn in e.ChildElements.Where(c => c.Name == "fn"))
                {
                    MarkupAttribute nameAttr = fn.GetAttribute("name");
                    string name = nameAttr?.ValueText?.Trim().TrimStart('@');
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }
                    if (methods.TryGetValue(name, out Symbol existing))
                    {
                        // Two of the project's extension files adding the same method is an error when the project runs.
                        if (project && projectExtensionFile && existing.SourcePath == null)
                        {
                            Report(existing.DeclarationSpan, DiagnosticSeverity.Error, DiagnosticCodes.DuplicateDeclaration,
                                $"Another extension file ({System.IO.Path.GetFileName(path)}) already adds ->{name}() to {receiver}. Keep one.");
                        }
                        continue; // this file's own (or the first file's) wins, as when the script runs
                    }
                    Symbol method = new Symbol
                    {
                        Name = name,
                        Kind = SymbolKind.Function,
                        Description = fn.Documentation,
                        SourcePath = path,
                        ExtensionOf = receiver,
                    };
                    if (fn.GetAttribute("params")?.Value is DecodedText ps)
                    {
                        method.Parameters.AddRange(ParseParameters(ps, report: false));
                    }
                    TextSpan span = nameAttr.Value.SourceSpan;
                    result.ExternalDeclarations[method] = new ExternalLocation(path, other.GetPosition(span.Start), other.GetPosition(span.End));
                    methods[name] = method;
                }
            }
            if (project)
            {
                return;
            }
            foreach (MarkupElement import in doc.DescendantElements().Where(x => x.Name == "import" && x.GetAttribute("src") != null))
            {
                string src = import.GetAttributeValue("src")?.Trim();
                string dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
                AddExternalExtensions(string.IsNullOrEmpty(src) || dir == null ? null : System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, src)), visited, project: false);
            }
        }

        /// <summary>True when this file is one of the project's extension files.</summary>
        private bool projectExtensionFile;

        /// <summary>The receiver an extend element names: a declared type's name, or a built-in or package type. Null when unknown.</summary>
        private string ExtendReceiver(MarkupElement e, Scope root)
        {
            string type = e.GetAttributeValue("type")?.Trim();
            if (string.IsNullOrEmpty(type))
            {
                return null;
            }
            Symbol declared = root.Lookup(type, int.MaxValue);
            if (declared?.Shape?.Type == KType.Type && declared.Shape.Instance?.Type == KType.Map)
            {
                return type;
            }
            string receiver = ExtensionReceiver(type);
            return KnownExtendableTypes().Contains(receiver) ? receiver : null;
        }

        private IEnumerable<string> KnownExtendableTypes() =>
            ExtendableTypes.Concat(catalog.MethodReceivers).Where(r => r != KType.Any).Distinct(StringComparer.Ordinal);

        /// <summary>Checks an extend block (its type, that it holds only fn methods, override) and walks its methods with $this.</summary>
        private void AnalyzeExtend(MarkupElement e, Scope scope, Scope inner, bool markupMode)
        {
            bool code = result.Markup.Syntax == SourceSyntax.Code;
            MarkupAttribute typeAttr = e.GetAttribute("type");
            string type = typeAttr?.ValueText?.Trim();
            if (e.Parent is MarkupElement parent && parent != result.Markup.RootElements.FirstOrDefault())
            {
                Report(e.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.MisplacedElement, code ? "extend goes at the top level of a file, not inside a block." : "<extend> must be a direct child of <kscript>.");
            }
            if (string.IsNullOrEmpty(type))
            {
                Report(e.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.MissingAttribute, code ? "Write the type to add methods to: extend date { }." : "<extend> needs a type, such as <extend type=\"date\">.");
                WalkChildren(e.Children, inner, markupMode);
                return;
            }

            string receiver = ExtendReceiver(e, result.RootScope);
            TextSpan typeSpan = TrimmedSpan(typeAttr.Value);
            TypeShape thisShape = TypeShape.Unknown;
            TypeShape declared = null;
            if (receiver == null)
            {
                string close = Suggestions.Closest(type, KnownExtendableTypes().Concat(TypeNames()));
                Report(typeSpan, DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue,
                    $"There is no type called {type} to extend." + (close != null ? $" Did you mean {close}?" : string.Empty),
                    close != null ? new[] { new QuickFix($"Change to {close}", new TextChange(typeSpan, close)) } : null);
            }
            else
            {
                Symbol typeSymbol = result.RootScope.Lookup(type, int.MaxValue);
                declared = typeSymbol?.Shape?.Type == KType.Type ? typeSymbol.Shape.Instance : null;
                if (declared != null)
                {
                    result.References.Add(new SymbolReference(typeSymbol, typeSpan, false, false));
                }
                thisShape = declared ?? new TypeShape(receiver) { Declared = true };
            }

            foreach (MarkupNode node in e.Children)
            {
                if (node is MarkupText text && !text.IsWhitespace)
                {
                    Report(text.Span, DiagnosticSeverity.Error, DiagnosticCodes.StrayText, code ? $"extend {type} {{ }} holds fn methods only." : "<extend> holds <fn> elements only.");
                }
                if (node is not MarkupElement child)
                {
                    continue;
                }
                if (!IsKScriptElementName(child, "fn"))
                {
                    Report(child.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.MisplacedElement, code ? $"extend {type} {{ }} holds fn methods only, not {child.Name}." : $"<extend> holds <fn> elements only, not <{child.Name}>.");
                    continue;
                }
                if (receiver == null)
                {
                    continue;
                }
                CheckExtensionOverride(child, receiver, declared, code);
            }

            Scope extendScope = new Scope(inner, e.Span);
            DeclareImplicit(extendScope, "this", thisShape, $"The {type} the method is called on.", e);
            WalkChildren(e.Children, extendScope, markupMode);
        }

        private void CheckExtensionOverride(MarkupElement fn, string receiver, TypeShape declared, bool code)
        {
            MarkupAttribute nameAttr = fn.GetAttribute("name");
            string name = nameAttr?.ValueText?.Trim().TrimStart('@');
            if (string.IsNullOrEmpty(name))
            {
                return;
            }
            TextSpan span = TrimmedSpan(nameAttr.Value);
            bool isOverride = ScriptConverterYes(fn.GetAttributeValue("override"));
            bool builtIn = declared != null
                ? declared.Methods.ContainsKey(name) || BuiltInMethodFor(KType.Map, name)
                : BuiltInMethodFor(receiver, name);
            string article = (receiver.Length > 0 && "aeio".Contains(char.ToLowerInvariant(receiver[0])) ? "An " : "A ") + receiver;
            if (builtIn && !isOverride)
            {
                Report(span, DiagnosticSeverity.Error, DiagnosticCodes.DuplicateDeclaration,
                    $"{article} already has ->{name}(). To replace it, write " + (code ? $"override fn {name}(...)" : $"override=\"yes\"") + "; to add a new method, give it another name.");
                return;
            }
            if (isOverride && !builtIn)
            {
                Report(span, DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue,
                    $"{article} has no built-in ->{name}() to override. " + (code ? "Drop override" : "Drop override=\"yes\"") + " to add it as a new method.");
                return;
            }
        }

        private static bool ScriptConverterYes(string value) => value != null && (value.Trim() == "yes" || value.Trim() == "true");

        /// <summary>
        /// True when the type has a built-in method of this name in the packages the file has: its own, one every
        /// value has, or (for an email, handle, uuid or regex) a string method it keeps. The same rule as when the script runs.
        /// </summary>
        private bool BuiltInMethodFor(string receiver, string name) =>
            catalog.GetMethods(name, receiver).Any(m => string.Equals(m.Name, name, StringComparison.Ordinal)
                && (m.Package == null || m.Package == "KScript.Core" || importedPackages.Contains(m.Package))
                && (m.ReceiverType == receiver || m.ReceiverType == KType.Any
                    || (m.ReceiverType == KType.String && IsSemanticText(receiver) && KType.AcceptsMethod(KType.String, name, receiver))));
    }
}
