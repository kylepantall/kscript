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
        /// <summary>The handler shorthand (call) whose value is being checked, or null.</summary>
        private string handlerShorthand;

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
            CodeArguments.Normalise(result.Markup, this.catalog.GetElement);
        }

        private bool inComponentHtml;
        private DocumentKind Kind => inComponentHtml ? DocumentKind.Page : result.Kind;

        private bool IsComponentPage => result.Kind != DocumentKind.Script && result.Path != null
            && string.Equals(directory, ComponentFiles.Folder(result.Path, files), StringComparison.Ordinal);

        /// <summary>Analyzes a document. The kind is chosen from the path's extension unless given.</summary>
        public static DocumentAnalysis Analyze(string text, string path = null, IFileSystem files = null, LanguageCatalog catalog = null, DocumentKind? kind = null, Rules.DocumentRules rules = null)
        {
            if (Rules.DocumentRules.IsRuleFile(path)) return Rules.DocumentRules.Analyze(text, path);
            DocumentAnalysis document = Analyze(text, path, files, catalog, kind, projectWide: true);
            rules?.Check(document);
            return document;
        }

        /// <summary>
        /// Analyses one file without reading the rest of the project for calls to its functions: for listing what a
        /// file declares (as the docs' project page does), where unused-function warnings don't matter.
        /// </summary>
        public static DocumentAnalysis AnalyzeFile(string text, string path, IFileSystem files = null, LanguageCatalog catalog = null) =>
            Analyze(text, path, files, catalog, null, projectWide: false);

        /// <summary>
        /// With <paramref name="projectWide"/> false, functions are not checked for use (KS6002), so no other file in
        /// the project is read for calls. That is how files that import this one are analysed.
        /// </summary>
        internal static DocumentAnalysis Analyze(string text, string path, IFileSystem files, LanguageCatalog catalog, DocumentKind? kind, bool projectWide)
        {
            DocumentKind documentKind = kind ?? MarkupParser.KindFromPath(path);
            Analyzer analyzer = RunToFixedPoint(() => new Analyzer(new SourceText(text), path, documentKind, files, catalog));
            analyzer.ReportDeferred();
            FunctionErrors.Infer(analyzer.result, analyzer.importedErrors);
            analyzer.CheckSpellings();
            analyzer.CheckNamingStyle();
            analyzer.CheckDuplicateLiteralPatterns();
            analyzer.CheckUnused(projectWide);
            analyzer.SpeakCode();
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
            DeclareViewInputs(root);
            DeclareTemplateContract(root);
            AnalyzeFrontMatter(root);
            HoistFunctions(root);
            HoistProjectNatives(root);
            HoistProjectModels(root);
            ApplyKnownTypes(root);
            HoistTypes(root);
            HoistExtensions(root);
            HoistErrors(root);
            HoistJobs(root);
            CheckScriptRoot();
            CheckOneTypePerFile();
            CheckPolicies();

            WalkChildren(result.Markup.Children, root, markupMode: Kind != DocumentKind.Script);
            WalkTestBodies();
        }

        private static bool IsPartialPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }
            // A leading / so relative paths such as _components/card.html match too.
            string normalized = "/" + path.Replace('\\', '/');
            // Files whose name or folder starts with _ (_layout.html, _components/, _layouts/, _partials/) are never
            // served on their own, so the page that uses them gives them their variables.
            return normalized.Split('/').Any(part => part.StartsWith("_", StringComparison.Ordinal))
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
                    "request" => Kind != DocumentKind.Script || importedPackages.Contains("KScript.Http"),
                    "event" => false, // declared inside <on>
                    _ => Kind == DocumentKind.Script ? package == null || importedPackages.Contains(package) : package != null && pageImports.Contains(package),
                };
                if (applies)
                {
                    root.Declare(BuiltIn(builtIn.Key, builtIn.Key == "request" ? RequestContextFields() : null), 0);
                }
            }
        }

        /// <summary>
        /// A view gets its variables from the scripts that render it: <c>@view('auth/account', data: { user: @user() })</c>
        /// declares $data in views/auth/account.html, typed from the call, so its fields are offered and checked.
        /// </summary>
        private void DeclareViewInputs(Scope root)
        {
            if (Kind == DocumentKind.Script || result.Path == null)
            {
                return;
            }
            foreach (KeyValuePair<string, List<ViewInput>> given in ViewInputs.Find(result.Path, files, catalog))
            {
                ViewInput first = given.Value[0];
                string from = string.Join(", ", given.Value.Select(i => i.Function != null ? i.Function + "()" : null).Where(f => f != null).Distinct());
                Symbol symbol = new Symbol
                {
                    Name = given.Key,
                    Kind = SymbolKind.BuiltIn,
                    Shape = Unify(given.Value.Select(i => i.Shape)),
                    Description = $"Given to this view by @view in {RelativeToHere(first.Path)}" + (from.Length > 0 ? $" ({from})." : "."),
                };
                result.Symbols.Add(symbol);
                result.ExternalDeclarations[symbol] = new ExternalLocation(first.Path, first.Start, first.End);
                root.Declare(symbol, 0);
            }
        }

        private string RelativeToHere(string path) => System.IO.Path.GetRelativePath(SiteRoot(), path).Replace('\\', '/');

        private Symbol BuiltIn(string name, IEnumerable<ValueFieldInfo> extraFields = null)
        {
            (string type, string description) = catalog.BuiltInVariables[name];
            TypeShape shape = ShapeWithFields(type, catalog.FieldsOf(type).Values.Concat(extraFields ?? Enumerable.Empty<ValueFieldInfo>()));
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
                    foreach ((string name, bool required) in TemplateContracts.Read(files, ResolvePath(entry.Value.Value)))
                    {
                        if (required && !fm.Entries.Any(p => global::KScript.Pages.TemplateContract.VariableName(p.Key) == global::KScript.Pages.TemplateContract.VariableName(name)))
                            Report(entry.Value.SourceSpan, DiagnosticSeverity.Error, DiagnosticCodes.MissingAttribute,
                                $"Pass the required '{name}' prop to {entry.Value.Value} in front matter.");
                    }
                }
            }
        }

        /// <summary>Functions can be called before they are declared, so they are declared up front.</summary>
        private void HoistFunctions(Scope root)
        {
            Dictionary<string, Symbol> seen = new Dictionary<string, Symbol>(StringComparer.Ordinal);
            foreach (MarkupElement e in result.Markup.DescendantElements())
            {
                if (IsKScriptElementName(e, "fn") && !(e.Parent is MarkupElement owner && (IsTypeElement(owner) || IsKScriptElementName(owner, "job") || IsKScriptElementName(owner, "extend") || IsKScriptElementName(owner, "policy"))))
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
                                $"The doc comment describes a parameter called {docName}, but {name} has no parameter by that name."
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
            string src = e.GetAttributeValue("path")?.Trim();
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
                        Symbol fn = new Symbol
                        {
                            Name = name,
                            Kind = SymbolKind.Function,
                            DeclarationSpan = span,
                            DeclaringElement = e,
                            SourcePath = full,
                            Alias = alias,
                            Description = exported[name].Description ?? $"Imported from {src}",
                        };
                        fn.Parameters.AddRange(exported[name].Params);
                        result.ExternalDeclarations[fn] = exported[name].Location;
                        alias.Members.TryAdd(name, fn);
                    }
                }
                if (constants != null)
                {
                    foreach (string name in constants.Keys)
                    {
                        alias.Shape.Fields.TryAdd(name, new FieldInfo(name, TypeShape.Unknown, null, constants[name].Description));
                        result.ExternalMembers.TryAdd((alias, name), constants[name].Location);
                        if (!alias.Members.ContainsKey(name))
                        {
                            Symbol constant = MemberConstant(name, span, e, full, src, constants[name]);
                            constant.Alias = alias;
                            alias.Members[name] = constant;
                        }
                    }
                }
                result.Symbols.Add(alias);
                result.References.Add(new SymbolReference(alias, span, true, true));
                root.Declare(alias, 0);
            }

            MarkupAttribute namesAttr = e.GetAttribute("names");
            if (namesAttr?.Value == null)
            {
                // <import path="lib.ks.xml" /> with neither as nor names brings in every exported function.
                if (string.IsNullOrWhiteSpace(asAttr?.ValueText) && exported != null)
                {
                    MarkupAttribute srcAttr = e.GetAttribute("path");
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

        /// <summary>
        /// The types and enums in the project's native type files (the natives folder, or kscript.json's "natives"), which
        /// every script in the project can use without an import. A type this file declares itself wins.
        /// </summary>
        private void HoistProjectNatives(Scope root)
        {
            MarkupElement owner = result.Markup.RootElements.FirstOrDefault();
            if (owner == null)
            {
                return;
            }
            string self = result.Path != null ? System.IO.Path.GetFullPath(result.Path) : null;
            foreach (string full in ProjectNativeFiles())
            {
                if (string.Equals(full, self, StringComparison.Ordinal))
                {
                    continue;
                }
                string text = files.ReadText(full);
                if (text == null)
                {
                    continue;
                }
                SourceText other = new SourceText(text);
                MarkupElement fileRoot = MarkupParser.Parse(other, MarkupParser.KindFromPath(full), full).RootElements.FirstOrDefault();
                foreach (MarkupElement e in fileRoot?.ChildElements.Where(x => x.Name == "type" || x.Name == "enum") ?? Enumerable.Empty<MarkupElement>())
                {
                    MarkupAttribute nameAttr = e.GetAttribute("name");
                    string name = nameAttr?.ValueText?.Trim().TrimStart('$');
                    if (string.IsNullOrEmpty(name) || TypeElementIn(result.Markup, name) != null || result.Markup.DescendantElements().Any(x => IsKScriptElementName(x, "enum") && x.GetAttributeValue("name")?.Trim() == name)
                        || result.Symbols.Any(s => s.SourcePath == full && s.Name == name))
                    {
                        continue;
                    }
                    TextSpan span = nameAttr.Value.SourceSpan;
                    string where = ProjectRelative(full);
                    if (e.Name == "enum")
                    {
                        importedEnums[(full, name)] = EnumValuesOf(e);
                    }
                    importedTypes.Add((full, name));
                    Symbol symbol = ImportedConstant(name, owner.NameSpan, owner, full, where,
                        (new ExternalLocation(full, other.GetPosition(span.Start), other.GetPosition(span.End)), e.Documentation ?? $"A native type of this project, from {where}"));
                    root.Declare(symbol, 0);
                }
            }
        }

        /// <summary>
        /// The models this file's models name without importing them, found in the project's models folder as the
        /// script does when it runs: <c>posts: Post[]</c> in models/user.kscript finds models/post.kscript.
        /// </summary>
        private void HoistProjectModels(Scope root)
        {
            MarkupElement owner = result.Markup.RootElements.FirstOrDefault();
            List<MarkupElement> models = result.Markup.DescendantElements().Where(IsModelElement).ToList();
            if (owner == null || models.Count == 0)
            {
                return;
            }
            List<string> modelFiles = null;
            string self = result.Path != null ? System.IO.Path.GetFullPath(result.Path) : null;
            var named = models.SelectMany(ModelFieldTypes).Select(f => TypeInference.WithoutTypeArguments(f.Type).TrimEnd('?').TrimEnd().Replace("[]", string.Empty).Trim())
                .Where(n => n.Length > 0 && Lexer.IsIdentStart(n[0]) && char.IsUpper(n[0]) && TypeElementIn(result.Markup, n) == null && !result.Symbols.Any(s => s.Name == n))
                .Distinct(StringComparer.Ordinal);
            foreach (string name in named)
            {
                modelFiles ??= ProjectFiles((config, d, f, l, ds) => config.ModelFiles(d, f, l, ds));
                string full = KScript.Projects.ProjectConfig.ModelFileFor(modelFiles, name);
                string text = full != null && !string.Equals(full, self, StringComparison.Ordinal) ? files.ReadText(full) : null;
                if (text == null)
                {
                    continue;
                }
                SourceText other = new SourceText(text);
                MarkupElement model = MarkupParser.Parse(other, MarkupParser.KindFromPath(full), full).DescendantElements()
                    .FirstOrDefault(e => e.Name == "model" && e.GetAttributeValue("name")?.Trim() == name);
                if (model == null)
                {
                    continue;
                }
                TextSpan span = model.GetAttribute("name").Value.SourceSpan;
                string where = ProjectRelative(full);
                Symbol symbol = ImportedConstant(name, owner.NameSpan, owner, full, where,
                    (new ExternalLocation(full, other.GetPosition(span.Start), other.GetPosition(span.End)), model.Documentation ?? $"A model of this project, from {where}"));
                symbol.IsTypeDeclaration = true;
                root.Declare(symbol, 0);
            }
        }

        private string ProjectRelative(string full)
        {
            for (string dir = directory; dir != null; dir = System.IO.Path.GetDirectoryName(dir))
            {
                if (files.FileExists(System.IO.Path.Combine(dir, KScript.Projects.ProjectConfig.FileName)))
                {
                    return System.IO.Path.GetRelativePath(dir, full).Replace('\\', '/');
                }
            }
            return System.IO.Path.GetFileName(full);
        }

        /// <summary>The values of the enums other files declare, by file and name, so an imported enum is a type here too.</summary>
        private readonly Dictionary<(string Path, string Name), List<string>> importedEnums = new Dictionary<(string Path, string Name), List<string>>();

        private static List<string> EnumValuesOf(MarkupElement e) =>
            (e.GetAttributeValue("values") ?? string.Empty).Split(',').Select(v => v.Trim()).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        private Symbol ImportedConstant(string name, TextSpan at, MarkupElement import, string full, string src, (ExternalLocation Location, string Description) constant)
        {
            Symbol symbol = MemberConstant(name, at, import, full, src, constant);
            result.Symbols.Add(symbol);
            return symbol;
        }

        /// <summary>A constant, enum or type another script declares, with where it is declared (not yet one of this file's symbols).</summary>
        private Symbol MemberConstant(string name, TextSpan at, MarkupElement import, string full, string src, (ExternalLocation Location, string Description) constant)
        {
            Symbol symbol = new Symbol
            {
                Name = name,
                Kind = SymbolKind.Constant,
                DeclarationSpan = at,
                DeclaringElement = import,
                SourcePath = full,
                Description = constant.Description ?? $"Imported from {src}",
                IsTypeDeclaration = importedTypes.Contains((full, name)),
                Shape = TypeShape.Unknown,
            };
            if (full != null && importedEnums.TryGetValue((full, name), out List<string> values))
            {
                symbol.EnumValues = values;
                symbol.Shape = new TypeShape(KType.Map);
                foreach (string v in values)
                {
                    symbol.Shape.Fields[v] = new FieldInfo(v, new TypeShape(KType.String), null, $"A value of {name}.");
                }
            }
            result.ExternalDeclarations[symbol] = constant.Location;
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
            CodeArguments.Normalise(doc, catalog.GetElement);
            Dictionary<string, (ExternalLocation, string)> constants = new Dictionary<string, (ExternalLocation, string)>(StringComparer.Ordinal);
            MarkupElement root = doc.RootElements.FirstOrDefault();
            if (root == null)
            {
                return constants;
            }
            foreach (MarkupElement let in root.ChildElements.Where(x => (x.Name == "let" && IsYes(x.GetAttributeValue("const"))) || x.Name == "const" || x.Name == "enum" || x.Name == "type" || x.Name == "model" || x.Name == "error" || x.Name == "job"))
            {
                MarkupAttribute nameAttr = let.GetAttribute("name");
                if (string.IsNullOrWhiteSpace(nameAttr?.ValueText))
                {
                    continue;
                }
                if (let.Name == "error")
                {
                    importedErrors.TryAdd(nameAttr.ValueText.Trim(), let.GetAttributeValue("extends")?.Trim() ?? KScript.Runtime.ErrorTypes.Root);
                }
                if (let.Name != "let" && let.Name != "const")
                {
                    importedTypes.Add((path, nameAttr.ValueText.Trim().TrimStart('$')));
                }
                if (let.Name == "enum")
                {
                    importedEnums[(path, nameAttr.ValueText.Trim())] = EnumValuesOf(let);
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
            CodeArguments.Normalise(doc, catalog.GetElement);
            Dictionary<string, (FunctionParameter[], ExternalLocation, string)> exports = new Dictionary<string, (FunctionParameter[], ExternalLocation, string)>(StringComparer.Ordinal);
            foreach (MarkupElement fn in doc.DescendantElements().Where(x => x.Name == "fn" && x.Parent?.Name is not ("extend" or "type" or "model" or "job" or "policy")))
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
                    $"A script has one root element. Put everything inside <kscript>.");
            }
            if (roots.Count > 0 && roots[0].Name != "kscript" && catalog.GetElement(roots[0].Name) != null)
            {
                Report(roots[0].NameSpan, DiagnosticSeverity.Warning, DiagnosticCodes.MisplacedElement, $"Scripts start with <kscript>.");
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
                                $"Text here is ignored. To print it, put it in <echo>.");
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

            if (info.Name == "normalize" && info.Package == "KScript.Core" && e.Parent != null
                && result.DeclaredTypes.TryGetValue(e.Parent, out TypeShape owner) && owner.RefinementBase != null)
            {
                scope = new Scope(scope, e.Span) { Normalizing = owner };
            }

            CheckAttributes(e, info);
            if (info.Package == "KScript.Http" && info.GetAttribute("where") != null
                && e.GetAttribute("where") is { Value: not null, FormError: null } where)
            {
                CheckRouteWhere(e, where);
            }
            if (Kind != DocumentKind.Script && info.Name is "layout" or "include")
                CheckTemplateInputs(e, ResolvePath(e.GetAttributeValue("path")));
            if (Kind != DocumentKind.Script && info.Name == "prop" && e.Parent != null)
                Report(e.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.MisplacedElement, "Declare props at the top level of the layout or component.");
            if (Kind == DocumentKind.Script && info.Name == ComponentScript.SlotElement && !e.Ancestors().Any(ComponentScript.IsRender))
                Report(e.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.MisplacedElement, Wording.Prose(ComponentScript.SlotOutsideRender));
            ReportDeprecatedElement(e, info);
            CheckPlacement(e, info, previous);
            if (info.Name == "guard" && !GuardLeaves(e))
                Report(e.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.StatementSyntax,
                    "The guard's failure block must leave with return, throw, break, continue or exit.");
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

            if (info.Planned && Kind == DocumentKind.Script)
            {
                // Described in the catalog, but no package runs it: the runtime would call it unknown.
                Report(e.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.PlannedFeature,
                    $"<{e.Name}> is planned but not built yet, so it can't run.");
            }

            // Elements that build their own scopes (fn, catch) must not also get an empty content scope,
            // or Scope.Innermost would find the empty one first.
            bool ownScopes = info.Name == "fn" || info.Name == "catch";
            Scope inner = e.SelfClosing || !e.StartTagClosed || ownScopes ? scope : new Scope(scope,
                info.Name is "type" or "model" && e.ChildElements.Any(c => c.Name == "catch") ? e.Span : e.ContentSpan);
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
                case "model" when info.Package == "KScript.Data":
                    AnalyzeType(e, info, scope, inner, markupMode);
                    return;

                case "error" when info.Package == "KScript.Core":
                    AnalyzeError(e, info, scope, inner, markupMode);
                    return;

                case "extend" when info.Package == "KScript.Core":
                    AnalyzeExtend(e, scope, inner, markupMode);
                    return;

                case "job" when info.Package == "KScript.Queue":
                    AnalyzeJob(e, info, scope, inner, markupMode);
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

                case "test" or "before_each" or "after_each" when info.Package == "KScript.Test" && testBodies != null && inner != scope:
                    AnalyzeAttributes(e, info, scope);
                    testBodies.Add((e, inner, markupMode));
                    return;

                case "expect_error" when info.Package == "KScript.Test":
                    // <expect_error as="err"> declares the caught error after the element, shaped like <catch as>.
                    AnalyzeAttributes(e, info, scope, skip: "as");
                    WalkChildren(e.Children, inner, markupMode);
                    DeclareFromAttribute(e, "as", scope, e.Span.End, SymbolKind.Variable, CaughtErrorShape(e, scope));
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
                    // In a .kscript file the catch follows the try's { } block, outside the try's own scope.
                    Scope outer = scope;
                    while (outer.Parent != null && !outer.Span.Contains(e.Span.Start))
                    {
                        outer = outer.Parent;
                    }
                    Scope catchScope = new Scope(outer, e.Span);
                    if (e.Parent != null && result.DeclaredTypes.TryGetValue(e.Parent, out TypeShape ownerType) && ownerType.RefinementBase != null)
                        DeclareImplicit(catchScope, "this", ownerType, $"The {ownerType.Name} whose method was called.", e);
                    if (e.GetAttribute("as") == null)
                    {
                        // Without as, the error is $err.
                        Symbol err = new Symbol { Name = "err", Kind = SymbolKind.BuiltIn, Shape = CaughtErrorShape(e, outer), Description = "The error this catch is handling.", DeclaringElement = e };
                        result.Symbols.Add(err);
                        catchScope.Declare(err, e.Span.Start);
                    }
                    DeclareFromAttribute(e, "as", catchScope, e.Span.Start, SymbolKind.Variable, CaughtErrorShape(e, outer));
                    AnalyzeAttributes(e, info, catchScope, skip: "as");
                    WalkChildren(e.Children, new Scope(catchScope, e.ContentSpan), markupMode);
                    return;
                }

                case "Get" or "Post" or "Put" or "Patch" or "Delete" or "Options" or "Any" or "Route" when info.Package == "KScript.Http" && inner != scope:
                    // An inline handler gets what a controller can ask for: $request, $json, $query... and the path's {parameters}.
                    AnalyzeAttributes(e, info, scope);
                    DeclareRequestVariables(e, inner);
                    WalkChildren(e.Children, inner, markupMode);
                    return;

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

            // Elements that store a result: <input into>, <llm into>, <collection name>, <picker into>.
            foreach (AttributeInfo a in info.Attributes.Where(a => a.Kind == AttributeKind.DeclareVariable && info.Name != "import"))
            {
                MarkupAttribute attr = e.GetAttribute(a.Name);
                if (attr?.Value != null)
                {
                    TypeShape shape = info.Name == "collection" ? CollectionShape(e) : info.Name == "llm" && e.GetAttribute("schema") != null ? new TypeShape(KType.Map) : new TypeShape(info.DeclaresType ?? KType.String);
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
                            AnalyzeTemplate(text.Content, scope, e, null, allowBare: Kind == DocumentKind.Script, info.LiteralText);
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
                if (ComponentDrawnBy(e) is MarkupElement drawn)
                {
                    WalkComponentElement(e, drawn, scope, inner);
                    return;
                }
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
            string component = Kind != DocumentKind.Script && global::KScript.Pages.PageElementNames.CanBeComponent(e.Name)
                ? CheckComponent(e, required: e.Name.Contains('-'))
                : null;
            if (component != null) CheckTemplateInputs(e, component);

            foreach (MarkupAttribute attr in e.Attributes)
            {
                if (component != null && global::KScript.Pages.TemplateContract.IsLoadCallback(attr.Name))
                {
                    if (ScriptFiles.IsScript(component))
                        Report(attr.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue, "onLoad is available on components with companion load functions.");
                    else
                        CheckLoadCallback(e, attr, scope);
                    continue;
                }
                if (component == null && Kind != DocumentKind.Script &&
                    global::KScript.Pages.TemplateContract.NeedsLiveOwner(attr.Name))
                {
                    RequireLive(e, attr);
                    if (attr.Name.Equals("live-action", StringComparison.OrdinalIgnoreCase) && e.GetAttribute("on-action") == null)
                        Report(attr.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.MissingAttribute,
                            "A live-action name needs an on-action handler.");
                    if (attr.Name.Equals("bind-on", StringComparison.OrdinalIgnoreCase) && (e.GetAttribute("bind") == null ||
                        !global::KScript.Pages.LiveAttributes.Get("bind-on").Options.Contains(attr.ValueText)))
                        Report(attr.Span, DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue,
                            "Use bind-on with bind and choose input, change, blur or action.");
                }
                if (attr.Name == "autogrow" && Kind != DocumentKind.Script && component == null && !e.Name.Equals("textarea", StringComparison.OrdinalIgnoreCase))
                {
                    Report(attr.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttribute, "Use @autogrow() on a textarea.");
                }
                if (attr.Name == "drop-files" && Kind != DocumentKind.Script && component == null && attr.Value != null &&
                    attr.ValueText.Trim() is string uploadName && uploadName.Length > 0 && !uploadName.Contains('{') && !HasUploadInput(e, uploadName))
                {
                    Report(attr.Span, DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue,
                        $"@drop-files('{uploadName}') needs a file input named {uploadName} with live-upload inside this element.");
                }
                if (attr.Value == null)
                {
                    continue;
                }
                if (component == null && attr.Name.StartsWith("on-", StringComparison.OrdinalIgnoreCase) && Kind != DocumentKind.Script)
                {
                    // <button on-click="count += 1">: statements run on the server when the event fires.
                    // $event (value, checked, key) describes the browser event the handler runs for.
                    Scope handlerScope = new Scope(scope, e.Span);
                    handlerScope.Declare(BuiltIn("event"), e.Span.Start);
                    if (HandlerShorthands.IsShorthand(attr.ValueText))
                    {
                        AnalyzeShorthands(attr, e, handlerScope);
                        continue;
                    }
                    AnalyzeStatements(attr.Value, e, attr, handlerScope, declare: false, requireEffect: true);
                }
                else if (attr.Name == ClassDirective.Attribute && Kind != DocumentKind.Script)
                {
                    AnalyzeClassDirective(attr, e, scope);
                }
                else if (component == null && attr.Name == "focus" && Kind != DocumentKind.Script)
                {
                    AnalyzeFocusDirective(attr, e, scope);
                }
                else if (component == null && attr.Name == PendingDirective.Attribute && Kind != DocumentKind.Script)
                {
                    AnalyzePendingDirective(attr, e, scope);
                }
                else if (component == null && attr.Name.Equals("bind", StringComparison.OrdinalIgnoreCase) && Kind != DocumentKind.Script)
                {
                    CheckBinding(e, attr, scope);
                }
                else
                {
                    // Components get the value of a lone { expr } (person="{$p}"); anything else is text.
                    AnalyzeTemplate(attr.Value, scope, e, attr, allowBare: false);
                }
            }

            WalkChildren(e.Children, inner, markupMode: true);
        }

        /// <summary>True when the element holds a live-upload file input of this name.</summary>
        private static bool HasUploadInput(MarkupElement e, string name) => e.Children.OfType<MarkupElement>().Any(child =>
            child.Name.Equals("input", StringComparison.OrdinalIgnoreCase) && child.GetAttribute("live-upload") != null &&
            child.GetAttribute("name")?.ValueText == name || HasUploadInput(child, name));

        private void ReportUnknownElement(MarkupElement e)
        {
            if (IsCode && ReportHabit(e))
            {
                return;
            }

            // KScript 1 was XML only, so a .kscript file never holds one of its elements: there the word is just unknown.
            RetiredInfo retired = catalog.GetRetiredElement(e.Name);
            if (IsCode && retired?.Renamed != true)
            {
                retired = null;
            }
            if (retired != null)
            {
                List<QuickFix> fixes = new List<QuickFix>();
                if (retired.Rename != null)
                {
                    fixes.Add(RenameElementFix(e, retired.Rename, retired.Renamed));
                }
                Report(e.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.RetiredElement, Wording.Prose(retired.Message(IsCode ? e.Name : $"<{e.Name}>")), fixes);
                return;
            }

            IEnumerable<string> candidates = catalog.Elements.Where(x => x.Context.HasFlag(ElementContext.Script)).Select(x => x.Name);
            string suggestion = Suggestions.Closest(e.Name, candidates);
            Report(e.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.UnknownElement,
                IsCode
                    ? $"Unknown statement '{e.Name}'." + (suggestion != null ? $" Did you mean {suggestion}?" : string.Empty)
                    : $"Unknown element <{e.Name}>." + (suggestion != null ? $" Did you mean <{suggestion}>?" : string.Empty),
                suggestion != null ? new[] { RenameElementFix(e, suggestion) } : null);
        }

        private QuickFix RenameElementFix(MarkupElement e, string name, bool renamed = false)
        {
            List<TextChange> changes = new List<TextChange> { new TextChange(e.NameSpan, name) };
            if (e.EndNameSpan.HasValue)
            {
                changes.Add(new TextChange(e.EndNameSpan.Value, name));
            }
            return new QuickFix((renamed ? Fixer.RenameTitle : "Change to ") + (IsCode ? name : $"<{name}>"), changes.ToArray());
        }

        private string CheckComponent(MarkupElement e, bool required)
        {
            string componentDir = ComponentFiles.Folder(result.Path, files);
            if (componentDir == null)
            {
                return null;
            }

            string file = TemplateContracts.ComponentFile(files, componentDir, e.Name);
            if (file != null)
            {
                result.Links.Add(new DocumentLink(e.NameSpan, file, "component"));
                return file;
            }
            if (!required)
            {
                return null;
            }

            IEnumerable<string> known = ComponentFiles.List(componentDir, files).Select(ComponentScript.Tag);
            string suggestion = Suggestions.Closest(e.Name, known);
            Report(e.NameSpan, DiagnosticSeverity.Warning, DiagnosticCodes.UnknownElement,
                Wording.Of($"No component named <{e.Name}> in {System.IO.Path.GetFileName(componentDir)}.") + (suggestion != null ? Wording.Of($" Did you mean <{suggestion}>?") : null),
                suggestion != null ? new[] { RenameElementFix(e, suggestion) } : null);
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

                RetiredInfo retired = catalog.GetRetiredAttribute(info, attr.Name);
                if (retired != null)
                {
                    Report(attr.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.RetiredAttribute,
                        retired.Renamed ? Wording.Prose(retired.Message($"'{attr.Name}'")) : Wording.Of($"The '{attr.Name}' attribute was removed in KScript 2. {Wording.Prose(retired.Replacement)}"),
                        retired.Rename != null && info.GetAttribute(retired.Rename) != null && e.GetAttribute(retired.Rename) == null
                            ? new[] { new QuickFix((retired.Renamed ? Fixer.RenameTitle : "Change to ") + retired.Rename, new TextChange(attr.NameSpan, retired.Rename)) }
                            : null);
                    continue;
                }

                if (info.AllowsExtraAttributes || attr.Name.StartsWith("xmlns", StringComparison.Ordinal))
                {
                    continue;
                }

                string suggestion = Suggestions.Closest(attr.Name, info.Attributes.Select(x => x.Name));
                Report(attr.NameSpan, DiagnosticSeverity.Warning, DiagnosticCodes.UnknownAttribute,
                    Wording.Of($"<{e.Name}> has no '{attr.Name}' attribute.") + (suggestion != null ? $" Did you mean '{suggestion}'?" : AttributeList(info)),
                    suggestion != null ? new[] { new QuickFix($"Change to {suggestion}", new TextChange(attr.NameSpan, suggestion)) } : null);
            }

            foreach (AttributeInfo a in info.Attributes.Where(a => a.Required))
            {
                if (e.GetAttribute(a.Name) == null && !RequiredSatisfiedOtherwise(e, info, a))
                {
                    Report(e.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.MissingAttribute, $"<{e.Name}> needs the '{a.Name}' attribute: {Wording.Prose(a.Description)}");
                }
            }
        }

        private static string AttributeList(ElementInfo info) =>
            info.Attributes.Count == 0 ? $" <{info.Name}> takes no attributes." : $" It takes: {string.Join(", ", info.Attributes.Select(a => a.Name))}.";

        /// <summary>A retired attribute that maps onto the missing one (condition= for test=) has already been reported.</summary>
        private bool RequiredSatisfiedOtherwise(MarkupElement e, ElementInfo info, AttributeInfo a) =>
            e.Attributes.Any(x => catalog.GetRetiredAttribute(info, x.Name)?.Rename == a.Name);

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
                Report(TrimmedSpan(attr.Value), DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue, $"'{a.Name}' takes true or false, not '{value}'.");
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
                bool ok = (info.Name != "catch" && info.MustBeInside.Contains("fn")) || info.InsideAnyAncestor
                    ? e.Ancestors().Any(a => info.MustBeInside.Contains(ElementInfo.BareName(a.Name))
                        || (info.MustBeInside.Contains("fn") && catalog.GetElement(ElementInfo.BareName(a.Name))?.IsFunctionBody == true))
                    : e.Parent != null && info.MustBeInside.Contains(ElementInfo.BareName(e.Parent.Name), StringComparer.OrdinalIgnoreCase);
                if (!ok && info.Name == "return")
                    ok = e.Ancestors().Any(a => a.Name == "catch" && a.Parent?.Name is "type" or "model");
                if (!ok && info.Name == "state" && (inComponentHtml || IsComponentPage))
                    ok = e.Parent == null;
                if (!ok)
                {
                    Report(e.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.MisplacedElement,
                        IsCode
                            ? $"'{e.Name}' only works inside {KScript.Runtime.Values.OrList(info.MustBeInside.ToList())}."
                            : $"<{e.Name}> must be inside {KScript.Runtime.Values.OrList(info.MustBeInside.Select(n => IsCode ? n : "<" + n + ">").ToList())}.");
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
            CheckArgumentForm(e, attr, a);
            // A declaration's attached doc comment is prose.
            if (attr.Name == "description" && attr.NameSpan.IsEmpty) return;
            if (attr.Name == "value" && e.Name is "fn" or "return")
            {
                var fn = e.Name == "fn" ? e : e.Ancestors().FirstOrDefault(parent => parent.Name == "fn");
                if (ComponentScript.HtmlBlock(fn, attr.Value) is { Error: null } block)
                {
                    componentHtmlOwner = fn.Parent;
                    try { AnalyzeComponentHtml(block, attr.Value, scope); }
                    finally { componentHtmlOwner = null; }
                    return;
                }
                if (ComponentScript.StyleBlock(fn, attr.Value) is { Error: null }) return;
                if (ComponentScript.SourceBlock(fn, attr.Value) is { Error: null } part)
                {
                    AnalyzeComponentSource(part, attr, scope);
                    return;
                }
            }
            switch (a.Kind)
            {
                case AttributeKind.Expression:
                    var expression = AnalyzeExpressionRegion(attr.Value, RegionKind.Expression, scope, e, attr);
                    if (a.RequiresDeclaredRecord && expression.Parse?.Root is ExprNode value)
                    {
                        TypeShape shape = Infer(value, expression);
                        if (value is MapNode || (shape.Type != KType.Any && (shape.Type != KType.Map || shape.Kind != null)))
                            Report(TrimmedSpan(attr.Value), DiagnosticSeverity.Error, DiagnosticCodes.TypeMismatch,
                                $"'{attr.Name}' requires a value of a declared record type.");
                    }
                    break;
                case AttributeKind.ExpressionList:
                    AnalyzeExpressionRegion(attr.Value, RegionKind.ExpressionList, scope, e, attr);
                    break;
                case AttributeKind.Template:
                    AnalyzeTemplate(attr.Value, scope, e, attr, allowBare: true);
                    break;
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
                            ReferenceFunction(bare, new TextSpan(span.Start, bare.Length), scope, a.FunctionSignature);
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
                case AttributeKind.ErrorType:
                    CheckErrorTypeAttribute(e, attr, scope);
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
                        isLet ? new Wording("<let> needs a name and a value: <let name=\"x\" value=\"3\" /> or <let>x = 3</let>.", "A variable needs a name and a value: x = 3.")
                              : new Wording("<set> needs a name and a value: <set name=\"x\" value=\"3\" /> or <set>x = 3</set>.", "An assignment needs a name and a value: x = 3."));
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
                Report(e.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.MissingAttribute, $"<let> needs a value: a value attribute or text.");
            }
            else
            {
                Report(e.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.MissingAttribute, $"<set> needs a value: a value attribute or text.");
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
                    declared.Description = "fn(" + string.Join(", ", lambda.Parameters.Select(p => p.Name)) + ")";
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
            if (!inComponentHtml && !IsComponentPage && !e.Ancestors().Any(a => a.Name == "live"))
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
                    Report(e.NameSpan, DiagnosticSeverity.Warning, DiagnosticCodes.StatementSyntax, $"<do> is empty.");
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
                    if (assign.Name != null && assign.Value is LiteralNode { Template: not null })
                    {
                        filledTexts.Add(assign.Name);
                    }
                    AnalyzeExpr(assign.Value, region, scope, ExprContext.Value);
                    TypeShape shape = Infer(assign.Value, region);
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
                        if (assign.Value is LiteralNode { Kind: LiteralKind.String, Template: null } text)
                        {
                            literalTexts[symbol] = text.Value;
                        }
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
                    if (attr != null && requireEffect && region.Parse.Nodes.Count == 1 && node is VariableNode toggled)
                    {
                        // A handler that is one value: on_click: $open flips a bool, on_click: $save calls a function or method.
                        TextSpan name = VariableNameSpan(toggled, region);
                        Symbol variable = scope.Lookup(toggled.Name, name.Start);
                        Symbol called = scope.Lookup(toggled.Name, int.MaxValue, includeMethods: true);
                        if (called?.Kind != SymbolKind.Function && scope.Lookup("this", name.Start)?.Shape is TypeShape self)
                            self.Methods.TryGetValue(toggled.Name, out called);
                        if ((variable == null || variable.Kind == SymbolKind.Function) && called?.Kind == SymbolKind.Function)
                        {
                            result.References.Add(new SymbolReference(called, name, false, false));
                            ReportIfDeprecated(called, name);
                            continue;
                        }
                        AnalyzeExpr(node, region, scope, ExprContext.Value);
                        string type = Infer(node, region).Type;
                        if (handlerShorthand == HandlerShorthands.Call)
                        {
                            if (variable != null && type is not (KType.Fn or KType.Any))
                                Report(name, DiagnosticSeverity.Error, DiagnosticCodes.TypeMismatch,
                                    $"call: needs a function or one of the component's methods, and ${toggled.Name} is {type}.");
                            continue;
                        }
                        if (type is KType.Bool)
                        {
                            ReferenceVariable(toggled.Name, name, scope, isWrite: true, mustBeMutable: true, offset: name.Start);
                            if (!attr.IsArgument)
                            {
                                // A page handler's value is an action: flipping a bool is toggle: $open.
                                TextSpan whole = region.Source.ToSource(node.Span);
                                Report(whole, DiagnosticSeverity.Error, DiagnosticCodes.StatementSyntax, HandlerShorthands.BareBoolMessage("$" + toggled.Name),
                                    new[] { new QuickFix($"Change to toggle: ${toggled.Name}", new TextChange(new TextSpan(whole.Start, 0), HandlerShorthands.Toggle + ": ")) });
                            }
                        }
                        else if (variable != null && type is not (KType.Fn or KType.Any))
                            Report(name, DiagnosticSeverity.Error, DiagnosticCodes.StatementSyntax,
                                $"A handler that is one value calls a function, and ${toggled.Name} is {type}. Write statements instead, such as {toggled.Name} = ….");
                        continue;
                    }
                    if (requireEffect)
                    {
                        AnalyzeExpr(node, region, scope, ExprContext.Value);
                    }
                    ExprNode effect = node;
                    while (effect is UnaryNode { Operator: "not" } negation) effect = negation.Operand;
                    bool isCall = effect is CommandCallNode or MethodCallNode or CallNode or PipeNode;
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
            // $repo.stras = 3: a record takes only its type's fields, when it is set as when it is read.
            if (t is MemberNode set)
            {
                TypeShape record = set.Target is IdentifierNode named
                    ? scope.Lookup(named.Name, region.Source.ToSource(named.Span).Start)?.Shape
                    : Infer(set.Target, region);
                if (record != null)
                {
                    CheckRecordField(record, set, region);
                    CheckPrivate(record, set.Name, method: false, region.Source.ToSource(set.NameSpan), region);
                }
            }
            while (t is MemberNode || t is IndexNode)
            {
                if (t is IndexNode ix)
                {
                    AnalyzeExpr(ix.Index, region, scope, ExprContext.Value);
                    if (ix.Index is LiteralNode { Kind: LiteralKind.String } key)
                    {
                        writtenFields.Add(key.Value);
                    }
                    t = ix.Target;
                }
                else
                {
                    writtenFields.Add(((MemberNode)t).Name);
                    t = ((MemberNode)t).Target;
                }
            }
        }

        private static TextSpan NameSpanOf(AssignmentNode assign, ExpressionRegion region)
        {
            return region.Source.ToSource(assign.Target is VariableNode variable ? variable.NameSpan : assign.NameSpan);
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
                    itemShape = collection.Type == KType.List ? collection.Element ?? TypeShape.Unknown : TypeInference.NativeItems(collection) ?? TypeShape.Unknown;
                    // A native type that can't be walked stops the loop when it runs, so say so first.
                    if (KScript.Metadata.NativeTypes.Get(collection.Type) is { Iterates: null } native)
                    {
                        Report(region.Source.ToSource(region.Parse.Root.Span), DiagnosticSeverity.Warning, DiagnosticCodes.TypeMismatch,
                            $"<for> can't loop over {native.WithArticle}: {native.Name} values can't be walked.");
                    }
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

            DeclareFromAttribute(e, "at", body, e.ContentSpan.Start, SymbolKind.LoopVariable, new TypeShape(KType.Number));
            if (body == outer) return;

            var loop = new TypeShape(KType.Map);
            loop.Fields["index"] = new FieldInfo("index", new TypeShape(KType.Number), null, "The zero-based index of this iteration.");
            loop.Fields["first"] = new FieldInfo("first", new TypeShape(KType.Bool), null, "Whether this is the first iteration.");
            // Script loops know the sequence length. PageRenderer walks the sequence as it renders.
            if (Kind == DocumentKind.Script)
            {
                loop.Fields["last"] = new FieldInfo("last", new TypeShape(KType.Bool), null, "Whether this is the last iteration.");
                loop.Fields["count"] = new FieldInfo("count", new TypeShape(KType.Number), null, "The number of items in the loop.");
            }
            var position = new Symbol { Name = "loop", Kind = SymbolKind.LoopVariable, Shape = loop, Description = "The current loop iteration.", DeclaringElement = e };
            result.Symbols.Add(position);
            body.Declare(position, e.ContentSpan.Start);
        }

        private void AnalyzeComponentHtml(StringBlock block, DecodedText value, Scope outer)
        {
            TextSpan span = TextSpan.FromBounds(value.ToSource(block.ContentStart), value.ToSource(block.ContentEnd));
            var fragment = MarkupParser.ParsePageFragment(source, value, TextSpan.FromBounds(block.ContentStart, block.ContentEnd));
            result.ComponentMarkup.Add((span, fragment));
            result.Diagnostics.AddRange(fragment.Diagnostics);
            var scope = new Scope(outer, span);
            if (outer.Lookup("request", span.Start) == null)
                scope.Declare(BuiltIn("request", RequestContextFields()), span.Start);
            inComponentHtml = true;
            try { WalkChildren(fragment.Children, scope, markupMode: true); }
            finally { inComponentHtml = false; }
        }

        /// <summary>Checks the values in the Markdown or LaTeX a component's render returns for a document.</summary>
        private void AnalyzeComponentSource(StringBlock block, MarkupAttribute attribute, Scope scope)
        {
            DecodedText text = ComponentScript.BlockText(block, attribute.Value);
            if (StringBlock.ValueType(block.Tag) != KType.Latex)
            {
                AnalyzeTemplate(text, scope, attribute.Owner, attribute, allowBare: false, TextLiterals.Maths);
                return;
            }
            foreach ((_, _, DecodedText value) in ComponentScript.LatexValues(text))
                AnalyzeExpressionRegion(value, RegionKind.Expression, scope, attribute.Owner, attribute);
        }

        private void AnalyzeFn(MarkupElement e, Scope outer, bool markupMode)
        {
            ElementInfo info = catalog.GetElement("fn");
            Scope body = new Scope(outer, e.Span);

            if (e.Parent != null && result.DeclaredTypes.TryGetValue(e.Parent, out TypeShape refined) && refined.RefinementBase != null)
                DeclareImplicit(body, "this", refined, $"The {refined.Name} this method was called on.", e);

            bool routeHandler = IsRouteHandler(e);
            if (routeHandler && outer.Lookup("request", e.Span.Start)?.Kind == SymbolKind.BuiltIn)
                body.Declare(BuiltIn("request", KScript.Http.RouteRequestFields.Matched), e.Span.Start);

            MarkupAttribute paramsAttr = e.GetAttribute("params");
            if (paramsAttr?.Value != null)
            {
                List<Symbol> declared = new List<Symbol>();
                List<FunctionParameter> parameters = ParseParameters(paramsAttr.Value, report: true);
                foreach (FunctionParameter p in parameters)
                {
                    TypeShape inferred = InferredParameterShape(p, e.Span.Start);
                    TypeShape shape = inferred ?? (routeHandler && p.Name == "request" && p.Type is null or KType.Map && !p.HasDefault
                        ? MatchedRequestParameter(p.Type != null)
                        : ParameterShape(p, e.Span.Start));
                    Symbol symbol = NewVariable(p.Name, p.Span, e, SymbolKind.Parameter, shape);
                    symbol.TypeFromCalls = inferred != null;
                    body.Declare(symbol, e.Span.Start);
                    declared.Add(symbol);
                }
                functionParameters[e] = declared;
                CheckParameterTypes(e, parameters, declared);
            }

            foreach (MarkupAttribute attr in e.Attributes.Where(a => a.Name != "params" && a.Name != "name"))
            {
                if (attr.Value != null && info.GetAttribute(attr.Name) is AttributeInfo ai)
                {
                    AnalyzeAttribute(e, attr, ai, body);
                }
            }

            CheckIdentifier(e.GetAttribute("name")?.Value);
            if (e.GetAttribute("optional") is { Value: not null } optional && e.GetAttribute("signature") == null)
                Report(optional.Value.SourceSpan, DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue, "Only a method signature can be optional.");
            if (e.GetAttribute("returns") is { Value: not null } returns && returns.ValueText.Trim() is not (ComponentScript.Contract or ComponentScript.StyleContract))
                CheckTypeText(returns.ValueText.Trim(), TrimmedSpan(returns.Value), returns.Value.SourceSpan.Start, scope: body);

            bool hasBody = e.Children.Any(c => c is MarkupElement { Name: not "catch" } || (c is MarkupText t && !t.IsWhitespace));
            if (e.GetAttribute("value") != null && hasBody)
            {
                Report(e.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttribute, $"<fn> has both a value and a body. Use one.");
            }

            WalkChildren(e.ChildElements.Where(c => c.Name == "catch"), body, markupMode);
            WalkChildren(e.Children.Where(c => c is not MarkupElement { Name: "catch" }), new Scope(body, e.ContentSpan), markupMode);
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
            TypeShape shape = BaseErrorShape();
            TypeShape problem = new TypeShape(KType.Map);
            foreach (string f in new[] { "path", "field", "message" })
            {
                problem.Fields[f] = new FieldInfo(f, new TypeShape(KType.String), null);
            }
            shape.Fields["problems"] = new FieldInfo("problems", new TypeShape(KType.List) { Element = problem }, null,
                "For a Shape error (data that doesn't fit a <type>): each problem's JSON path, field and message.");
            return shape;
        }

        private TypeShape CollectionShape(MarkupElement e)
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
