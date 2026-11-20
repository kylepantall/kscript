using KScript.Language.Catalog;
using KScript.Syntax.Markup;
using KScript.Syntax;
using KScript.Syntax.Text;

namespace KScript.Language.Analysis
{
    /// <summary>
    /// Checks a KScript document the way strict mode would, without running it: markup errors, unknown and retired
    /// elements, attributes and commands, undeclared variables, misplaced elements, expression syntax, argument
    /// counts and the type errors that can be seen from the source. It also records symbols, references, scopes and
    /// expression regions for editor features.
    /// </summary>
    public sealed partial class Analyzer
    {
        private static readonly HashSet<string> TemplateRawTextElements = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "title", "textarea" };

        private readonly LanguageCatalog catalog;
        private readonly IFileSystem files;
        private readonly DocumentAnalysis result;
        private readonly SourceText source;
        private readonly string directory;

        private Analyzer(SourceText source, string path, DocumentKind kind, IFileSystem files, LanguageCatalog catalog)
        {
            this.source = source;
            this.files = files ?? DiskFileSystem.Instance;
            this.catalog = catalog ?? LanguageCatalog.Default;
            directory = path != null ? System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)) : null;
            result = new DocumentAnalysis(source, path, kind, MarkupParser.Parse(source, kind, path));
        }

        private DocumentKind Kind => result.Kind;

        /// <summary>Analyzes a document. The kind is chosen from the path's extension unless given.</summary>
        public static DocumentAnalysis Analyze(string text, string path = null, IFileSystem files = null, LanguageCatalog catalog = null, DocumentKind? kind = null) =>
            Analyze(text, path, files, catalog, kind, projectWide: true);

        /// <summary>
        /// With <paramref name="projectWide"/> false, functions are not checked for use (KS6002), so no other file in
        /// the project is read for calls. That is how files that import this one are analysed.
        /// </summary>
        internal static DocumentAnalysis Analyze(string text, string path, IFileSystem files, LanguageCatalog catalog, DocumentKind? kind, bool projectWide)
        {
            DocumentKind documentKind = kind ?? MarkupParser.KindFromPath(path);
            Analyzer analyzer = RunToFixedPoint(() => new Analyzer(new SourceText(text), path, documentKind, files, catalog));
            analyzer.CheckUnused(projectWide);
            return analyzer.result;
        }

        #region Driver

        private void Run()
        {
            result.Diagnostics.AddRange(result.Markup.Diagnostics);
            Scope root = new Scope(null, new TextSpan(0, source.Length));
            result.RootScope = root;
            result.IsPartial = IsPartialPath(result.Path);
            Dictionary<string, TypeShape> jsonCache = new Dictionary<string, TypeShape>(StringComparer.Ordinal);
            result.JsonFileShape = relative =>
            {
                string full = ResolvePath(relative);
                if (full == null)
                {
                    return null;
                }
                if (!jsonCache.TryGetValue(full, out TypeShape shape))
                {
                    jsonCache[full] = shape = files.FileExists(full) ? JsonShapes.FromText(files.ReadText(full)) : null;
                }
                return shape;
            };

            ResolvePackages(root);
            DeclareBuiltIns(root);
            AnalyzeFrontMatter(root);
            HoistFunctions(root);
            ApplyKnownTypes(root);
            HoistTypes(root);
            CheckScriptRoot();

            WalkChildren(result.Markup.Children, root, markupMode: Kind != DocumentKind.Script);
        }

        private static bool IsPartialPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }
            // A leading / so relative paths such as _components/card.html match too.
            string normalized = "/" + path.Replace('\\', '/');
            return System.IO.Path.GetFileName(normalized).StartsWith("_", StringComparison.Ordinal)
                || normalized.Contains("/_components/", StringComparison.Ordinal)
                // KScript.Http views get their variables from the controller: @view('people/show', person: $p).
                || normalized.Contains("/views/", StringComparison.Ordinal);
        }

        private void DeclareBuiltIns(Scope root)
        {
            foreach (KeyValuePair<string, (string Type, string Description)> builtIn in catalog.BuiltInVariables)
            {
                string package = catalog.PackageOfVariable(builtIn.Key);
                bool applies = builtIn.Key switch
                {
                    "request" => Kind != DocumentKind.Script,
                    "event" => false, // declared inside <on>
                    _ => Kind == DocumentKind.Script && (package == null || importedPackages.Contains(package)),
                };
                if (applies)
                {
                    root.Declare(BuiltIn(builtIn.Key), 0);
                }
            }
        }

        private Symbol BuiltIn(string name)
        {
            (string type, string description) = catalog.BuiltInVariables[name];
            TypeShape shape = new TypeShape(type);
            foreach (KeyValuePair<string, string> field in catalog.FieldsOf(type))
            {
                shape.Fields[field.Key] = new FieldInfo(field.Key, field.Key == "query" || field.Key == "form" || field.Key == "params" || field.Key == "headers" || field.Key == "cookies"
                    ? new TypeShape(KType.Map) : TypeShape.Unknown, null, field.Value);
            }
            Symbol symbol = new Symbol { Name = name, Kind = SymbolKind.BuiltIn, Shape = shape, Description = description };
            result.Symbols.Add(symbol);
            return symbol;
        }

        private void AnalyzeFrontMatter(Scope root)
        {
            FrontMatter fm = result.Markup.FrontMatter;
            if (fm == null)
            {
                return;
            }

            foreach (FrontMatterEntry entry in fm.Entries)
            {
                Symbol symbol = new Symbol
                {
                    Name = entry.Key,
                    Kind = SymbolKind.FrontMatter,
                    DeclarationSpan = entry.KeySpan,
                    Shape = new TypeShape(KType.String),
                    Description = "Set in the front matter: " + entry.Value.Value,
                };
                result.Symbols.Add(symbol);
                result.References.Add(new SymbolReference(symbol, entry.KeySpan, true, true));
                root.Declare(symbol, fm.Span.End);

                if (entry.Key == "layout" && entry.Value.Value.Length > 0)
                {
                    CheckPath(entry.Value.Value, entry.Value.SourceSpan, "layout");
                }
            }
        }

        /// <summary>Functions can be called before they are declared, so they are declared up front.</summary>
        private void HoistFunctions(Scope root)
        {
            Dictionary<string, Symbol> seen = new Dictionary<string, Symbol>(StringComparer.Ordinal);
            foreach (MarkupElement e in result.Markup.DescendantElements())
            {
                if (IsKScriptElementName(e, "fn") && !(e.Parent is MarkupElement owner && IsKScriptElementName(owner, "type")))
                {
                    MarkupAttribute nameAttr = e.GetAttribute("name");
                    string name = nameAttr?.ValueText?.Trim();
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    TextSpan span = TrimmedSpan(nameAttr.Value);
                    if (seen.ContainsKey(name))
                    {
                        Report(span, DiagnosticSeverity.Error, DiagnosticCodes.DuplicateDeclaration, $"A function named '{name}' is already declared.");
                        continue;
                    }

                    Symbol fn = new Symbol
                    {
                        Name = name,
                        Kind = SymbolKind.Function,
                        DeclarationSpan = span,
                        DeclaringElement = e,
                        Description = e.Documentation,
                    };
                    MarkupAttribute paramsAttr = e.GetAttribute("params");
                    if (paramsAttr?.Value != null)
                    {
                        fn.Parameters.AddRange(ParseParameters(paramsAttr.Value, report: false));
                    }
                    foreach ((string docName, string _) in fn.Doc?.Params ?? Array.Empty<(string, string)>())
                    {
                        if (!fn.Parameters.Any(p => p.Name == docName))
                        {
                            Report(span, DiagnosticSeverity.Warning, DiagnosticCodes.DocParameter,
                                $"The doc comment describes @param {docName}, but {name} has no parameter called {docName}."
                                + (fn.Parameters.Count > 0 ? " Its parameters: " + string.Join(", ", fn.Parameters.Select(p => p.Name)) + "." : string.Empty));
                        }
                    }
                    seen[name] = fn;
                    result.Symbols.Add(fn);
                    result.References.Add(new SymbolReference(fn, span, true, true));
                    root.Declare(fn, 0);
                }
                else if (IsKScriptElementName(e, "import") && e.GetAttribute("package") == null)
                {
                    HoistImport(e, root);
                }
            }
        }

        private void HoistImport(MarkupElement e, Scope root)
        {
            string src = e.GetAttributeValue("src")?.Trim();
            string full = ResolvePath(src);
            Dictionary<string, (FunctionParameter[] Params, ExternalLocation Location, string Description)> exported = ReadExports(full);
            Dictionary<string, (ExternalLocation Location, string Description)> constants = ReadConstantExports(full);

            MarkupAttribute asAttr = e.GetAttribute("as");
            if (!string.IsNullOrWhiteSpace(asAttr?.ValueText))
            {
                TextSpan span = TrimmedSpan(asAttr.Value);
                Symbol alias = new Symbol
                {
                    Name = asAttr.ValueText.Trim(),
                    Kind = SymbolKind.Import,
                    DeclarationSpan = span,
                    DeclaringElement = e,
                    SourcePath = full,
                    Description = $"Imported from {src}",
                    Shape = new TypeShape(KType.Map),
                };
                if (exported != null)
                {
                    foreach (string name in exported.Keys)
                    {
                        alias.Shape.Fields.TryAdd(name, new FieldInfo(name, new TypeShape(KType.Fn), null, exported[name].Description));
                        result.ExternalMembers.TryAdd((alias, name), exported[name].Location);
                    }
                }
                if (constants != null)
                {
                    foreach (string name in constants.Keys)
                    {
                        alias.Shape.Fields.TryAdd(name, new FieldInfo(name, TypeShape.Unknown, null, constants[name].Description));
                        result.ExternalMembers.TryAdd((alias, name), constants[name].Location);
                    }
                }
                result.Symbols.Add(alias);
                result.References.Add(new SymbolReference(alias, span, true, true));
                root.Declare(alias, 0);
            }

            MarkupAttribute namesAttr = e.GetAttribute("names");
            if (namesAttr?.Value == null)
            {
                // <import src="lib.ks.xml" /> with neither as nor names brings in every exported function.
                if (string.IsNullOrWhiteSpace(asAttr?.ValueText) && exported != null)
                {
                    MarkupAttribute srcAttr = e.GetAttribute("src");
                    TextSpan at = srcAttr?.Value != null ? TrimmedSpan(srcAttr.Value) : e.NameSpan;
                    foreach (var export in exported)
                    {
                        Symbol fn = new Symbol
                        {
                            Name = export.Key,
                            Kind = SymbolKind.Function,
                            DeclarationSpan = at,
                            DeclaringElement = e,
                            SourcePath = full,
                            Description = export.Value.Description ?? $"Imported from {src}",
                        };
                        fn.Parameters.AddRange(export.Value.Params);
                        result.ExternalDeclarations[fn] = export.Value.Location;
                        result.Symbols.Add(fn);
                        root.Declare(fn, 0);
                    }
                    foreach (var constant in constants ?? new Dictionary<string, (ExternalLocation, string)>())
                    {
                        root.Declare(ImportedConstant(constant.Key, at, e, full, src, constant.Value), 0);
                    }
                }
                return;
            }

            foreach ((string name, TextSpan span) in SplitNames(namesAttr.Value))
            {
                if (constants != null && constants.TryGetValue(name, out var constant) && exported?.ContainsKey(name) != true)
                {
                    Symbol c = ImportedConstant(name, span, e, full, src, constant);
                    result.References.Add(new SymbolReference(c, span, true, true));
                    root.Declare(c, 0);
                    continue;
                }
                Symbol fn = new Symbol
                {
                    Name = name,
                    Kind = SymbolKind.Function,
                    DeclarationSpan = span,
                    DeclaringElement = e,
                    SourcePath = full,
                    Description = $"Imported from {src}",
                };
                if (exported != null)
                {
                    if (exported.TryGetValue(name, out var export))
                    {
                        fn.Parameters.AddRange(export.Params);
                        fn.Description = export.Description ?? fn.Description;
                        result.ExternalDeclarations[fn] = export.Location;
                    }
                    else
                    {
                        Report(span, DiagnosticSeverity.Error, DiagnosticCodes.UndefinedFunction, $"{src} has no function named '{name}'.",
                            SuggestFix(span, name, exported.Keys, n => n));
                    }
                }
                result.Symbols.Add(fn);
                result.References.Add(new SymbolReference(fn, span, true, true));
                root.Declare(fn, 0);
            }
        }

        private Symbol ImportedConstant(string name, TextSpan at, MarkupElement import, string full, string src, (ExternalLocation Location, string Description) constant)
        {
            Symbol symbol = new Symbol
            {
                Name = name,
                Kind = SymbolKind.Constant,
                DeclarationSpan = at,
                DeclaringElement = import,
                SourcePath = full,
                Description = constant.Description ?? $"Imported from {src}",
                Shape = TypeShape.Unknown,
            };
            result.ExternalDeclarations[symbol] = constant.Location;
            result.Symbols.Add(symbol);
            return symbol;
        }

        /// <summary>The top-level &lt;let const="yes"&gt; values of another script, which it exports along with its functions.</summary>
        private Dictionary<string, (ExternalLocation Location, string Description)> ReadConstantExports(string path)
        {
            string text = path != null ? files.ReadText(path) : null;
            if (text == null)
            {
                return null;
            }

            SourceText other = new SourceText(text);
            MarkupDocument doc = MarkupParser.Parse(other, MarkupParser.KindFromPath(path), path);
            Dictionary<string, (ExternalLocation, string)> constants = new Dictionary<string, (ExternalLocation, string)>(StringComparer.Ordinal);
            MarkupElement root = doc.RootElements.FirstOrDefault();
            if (root == null)
            {
                return constants;
            }
            foreach (MarkupElement let in root.ChildElements.Where(x => (x.Name == "let" && IsYes(x.GetAttributeValue("const"))) || x.Name == "const" || x.Name == "enum" || x.Name == "type"))
            {
                MarkupAttribute nameAttr = let.GetAttribute("name");
                if (string.IsNullOrWhiteSpace(nameAttr?.ValueText))
                {
                    continue;
                }
                TextSpan span = nameAttr.Value.SourceSpan;
                constants[nameAttr.ValueText.Trim().TrimStart('$')] = (new ExternalLocation(path, other.GetPosition(span.Start), other.GetPosition(span.End)), let.Documentation);
            }
            return constants;
        }

        /// <summary>The top-level functions of another script, read from disk (or the editor) without analysing it fully.</summary>
        private Dictionary<string, (FunctionParameter[] Params, ExternalLocation Location, string Description)> ReadExports(string path)
        {
            string text = path != null ? files.ReadText(path) : null;
            if (text == null)
            {
                return null;
            }

            SourceText other = new SourceText(text);
            MarkupDocument doc = MarkupParser.Parse(other, MarkupParser.KindFromPath(path), path);
            Dictionary<string, (FunctionParameter[], ExternalLocation, string)> exports = new Dictionary<string, (FunctionParameter[], ExternalLocation, string)>(StringComparer.Ordinal);
            foreach (MarkupElement fn in doc.DescendantElements().Where(x => x.Name == "fn"))
            {
                MarkupAttribute nameAttr = fn.GetAttribute("name");
                if (string.IsNullOrWhiteSpace(nameAttr?.ValueText))
                {
                    continue;
                }
                FunctionParameter[] ps = fn.GetAttribute("params")?.Value is DecodedText p ? ParseParameters(p, report: false).ToArray() : Array.Empty<FunctionParameter>();
                TextSpan span = nameAttr.Value.SourceSpan;
                exports[nameAttr.ValueText.Trim()] = (ps, new ExternalLocation(path, other.GetPosition(span.Start), other.GetPosition(span.End)), fn.Documentation);
            }
            return exports;
        }

        private void CheckScriptRoot()
        {
            if (Kind != DocumentKind.Script)
            {
                return;
            }

            List<MarkupElement> roots = result.Markup.RootElements.ToList();
            if (roots.Count > 1)
            {
                Report(roots[1].NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.MarkupSyntax,
                    "A script has one root element. Put everything inside <kscript>.");
            }
            if (roots.Count > 0 && roots[0].Name != "kscript" && catalog.GetElement(roots[0].Name) != null)
            {
                Report(roots[0].NameSpan, DiagnosticSeverity.Warning, DiagnosticCodes.MisplacedElement, "Scripts start with <kscript>.");
            }
        }

        #endregion

        #region Elements

        private void WalkChildren(IEnumerable<MarkupNode> nodes, Scope scope, bool markupMode)
        {
            MarkupElement previous = null;
            foreach (MarkupNode node in nodes)
            {
                switch (node)
                {
                    case MarkupElement element:
                        WalkElement(element, scope, markupMode, previous);
                        previous = element;
                        break;

                    case MarkupText text:
                        if (markupMode)
                        {
                            if (!text.IsCData || (text.Parent != null && TemplateRawTextElements.Contains(text.Parent.Name)))
                            {
                                AnalyzeTemplate(text.Content, scope, text.Parent, null, allowBare: false);
                            }
                        }
                        else if (!text.IsWhitespace)
                        {
                            Report(TrimmedSpan(text.Content), DiagnosticSeverity.Warning, DiagnosticCodes.StrayText,
                                "Text here is ignored. To print it, put it in <echo>.");
                        }
                        if (!text.IsWhitespace)
                        {
                            previous = null;
                        }
                        break;
                }
            }
        }

        /// <summary>The element's definition if it is a KScript element here, or null if it is HTML (or unknown).</summary>
        private ElementInfo ResolveElement(MarkupElement e, bool markupMode)
        {
            // <KScript.Window> is <Window> from the package that has it, and imports that package.
            string name = Kind == DocumentKind.Script && e.Name.StartsWith("KScript.", StringComparison.Ordinal) ? e.Name.Substring(8) : e.Name;
            ElementInfo info = catalog.GetElement(name);
            if (info == null)
            {
                return null;
            }
            if (!string.Equals(info.Name, name, StringComparison.Ordinal) && Kind == DocumentKind.Script)
            {
                // XML is case-sensitive; <Echo> is not <echo>.
                return null;
            }
            if (markupMode && Kind != DocumentKind.Script && (info.HtmlWins || !info.Context.HasFlag(ElementContext.Page)))
            {
                return null;
            }
            if (markupMode && Kind == DocumentKind.Script && info.HtmlWins)
            {
                // Inside <render> in a script, <input> and friends are HTML.
                return null;
            }
            return info;
        }

        private bool IsKScriptElementName(MarkupElement e, string name) =>
            string.Equals(e.Name, name, Kind == DocumentKind.Script ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

        private void WalkElement(MarkupElement e, Scope scope, bool markupMode, MarkupElement previous)
        {
            ElementInfo info = ResolveElement(e, markupMode);
            if (info == null)
            {
                WalkOtherElement(e, scope, markupMode);
                return;
            }

            CheckAttributes(e, info);
            CheckPlacement(e, info, previous);
            NoteResourceUses(e, info, scope);
            CheckElse(e, previous, markupMode);
            RequirePackage(info.Package, e.NameSpan, $"<{e.Name}>");
            if (Kind != DocumentKind.Script && !info.Context.HasFlag(ElementContext.Page))
            {
                Report(e.NameSpan, DiagnosticSeverity.Warning, DiagnosticCodes.MisplacedElement, $"<{e.Name}> only works in scripts, not in pages.");
            }
            else if (Kind == DocumentKind.Script && !info.Context.HasFlag(ElementContext.Script))
            {
                Report(e.NameSpan, DiagnosticSeverity.Warning, DiagnosticCodes.MisplacedElement, $"<{e.Name}> only works in pages served by kscript serve.");
            }

            // Elements that build their own scopes (fn, catch) must not also get an empty content scope,
            // or Scope.Innermost would find the empty one first.
            bool ownScopes = info.Name == "fn" || info.Name == "catch" || (info.Name == "test" && info.Package == "KScript.Test" && e.GetAttribute("each") != null);
            Scope inner = e.SelfClosing || !e.StartTagClosed || ownScopes ? scope : new Scope(scope, e.ContentSpan);
            List<Action> declareAfter = new List<Action>();

            switch (info.Name)
            {
                case "let":
                case "set":
                case "state":
                case "const" when info.Package == "KScript.Core":
                    AnalyzeLetOrSet(e, info.Name != "set", scope, declareAfter);
                    return;

                case "enum" when info.Package == "KScript.Core":
                    AnalyzeAttributes(e, info, scope);
                    AnalyzeEnum(e, scope);
                    return;

                case "type" when info.Package == "KScript.Core":
                    AnalyzeType(e, info, scope, inner, markupMode);
                    return;

                case "field" when info.Package == "KScript.Core":
                    AnalyzeField(e, info, scope);
                    return;

                case "match":
                {
                    // Inside <match>, $it is the value being matched (<case when="$it gt 9">).
                    AnalyzeAttributes(e, info, scope);
                    Symbol it = new Symbol { Name = "it", Kind = SymbolKind.BuiltIn, Description = "The value being matched." };
                    result.Symbols.Add(it);
                    inner.Declare(it, e.ContentSpan.Start);
                    WalkChildren(e.Children, inner, markupMode);
                    return;
                }

                case "do":
                    AnalyzeStatementsContent(e, scope, declare: false);
                    return;

                case "test" when info.Package == "KScript.Test" && e.GetAttribute("each") != null:
                    AnalyzeTestEach(e, info, scope, markupMode);
                    return;

                case "expect-error" when info.Package == "KScript.Test":
                    // <expect-error as="err"> declares the caught error after the element, shaped like <catch as>.
                    AnalyzeAttributes(e, info, scope, skip: "as");
                    WalkChildren(e.Children, inner, markupMode);
                    DeclareFromAttribute(e, "as", scope, e.Span.End, SymbolKind.Variable, ErrorShape());
                    return;

                case "for":
                    AnalyzeFor(e, scope, inner);
                    WalkChildren(e.Children, inner, markupMode);
                    return;

                case "fn":
                    AnalyzeFn(e, scope, markupMode);
                    return;

                case "catch":
                {
                    // $err is visible in the when="..." test as well as the body.
                    Scope catchScope = new Scope(scope, e.Span);
                    DeclareFromAttribute(e, "as", catchScope, e.Span.Start, SymbolKind.Variable, ErrorShape());
                    AnalyzeAttributes(e, info, catchScope, skip: "as");
                    WalkChildren(e.Children, new Scope(catchScope, e.ContentSpan), markupMode);
                    return;
                }

                case "on":
                    AnalyzeAttributes(e, info, scope);
                    inner.Declare(BuiltIn("event"), e.ContentSpan.Start);
                    WalkChildren(e.Children, inner, markupMode);
                    return;

                case "call":
                case "spawn":
                    AnalyzeAttributes(e, info, scope);
                    CheckCallArguments(e);
                    return;
            }

            AnalyzeAttributes(e, info, scope);

            // Elements that store a result: <input to>, <llm to>, <marray id>, <file_info to>, <picker to>.
            foreach (AttributeInfo a in info.Attributes.Where(a => a.Kind == AttributeKind.DeclareVariable && info.Name != "import"))
            {
                MarkupAttribute attr = e.GetAttribute(a.Name);
                if (attr?.Value != null)
                {
                    TypeShape shape = info.Name == "marray" ? MarrayShape(e) : info.Name == "llm" && e.GetAttribute("schema") != null ? new TypeShape(KType.Map) : new TypeShape(KType.String);
                    declareAfter.Add(() => DeclareFromAttribute(e, a.Name, scope, e.Span.End, SymbolKind.Variable, shape));
                }
            }

            AnalyzeContent(e, info, scope, inner, markupMode);
            foreach (Action declare in declareAfter)
            {
                declare();
            }
        }

        private void AnalyzeContent(MarkupElement e, ElementInfo info, Scope scope, Scope inner, bool markupMode)
        {
            switch (info.Content)
            {
                case ContentKind.Empty:
                    MarkupNode content = e.Children.FirstOrDefault(c => !(c is MarkupComment) && !(c is MarkupText t && t.IsWhitespace));
                    if (content != null)
                    {
                        Report(content is MarkupElement ce ? ce.NameSpan : TrimmedSpan(((MarkupText)content).Content), DiagnosticSeverity.Warning,
                            DiagnosticCodes.StrayText, $"<{e.Name}> doesn't take any content; write <{e.Name} ... />.");
                    }
                    break;

                case ContentKind.Statements:
                    WalkChildren(e.Children, inner, markupMode);
                    break;

                case ContentKind.Markup:
                    WalkChildren(e.Children, inner, markupMode: true);
                    break;

                case ContentKind.Template:
                    foreach (MarkupNode child in e.Children)
                    {
                        if (child is MarkupText json && IsLiteralJson(e, json))
                        {
                            // <echo format="json">{ "a": 1 }</echo>: JSON written out, used as it is.
                            continue;
                        }
                        if (child is MarkupText text)
                        {
                            AnalyzeTemplate(text.Content, scope, e, null, allowBare: Kind == DocumentKind.Script);
                        }
                        else if (child is MarkupElement ce)
                        {
                            WalkElement(ce, inner, markupMode: true, previous: null);
                        }
                    }
                    break;

                case ContentKind.Expression:
                    MarkupText single = e.SingleText;
                    if (single != null && !single.IsWhitespace)
                    {
                        AnalyzeExpressionRegion(single.Content, RegionKind.Expression, scope, e, null);
                    }
                    break;

                case ContentKind.AssignmentStatements:
                    AnalyzeStatementsContent(e, scope, declare: false);
                    break;
            }
        }

        /// <summary>HTML elements in pages and <c>&lt;render&gt;</c>, components, and unknown elements in scripts.</summary>
        private void WalkOtherElement(MarkupElement e, Scope scope, bool markupMode)
        {
            Scope inner = e.SelfClosing || !e.StartTagClosed ? scope : new Scope(scope, e.ContentSpan);

            if (!markupMode)
            {
                ReportUnknownElement(e);

                // Keep going inside, but don't pile up knock-on errors: a retired <def id="x"> still declares $x,
                // and text inside an unknown element isn't reported again.
                WalkChildren(e.Children.OfType<MarkupElement>(), inner, markupMode: false);
                if (catalog.GetRetiredElement(e.Name) != null && e.GetAttribute("id")?.Value is DecodedText id && !string.IsNullOrWhiteSpace(id.Value))
                {
                    DeclareFromAttribute(e, "id", scope, e.Span.End, SymbolKind.Variable, TypeShape.Unknown);
                }
                return;
            }

            if (string.Equals(e.Name, "script", StringComparison.OrdinalIgnoreCase)
                && string.Equals(e.GetAttributeValue("type")?.Trim(), "text/kscript", StringComparison.OrdinalIgnoreCase))
            {
                // A browser-side block: its content is a KScript script.
                WalkChildren(e.Children, inner, markupMode: false);
                return;
            }

            // Same rule as the page engine (KScript.Pages.PageElementNames): any non-HTML, non-KScript element can be a
            // component. Hyphenated names must have a file; other names (<hero>) are components only when one exists.
            if (Kind != DocumentKind.Script && global::KScript.Pages.PageElementNames.CanBeComponent(e.Name))
            {
                CheckComponent(e, required: e.Name.Contains('-'));
            }

            foreach (MarkupAttribute attr in e.Attributes)
            {
                if (attr.Value == null)
                {
                    continue;
                }
                if (attr.Name.StartsWith("on-", StringComparison.Ordinal) && Kind != DocumentKind.Script)
                {
                    // <button on-click="count += 1">: statements run on the server when the event fires.
                    RequireLive(e, attr);
                    // $event (value, checked, key) describes the browser event the handler runs for.
                    Scope handlerScope = new Scope(scope, e.Span);
                    handlerScope.Declare(BuiltIn("event"), e.Span.Start);
                    AnalyzeStatements(attr.Value, e, attr, handlerScope, declare: false, requireEffect: true);
                }
                else if (attr.Name == "bind" && Kind != DocumentKind.Script)
                {
                    // <input bind="query">: two-way binding to a <state> variable.
                    RequireLive(e, attr);
                    if (ReferenceVariableByName(attr.Value, scope, isWrite: true, mustBeMutable: true) is Symbol bound)
                    {
                        boundVariables.Add(bound);
                    }
                }
                else
                {
                    // Components get the value of a lone { expr } (person="{$p}"); anything else is text.
                    AnalyzeTemplate(attr.Value, scope, e, attr, allowBare: false);
                }
            }

            WalkChildren(e.Children, inner, markupMode: true);
        }

        private void ReportUnknownElement(MarkupElement e)
        {
            RetiredInfo retired = catalog.GetRetiredElement(e.Name);
            if (retired != null)
            {
                List<QuickFix> fixes = new List<QuickFix>();
                if (retired.Rename != null)
                {
                    fixes.Add(RenameElementFix(e, retired.Rename));
                }
                Report(e.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.RetiredElement, $"<{e.Name}> was removed in KScript 2. {retired.Replacement}", fixes);
                return;
            }

            IEnumerable<string> candidates = catalog.Elements.Where(x => x.Context.HasFlag(ElementContext.Script)).Select(x => x.Name);
            string suggestion = Suggestions.Closest(e.Name, candidates);
            Report(e.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.UnknownElement,
                $"Unknown element <{e.Name}>." + (suggestion != null ? $" Did you mean <{suggestion}>?" : string.Empty),
                suggestion != null ? new[] { RenameElementFix(e, suggestion) } : null);
        }

        private static QuickFix RenameElementFix(MarkupElement e, string name)
        {
            List<TextChange> changes = new List<TextChange> { new TextChange(e.NameSpan, name) };
            if (e.EndNameSpan.HasValue)
            {
                changes.Add(new TextChange(e.EndNameSpan.Value, name));
            }
            return new QuickFix($"Change to <{name}>", changes.ToArray());
        }

        private void CheckComponent(MarkupElement e, bool required)
        {
            string componentDir = FindUpwards("_components");
            if (componentDir == null)
            {
                return;
            }

            string file = System.IO.Path.Combine(componentDir, e.Name.ToLowerInvariant() + ".html");
            if (files.FileExists(file))
            {
                result.Links.Add(new DocumentLink(e.NameSpan, file, "component"));
                return;
            }
            if (!required)
            {
                return;
            }

            IEnumerable<string> known = files.ListFiles(componentDir, "*.html").Select(f => System.IO.Path.GetFileNameWithoutExtension(f));
            string suggestion = Suggestions.Closest(e.Name, known);
            Report(e.NameSpan, DiagnosticSeverity.Warning, DiagnosticCodes.UnknownElement,
                $"No component named <{e.Name}>: _components/{e.Name}.html doesn't exist." + (suggestion != null ? $" Did you mean <{suggestion}>?" : string.Empty),
                suggestion != null ? new[] { RenameElementFix(e, suggestion) } : null);
        }

        private string FindUpwards(string folderName)
        {
            for (string dir = directory; dir != null; dir = System.IO.Path.GetDirectoryName(dir))
            {
                string candidate = System.IO.Path.Combine(dir, folderName);
                if (files.DirectoryExists(candidate))
                {
                    return candidate;
                }
            }
            return null;
        }

        private void CheckAttributes(MarkupElement e, ElementInfo info)
        {
            foreach (MarkupAttribute attr in e.Attributes)
            {
                AttributeInfo a = info.GetAttribute(attr.Name);
                if (a != null)
                {
                    CheckAttributeValue(attr, a);
                    continue;
                }

                RetiredInfo retired = catalog.GetRetiredAttribute(attr.Name);
                if (retired != null)
                {
                    Report(attr.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.RetiredAttribute, $"The '{attr.Name}' attribute was removed in KScript 2. {retired.Replacement}",
                        retired.Rename != null && info.GetAttribute(retired.Rename) != null && e.GetAttribute(retired.Rename) == null
                            ? new[] { new QuickFix($"Change to {retired.Rename}", new TextChange(attr.NameSpan, retired.Rename)) }
                            : null);
                    continue;
                }

                if (info.AllowsExtraAttributes || attr.Name.StartsWith("xmlns", StringComparison.Ordinal))
                {
                    continue;
                }

                string suggestion = Suggestions.Closest(attr.Name, info.Attributes.Select(x => x.Name));
                Report(attr.NameSpan, DiagnosticSeverity.Warning, DiagnosticCodes.UnknownAttribute,
                    $"<{e.Name}> has no '{attr.Name}' attribute." + (suggestion != null ? $" Did you mean '{suggestion}'?" : AttributeList(info)),
                    suggestion != null ? new[] { new QuickFix($"Change to {suggestion}", new TextChange(attr.NameSpan, suggestion)) } : null);
            }

            foreach (AttributeInfo a in info.Attributes.Where(a => a.Required))
            {
                if (e.GetAttribute(a.Name) == null && !RequiredSatisfiedOtherwise(e, info, a))
                {
                    Report(e.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.MissingAttribute, $"<{e.Name}> needs the '{a.Name}' attribute: {a.Description}");
                }
            }
        }

        private static string AttributeList(ElementInfo info) =>
            info.Attributes.Count == 0 ? $" <{info.Name}> takes no attributes." : $" It takes: {string.Join(", ", info.Attributes.Select(a => a.Name))}.";

        /// <summary>A retired attribute that maps onto the missing one (condition= for test=) has already been reported.</summary>
        private bool RequiredSatisfiedOtherwise(MarkupElement e, ElementInfo info, AttributeInfo a) =>
            e.Attributes.Any(x => catalog.GetRetiredAttribute(x.Name)?.Rename == a.Name);

        private void CheckAttributeValue(MarkupAttribute attr, AttributeInfo a)
        {
            string value = attr.ValueText?.Trim();
            if (value == null || value.Contains('{'))
            {
                return;
            }

            if (a.Kind == AttributeKind.Enum && a.Options.Count > 0 && !a.Options.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                string suggestion = Suggestions.Closest(value, a.Options);
                Report(TrimmedSpan(attr.Value), DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue,
                    $"'{value}' isn't a valid {a.Name}. Use one of: {string.Join(", ", a.Options)}.",
                    suggestion != null ? new[] { new QuickFix($"Change to {suggestion}", new TextChange(TrimmedSpan(attr.Value), suggestion)) } : null);
            }
            else if (a.Kind == AttributeKind.Bool && KScript.Runtime.Values.ParseBool(value) == null)
            {
                Report(TrimmedSpan(attr.Value), DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue, $"'{a.Name}' takes yes or no, not '{value}'.");
            }
        }

        private void CheckPlacement(MarkupElement e, ElementInfo info, MarkupElement previous)
        {
            if (info.MustFollow != null && info.MustFollow.Count > 0)
            {
                if (previous == null || !info.MustFollow.Contains(previous.Name, StringComparer.OrdinalIgnoreCase))
                {
                    Report(e.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.MisplacedElement,
                        $"<{e.Name}> must come straight after {string.Join(" or ", info.MustFollow.Select(n => "<" + n + ">"))}.");
                }
            }

            if (info.MustBeInside != null && info.MustBeInside.Count > 0)
            {
                bool ok = info.MustBeInside.Contains("fn") || info.InsideAnyAncestor
                    ? e.Ancestors().Any(a => info.MustBeInside.Contains(ElementInfo.BareName(a.Name))
                        || (info.MustBeInside.Contains("fn") && catalog.GetElement(ElementInfo.BareName(a.Name))?.IsFunctionBody == true))
                    : e.Parent != null && info.MustBeInside.Contains(e.Parent.Name, StringComparer.OrdinalIgnoreCase);
                if (!ok)
                {
                    Report(e.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.MisplacedElement,
                        $"<{e.Name}> must be inside {string.Join(" or ", info.MustBeInside.Select(n => "<" + n + ">"))}.");
                }
            }

            if (info.Name == "break" || info.Name == "continue")
            {
                bool inLoop = false;
                foreach (MarkupElement a in e.Ancestors())
                {
                    if (a.Name == "fn")
                    {
                        break;
                    }
                    if (catalog.GetElement(a.Name)?.IsLoop == true)
                    {
                        inLoop = true;
                        break;
                    }
                }
                if (!inLoop)
                {
                    Report(e.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.MisplacedElement, $"<{e.Name}/> is only allowed inside a loop (<for> or <while>).");
                }
            }
        }

        #endregion

        #region Specific elements

        private void AnalyzeAttributes(MarkupElement e, ElementInfo info, Scope scope, string skip = null)
        {
            foreach (MarkupAttribute attr in e.Attributes)
            {
                if (attr.Value == null || attr.Name == skip)
                {
                    continue;
                }

                AttributeInfo a = info.GetAttribute(attr.Name);
                if (a == null)
                {
                    if (info.AllowsExtraAttributes)
                    {
                        AnalyzeTemplate(attr.Value, scope, e, attr, allowBare: false);
                    }
                    continue;
                }

                AnalyzeAttribute(e, attr, a, scope);
            }
        }

        private void AnalyzeAttribute(MarkupElement e, MarkupAttribute attr, AttributeInfo a, Scope scope)
        {
            switch (a.Kind)
            {
                case AttributeKind.Expression:
                    AnalyzeExpressionRegion(attr.Value, RegionKind.Expression, scope, e, attr);
                    break;
                case AttributeKind.ExpressionList:
                    AnalyzeExpressionRegion(attr.Value, RegionKind.ExpressionList, scope, e, attr);
                    break;
                case AttributeKind.Template:
                case AttributeKind.Text when attr.ValueText.Contains('{') && !a.Literal:
                    AnalyzeTemplate(attr.Value, scope, e, attr, allowBare: false);
                    break;
                case AttributeKind.VariableReference:
                    Symbol target = ReferenceVariableByName(attr.Value, scope, isWrite: true, mustBeMutable: true);
                    if (target != null && attr.Name == "bind")
                    {
                        // A bound control shows the value as well as changing it.
                        boundVariables.Add(target);
                    }
                    break;
                case AttributeKind.FunctionReference:
                    ReferenceFunctionByName(attr.Value, scope);
                    break;
                case AttributeKind.FunctionReferences:
                    if (e.Name != "import")
                    {
                        foreach ((string name, TextSpan span) in SplitNames(attr.Value))
                        {
                            // Middleware lists (KScript.Http) also take built-ins and groups, with an argument: throttle:30.
                            int colon = a.Options.Count > 0 ? name.IndexOf(':') : -1;
                            string bare = colon > 0 ? name.Substring(0, colon).TrimEnd() : name;
                            if (a.Options.Contains(bare) || (a.Options.Count > 0 && MiddlewareGroups().Contains(bare)))
                            {
                                continue;
                            }
                            ReferenceFunction(bare, new TextSpan(span.Start, bare.Length), scope);
                        }
                    }
                    break;
                case AttributeKind.Path:
                    CheckPath(attr.ValueText.Trim(), TrimmedSpan(attr.Value), e.Name);
                    break;
                case AttributeKind.DeclareVariable:
                    CheckIdentifier(attr.Value);
                    break;
                case AttributeKind.TypeName:
                    CheckTypeAttribute(attr);
                    break;
                case AttributeKind.Statements:
                {
                    Scope handlerScope = new Scope(scope, e.Span);
                    handlerScope.Declare(BuiltIn("event"), e.Span.Start);
                    AnalyzeStatements(attr.Value, e, attr, handlerScope, declare: false, requireEffect: true);
                    break;
                }
            }
        }

        private void AnalyzeLetOrSet(MarkupElement e, bool isLet, Scope scope, List<Action> unused)
        {
            ElementInfo info = catalog.GetElement(e.Name);
            MarkupAttribute nameAttr = e.GetAttribute("name");
            MarkupAttribute valueAttr = e.GetAttribute("value");
            MarkupText text = e.SingleText;
            bool hasText = text != null && !text.IsWhitespace;

            foreach (MarkupElement child in e.ChildElements)
            {
                Report(child.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.MisplacedElement, $"<{e.Name}> holds a value or assignments, not elements.");
            }

            if (nameAttr == null)
            {
                if (valueAttr != null || e.GetAttribute("op") != null)
                {
                    Report(e.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.MissingAttribute, $"<{e.Name}> needs a 'name' for its value.");
                }
                foreach (MarkupAttribute attr in e.Attributes.Where(a => a.Name != "name" && a.Name != "value" && a.Name != "op"))
                {
                    if (attr.Value != null && info.GetAttribute(attr.Name) is AttributeInfo ai)
                    {
                        AnalyzeAttribute(e, attr, ai, scope);
                    }
                }
                if (!hasText)
                {
                    Report(e.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.MissingAttribute,
                        isLet ? "<let> needs a name and a value: <let name=\"x\" value=\"3\" /> or <let>x = 3</let>."
                              : "<set> needs a name and a value: <set name=\"x\" value=\"3\" /> or <set>x = 3</set>.");
                    return;
                }
                AnalyzeStatementsContent(e, scope, declare: isLet);
                return;
            }

            // name="..." form: the value is the attribute or the text, evaluated before the name exists.
            TypeShape shape = TypeShape.Unknown;
            if (valueAttr != null && hasText)
            {
                Report(valueAttr.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttribute, $"<{e.Name}> has both a value attribute and text. Use one.");
            }

            ExpressionRegion region = null;
            string declaredType = e.GetAttributeValue("type")?.Trim();
            if (valueAttr?.Value != null && CheckTypedValue(valueAttr.Value, declaredType, scope, e.Span.Start))
            {
                // Raw text for its type: @kyle for a handle, red for an enum.
            }
            else if (valueAttr?.Value != null)
            {
                region = AnalyzeExpressionRegion(valueAttr.Value, RegionKind.Expression, scope, e, valueAttr);
            }
            else if (hasText && CheckTypedValue(text.Content, declaredType, scope, e.Span.Start))
            {
            }
            else if (hasText)
            {
                region = AnalyzeExpressionRegion(text.Content, RegionKind.Expression, scope, e, null);
            }
            else if (isLet)
            {
                Report(e.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.MissingAttribute, "<let> needs a value: a value attribute or text.");
            }
            else
            {
                Report(e.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.MissingAttribute, "<set> needs a value: a value attribute or text.");
            }

            if (region?.Parse?.Root != null)
            {
                shape = Infer(region.Parse.Root, region);
            }

            foreach (MarkupAttribute attr in e.Attributes.Where(a => a.Name != "name" && a.Name != "value"))
            {
                if (attr.Value != null && info.GetAttribute(attr.Name) is AttributeInfo ai)
                {
                    AnalyzeAttribute(e, attr, ai, scope);
                }
            }

            string type = e.GetAttributeValue("type")?.Trim();
            if (type != null)
            {
                shape = TypeNamed(type, e.Span.Start) ?? TypeShape.Unknown;
            }

            if (nameAttr.Value == null)
            {
                return;
            }

            if (isLet)
            {
                bool isConst = e.Name == "const" || IsYes(e.GetAttributeValue("const"));
                Symbol declared = DeclareFromAttribute(e, "name", scope, e.Span.End, isConst ? SymbolKind.Constant : SymbolKind.Variable, shape);
                if (declared != null && region?.Parse?.Root is LambdaNode lambda)
                {
                    declared.Description = "fn(" + string.Join(", ", lambda.Parameters.Select(p => "$" + p.Name)) + ")";
                }
                if (declared != null && e.Documentation is string doc)
                {
                    declared.Description = declared.Description != null ? declared.Description + "\n\n" + doc : doc;
                }
            }
            else
            {
                Symbol assigned = ReferenceVariableByName(nameAttr.Value, scope, isWrite: true, mustBeMutable: true);
                string op = e.GetAttributeValue("op")?.Trim();
                if (op == null || op == "=")
                {
                    Widen(assigned, shape);
                }
            }
        }

        /// <summary>&lt;enum name="Size" values="small, medium, large" /&gt;: a constant map of its values, usable as a parameter type.</summary>
        private void AnalyzeEnum(MarkupElement e, Scope scope)
        {
            MarkupAttribute valuesAttr = e.GetAttribute("values");
            var values = new List<string>();
            if (valuesAttr?.Value != null)
            {
                string text = valuesAttr.Value.Value;
                int start = 0;
                for (int i = 0; i <= text.Length; i++)
                {
                    if (i < text.Length && text[i] != ',')
                    {
                        continue;
                    }
                    string part = text.Substring(start, i - start);
                    string value = part.Trim();
                    int at = start + (part.Length - part.TrimStart().Length);
                    start = i + 1;
                    if (value.Length == 0)
                    {
                        continue;
                    }
                    TextSpan span = valuesAttr.Value.ToSource(new TextSpan(at, value.Length));
                    if (!Lexer.IsIdentStart(value[0]) || !value.All(Lexer.IsIdentChar))
                    {
                        Report(span, DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue, $"'{value}' can't be an enum value: use letters, digits and _, starting with a letter.");
                    }
                    else if (values.Contains(value, StringComparer.OrdinalIgnoreCase))
                    {
                        Report(span, DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue, $"'{value}' is listed twice.");
                    }
                    else
                    {
                        values.Add(value);
                    }
                }
            }

            TypeShape shape = new TypeShape(KType.Map);
            foreach (string v in values)
            {
                shape.Fields[v] = new FieldInfo(v, new TypeShape(KType.String), null, $"A value of {e.GetAttributeValue("name")?.Trim()}.");
            }
            Symbol declared = DeclareFromAttribute(e, "name", scope, e.Span.End, SymbolKind.Constant, shape);
            if (declared != null)
            {
                declared.EnumValues = values;
                declared.Description = (e.GetAttributeValue("description") is string d ? d + "\n\n" : string.Empty) + "enum: " + string.Join(", ", values);
            }
        }

        private void RequireLive(MarkupElement e, MarkupAttribute attr)
        {
            if (!e.Ancestors().Any(a => a.Name == "live"))
            {
                Report(attr.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.MisplacedElement,
                    $"{attr.Name} only works inside a <live> block, e.g. <live id=\"counter\">...</live>.");
            }
        }

        private void AnalyzeStatementsContent(MarkupElement e, Scope scope, bool declare)
        {
            MarkupText text = e.SingleText;
            if (text == null || text.IsWhitespace)
            {
                if (e.Name == "do")
                {
                    Report(e.NameSpan, DiagnosticSeverity.Warning, DiagnosticCodes.StatementSyntax, "<do> is empty.");
                }
                return;
            }
            AnalyzeStatements(text.Content, e, null, scope, declare, requireEffect: e.Name == "do");
        }

        /// <summary>Assignments and calls: the content of let/set/do, and on-* handlers in pages.</summary>
        private void AnalyzeStatements(DecodedText content, MarkupElement e, MarkupAttribute attr, Scope scope, bool declare, bool requireEffect)
        {
            if (string.IsNullOrWhiteSpace(content.Value))
            {
                return;
            }
            ExpressionRegion region = NewRegion(RegionKind.Statements, content, e, attr, scope, ExpressionParser.ParseStatements(content.Value));
            ReportSyntaxErrors(region, region.Parse.Errors);

            foreach (ExprNode node in region.Parse.Nodes)
            {
                if (node is AssignmentNode assign)
                {
                    TypeShape shape = Infer(assign.Value, region);
                    AnalyzeExpr(assign.Value, region, scope, ExprContext.Value);
                    AnalyzeAssignmentTarget(assign, region, scope);
                    TextSpan nameSpan = NameSpanOf(assign, region);
                    int after = region.Source.ToSource(assign.Span.End);

                    if (assign.Name == null)
                    {
                        continue;
                    }

                    bool plain = assign.Operator == "=" && (assign.Target is IdentifierNode || assign.Target is VariableNode);
                    // In a .kscript file, x = 3 in a <do> declares x when it isn't in scope yet.
                    bool declareNew = !declare && plain && e.Name == "do" && attr == null && result.Markup.Syntax == SourceSyntax.Code
                        && scope.Lookup(assign.Name, nameSpan.Start) == null;
                    if (declare && plain || declareNew)
                    {
                        Symbol symbol = NewVariable(assign.Name, nameSpan, e, SymbolKind.Variable, shape);
                        scope.Declare(symbol, after);
                    }
                    else
                    {
                        Symbol assigned = ReferenceVariable(assign.Name, nameSpan, scope, isWrite: true, mustBeMutable: true, offset: nameSpan.Start);
                        if (assign.Operator == "=" && (assign.Target is IdentifierNode || assign.Target is VariableNode))
                        {
                            Widen(assigned, shape);
                        }
                    }
                }
                else
                {
                    if (requireEffect)
                    {
                        AnalyzeExpr(node, region, scope, ExprContext.Value);
                    }
                    bool isCall = node is CommandCallNode || node is MethodCallNode || node is CallNode || node is PipeNode;
                    if (!requireEffect || !isCall)
                    {
                        Report(region.Source.ToSource(node.Span), DiagnosticSeverity.Error, DiagnosticCodes.StatementSyntax,
                            requireEffect ? "This statement does nothing. Assign it (x = ...) or call something." : "Expected an assignment such as x = 3.");
                    }
                }
            }
        }

        private void AnalyzeAssignmentTarget(AssignmentNode assign, ExpressionRegion region, Scope scope)
        {
            // Index and member parts of the target ($x[$i].total = ...) are ordinary expressions.
            ExprNode t = assign.Target;
            while (t is MemberNode || t is IndexNode)
            {
                if (t is IndexNode ix)
                {
                    AnalyzeExpr(ix.Index, region, scope, ExprContext.Value);
                    t = ix.Target;
                }
                else
                {
                    t = ((MemberNode)t).Target;
                }
            }
        }

        private static TextSpan NameSpanOf(AssignmentNode assign, ExpressionRegion region)
        {
            TextSpan local = assign.NameSpan;
            if (assign.Target is VariableNode || (assign.NameSpan.Length > 0 && region.Source.Value[assign.NameSpan.Start] == '$'))
            {
                local = TextSpan.FromBounds(local.Start + 1, local.End);
            }
            return region.Source.ToSource(local);
        }

        /// <summary>
        /// &lt;test each="case" in="[...]" name="… {$case.x}"&gt;: the item (and index) are visible in the name, the
        /// other attributes and the body, like a loop variable.
        /// </summary>
        private void AnalyzeTestEach(MarkupElement e, ElementInfo info, Scope outer, bool markupMode)
        {
            TypeShape itemShape = TypeShape.Unknown;
            MarkupAttribute inAttr = e.GetAttribute("in");
            if (inAttr?.Value != null)
            {
                ExpressionRegion region = AnalyzeExpressionRegion(inAttr.Value, RegionKind.Expression, outer, e, inAttr);
                if (region.Parse.Root != null)
                {
                    TypeShape collection = Infer(region.Parse.Root, region);
                    itemShape = collection.Type == KType.List ? collection.Element ?? TypeShape.Unknown : TypeShape.Unknown;
                }
            }

            Scope testScope = new Scope(outer, e.Span);
            DeclareFromAttribute(e, "each", testScope, e.Span.Start, SymbolKind.LoopVariable, itemShape);
            DeclareFromAttribute(e, "index", testScope, e.Span.Start, SymbolKind.LoopVariable, new TypeShape(KType.Number));

            foreach (MarkupAttribute attr in e.Attributes.Where(a => a.Name != "in" && a.Name != "each" && a.Name != "index"))
            {
                if (attr.Value != null && info.GetAttribute(attr.Name) is AttributeInfo ai)
                {
                    AnalyzeAttribute(e, attr, ai, testScope);
                }
            }
            WalkChildren(e.Children, e.SelfClosing || !e.StartTagClosed ? testScope : new Scope(testScope, e.ContentSpan), markupMode);
        }

        private void AnalyzeFor(MarkupElement e, Scope outer, Scope body)
        {
            ElementInfo info = catalog.GetElement("for");
            TypeShape itemShape = TypeShape.Unknown;
            TypeShape collection = TypeShape.Unknown;

            MarkupAttribute inAttr = e.GetAttribute("in");
            if (inAttr?.Value != null)
            {
                ExpressionRegion region = AnalyzeExpressionRegion(inAttr.Value, RegionKind.Expression, outer, e, inAttr);
                if (region.Parse.Root != null)
                {
                    collection = Infer(region.Parse.Root, region);
                    itemShape = collection.Type == KType.List ? collection.Element ?? TypeShape.Unknown : TypeShape.Unknown;
                }
            }

            MarkupAttribute each = e.GetAttribute("each");
            if (each?.Value != null)
            {
                List<(string Name, TextSpan Span)> names = SplitNames(each.Value).ToList();
                if (names.Count > 2)
                {
                    Report(TrimmedSpan(each.Value), DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue, "each takes one name, or two (key, value) for maps.");
                }
                for (int i = 0; i < names.Count; i++)
                {
                    TypeShape shape = names.Count == 2 ? (i == 0 ? new TypeShape(collection.Type == KType.List ? KType.Number : KType.String) : TypeShape.Unknown) : itemShape;
                    Symbol symbol = NewVariable(names[i].Name, names[i].Span, e, SymbolKind.LoopVariable, shape);
                    body.Declare(symbol, e.ContentSpan.Start);
                }
            }

            DeclareFromAttribute(e, "index", body, e.ContentSpan.Start, SymbolKind.LoopVariable, new TypeShape(KType.Number));
        }

        private void AnalyzeFn(MarkupElement e, Scope outer, bool markupMode)
        {
            ElementInfo info = catalog.GetElement("fn");
            Scope body = new Scope(outer, e.Span);

            MarkupAttribute paramsAttr = e.GetAttribute("params");
            if (paramsAttr?.Value != null)
            {
                List<Symbol> declared = new List<Symbol>();
                foreach (FunctionParameter p in ParseParameters(paramsAttr.Value, report: true))
                {
                    Symbol symbol = NewVariable(p.Name, p.Span, e, SymbolKind.Parameter, ParameterShape(p, e.Span.Start));
                    body.Declare(symbol, e.Span.Start);
                    declared.Add(symbol);
                }
                functionParameters[e] = declared;
            }

            foreach (MarkupAttribute attr in e.Attributes.Where(a => a.Name != "params" && a.Name != "name"))
            {
                if (attr.Value != null && info.GetAttribute(attr.Name) is AttributeInfo ai)
                {
                    AnalyzeAttribute(e, attr, ai, body);
                }
            }

            CheckIdentifier(e.GetAttribute("name")?.Value);

            bool hasBody = e.Children.Any(c => c is MarkupElement || (c is MarkupText t && !t.IsWhitespace));
            if (e.GetAttribute("value") != null && hasBody)
            {
                Report(e.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttribute, "<fn> has both a value and a body. Use one.");
            }

            WalkChildren(e.Children, new Scope(body, e.ContentSpan), markupMode);
            RecordReturnShape(e);
        }

        private void CheckCallArguments(MarkupElement e)
        {
            string name = e.GetAttributeValue("fn")?.Trim();
            Symbol fn = name != null ? result.RootScope.Lookup(name, int.MaxValue) : null;
            if (fn?.Kind != SymbolKind.Function || fn.SourcePath != null && fn.Parameters.Count == 0)
            {
                return;
            }

            MarkupAttribute args = e.GetAttribute("args");
            int count = 0;
            if (args?.Value != null && !string.IsNullOrWhiteSpace(args.ValueText))
            {
                count = ExpressionParser.ParseExpressionList(args.ValueText).Nodes.Count;
            }
            CheckFunctionArgumentCount(fn, count, args != null ? TrimmedSpan(args.Value) : TrimmedSpan(e.GetAttribute("fn").Value));
        }

        private static TypeShape ErrorShape()
        {
            TypeShape shape = new TypeShape(KType.Error);
            foreach (string f in new[] { "type", "message", "line" })
            {
                shape.Fields[f] = new FieldInfo(f, new TypeShape(f == "line" ? KType.Number : KType.String), null);
            }
            TypeShape problem = new TypeShape(KType.Map);
            foreach (string f in new[] { "path", "field", "message" })
            {
                problem.Fields[f] = new FieldInfo(f, new TypeShape(KType.String), null);
            }
            shape.Fields["problems"] = new FieldInfo("problems", new TypeShape(KType.List) { Element = problem }, null,
                "For a Shape error (data that doesn't fit a <type>): each problem's JSON path, field and message.");
            return shape;
        }

        private TypeShape MarrayShape(MarkupElement e)
        {
            List<MarkupElement> items = e.ChildElements.Where(c => c.Name == "item").ToList();
            bool keyed = items.Count > 0 && items.All(i => i.GetAttribute("key") != null);
            if (keyed)
            {
                TypeShape map = new TypeShape(KType.Map);
                foreach (MarkupElement item in items)
                {
                    MarkupAttribute key = item.GetAttribute("key");
                    map.Fields.TryAdd(key.ValueText, new FieldInfo(key.ValueText, TypeShape.Unknown, key.Value.SourceSpan));
                }
                return map;
            }
            return new TypeShape(KType.List);
        }

        #endregion
    }
}
