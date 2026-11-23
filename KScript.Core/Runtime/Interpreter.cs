using KScript.Metadata;
using KScript.Syntax;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Runtime
{
    /// <summary>
    /// Loads and runs KScript 2 scripts. Loading parses the script, resolves its imports and checks every element,
    /// attribute and expression, so mistakes are reported before anything runs. Running executes the elements.
    /// <code>
    /// var interpreter = new Interpreter(KScriptHost.Console());
    /// int exitCode = interpreter.Run(interpreter.Load("hello.ks.xml"));
    /// </code>
    /// </summary>
    public sealed partial class Interpreter
    {
        private readonly Dictionary<string, Module> modulesByPath = new Dictionary<string, Module>(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        private readonly HashSet<string> loading = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<Thread> spawned = new List<Thread>();
        private readonly ThreadLocal<int> callDepth = new ThreadLocal<int>();
        private readonly DateTime started = DateTime.UtcNow;
        private long steps;

        public Interpreter(KScriptHost host = null, PackageRegistry packages = null, InterpreterOptions options = null)
        {
            Host = host ?? KScriptHost.Console();
            Packages = packages ?? PackageRegistry.Default;
            Options = options ?? new InterpreterOptions();
        }

        public KScriptHost Host { get; }
        public PackageRegistry Packages { get; }
        public InterpreterOptions Options { get; }

        #region Loading

        /// <summary>Loads and checks a script file. Throws <see cref="KScriptException"/> (with a line and column) for any mistake.</summary>
        /// <summary>Every script loaded so far, imports included, such as what kscript compile embeds.</summary>
        public IEnumerable<Module> LoadedModules => modulesByPath.Values;

        public Module Load(string path)
        {
            string full = System.IO.Path.GetFullPath(path);
            if (!File.Exists(full)) throw new KScriptException($"There is no script at {path}.", "FileNotFound") { File = full };
            return LoadText(File.ReadAllText(full), full);
        }

        /// <summary>Loads and checks a script from text. <paramref name="path"/> names it in errors and anchors relative paths.</summary>
        public Module LoadText(string text, string path = null)
        {
            var source = new SourceText(text);
            MarkupDocument document = MarkupParser.Parse(source, DocumentKind.Script, path);
            var module = new Module(this, path, source, document);
            if (module.Path != null) loading.Add(module.Path);
            try
            {
                Diagnostic syntax = document.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
                if (syntax != null) throw Located(new KScriptException(syntax.Message, "Syntax"), module, syntax.Span.Start);

                List<MarkupElement> roots = document.RootElements.ToList();
                if (roots.Count == 0)
                    throw Located(new KScriptException($"The script is empty. A script starts with <kscript> and ends with </kscript>.", "Syntax"), module, 0);
                if (roots.Count > 1)
                    throw Located(new KScriptException($"A script has one root element, but this one has <{roots[0].Name}> and then <{roots[1].Name}>. Put everything inside <kscript>.", "Syntax"), module, roots[1].Span.Start);

                MarkupElement root = roots[0];
                module.Root = root;
                if (root.Name.StartsWith("KScript.", StringComparison.Ordinal))
                {
                    module.IsPackageRoot = true;
                    module.MainElement = root;
                }
                else if (root.ChildElements.Where(e => char.IsUpper(e.Name[0])).Take(2).ToList() is { Count: 1 } things)
                {
                    module.MainElement = things[0];
                }
                else if (root.Name != "kscript")
                {
                    throw Located(new KScriptException($"A script starts with <kscript>, not <{root.Name}>.", "Syntax"), module, root.Span.Start);
                }

                ResolveImports(module);
                CheckElement(module, root, isRoot: true);
                if (module.Path != null) modulesByPath[module.Path] = module;
                return module;
            }
            finally
            {
                if (module.Path != null) loading.Remove(module.Path);
            }
        }

        /// <summary>Gives a module a package (and the packages it needs). With an alias, its commands are reached as <c>@alias.name</c>.</summary>
        private void AddPackage(Module module, KScriptPackage package, string alias)
        {
            if (alias != null)
            {
                module.Aliases[alias] = new ImportNamespace(alias, package);
            }
            if (module.packages.Contains(package))
            {
                return;
            }
            module.packages.Add(package);
            foreach (string required in package.Requires)
            {
                KScriptPackage dependency = Packages.Get(required) ?? throw new KScriptException($"{package.Name} needs {required}, which isn't installed.", "Import");
                AddPackage(module, dependency, null);
            }
            foreach (var e in package.Elements) module.Elements[e.Key] = e.Value;
            if (alias == null || package == Packages.Core)
            {
                foreach (var c in package.Commands) module.Commands[c.Key] = c.Value;
            }
            foreach (MethodDefinition m in package.Methods) module.Methods[m.Receiver + ":" + m.Name] = m;
            foreach (ValueFieldDefinition f in package.Fields) module.Fields[f.Receiver + ":" + f.Name] = f;
            foreach (var v in package.Variables) module.Variables[v.Key] = v.Value;
            foreach (var f in package.EchoFormats) module.EchoFormats[f.Key] = f.Value;
        }

        private void ResolveImports(Module module)
        {
            void Add(KScriptPackage package, string alias) => AddPackage(module, package, alias);

            Add(Packages.Core, null);
            foreach (string name in Options.DefaultPackages)
            {
                Add(GetPackage(name, module, null), null);
            }

            // In an XML file, <KScript.Window> (as the root or anywhere) brings in the package that defines Window. A
            // .kscript file never has one: its packages are named only by import.
            foreach (MarkupElement e in module.Document.DescendantElements().Where(e => e.Name.StartsWith("KScript.", StringComparison.Ordinal)))
            {
                string bare = e.Name.Substring(8);
                KScriptPackage owner = Packages.LoadAll().FirstOrDefault(p => p.Elements.ContainsKey(bare));
                if (owner == null)
                {
                    throw Located(new KScriptException(Wording.Of($"No KScript package has an element called <{bare}>, so <{e.Name}> can't be used.") + SuggestElement(bare, Packages.LoadAll().SelectMany(p => p.Elements.Keys)), "Import"), module, e.NameSpan.Start);
                }
                Add(owner, null);
            }

            foreach (MarkupElement import in module.Root.ChildElements.Where(e => e.Name == "import"))
            {
                string packageName = import.GetAttributeValue("package");
                string alias = import.GetAttributeValue("as");
                if (alias != null && (alias.Length == 0 || !Lexer.IsIdentStart(alias[0]) || !alias.All(Lexer.IsIdentChar)))
                {
                    throw Located(new KScriptException($"as=\"{alias}\" isn't a name. Use letters, digits and _, such as as=\"math\".", "Import"), module, import.GetAttribute("as").Span.Start);
                }
                if (packageName != null)
                {
                    Add(GetPackage(packageName, module, import), alias);
                    continue;
                }
                string src = import.GetAttributeValue("path");
                if (src == null) continue; // reported by the attribute check
                Module imported = LoadImport(src, module, import);
                module.ScriptImports[import] = imported;
                if (alias != null) module.Aliases[alias] = new ImportNamespace(alias, imported);
            }

            // <import> only works at the top of a script, where it is resolved before anything runs.
            foreach (MarkupElement misplaced in module.Document.DescendantElements().Where(e => e.Name == "import" && e.Parent != module.Root))
            {
                string message = module.Document.Syntax == SourceSyntax.Code
                    ? "import goes at the top level of the file, not inside a block, before anything that uses it."
                    : "<import> must be a direct child of <kscript>, before anything that uses it.";
                throw Located(new KScriptException(message, "Import"), module, misplaced.Span.Start);
            }
        }

        /// <summary>
        /// Creates a module for a document that isn't a script, such as a web page. It has KScript.Core and the
        /// default packages; <see cref="ImportPackage"/> and <see cref="ImportScript"/> add more. Its expressions
        /// are evaluated with <see cref="EvaluateText"/>, <see cref="ExecuteStatements"/> and <see cref="Interpolate"/>.
        /// </summary>
        public Module CreateModule(string path, IEnumerable<string> packages = null)
        {
            var source = new SourceText(string.Empty);
            var module = new Module(this, path, source, MarkupParser.Parse(source, DocumentKind.Page)) { HasRun = true };
            ConfigValue.PrepareProject(Host, module.Directory);
            module.ProjectExtensions = ProjectExtensionsFor(module);
            module.Globals = new Scope(module.ProjectExtensions?.NativeScope);
            AddPackage(module, Packages.Core, null);
            foreach (string name in Options.DefaultPackages.Concat(packages ?? Enumerable.Empty<string>()))
            {
                AddPackage(module, GetPackage(name, module, null), null);
            }
            return module;
        }

        /// <summary>Adds a package to a module, as <c>&lt;import package="..." as="..." /&gt;</c> does.</summary>
        public void ImportPackage(Module module, string packageName, string alias = null)
        {
            CheckAlias(alias);
            AddPackage(module, GetPackage(packageName, module, null), alias);
        }

        /// <summary>
        /// Loads and runs a script (once) and gives a module its exports, as <c>&lt;import path="..." /&gt;</c> does:
        /// all of them, the comma separated <paramref name="names"/>, or under <paramref name="alias"/>.
        /// </summary>
        public void ImportScript(Module module, string path, Scope scope, string alias = null, string names = null)
        {
            CheckAlias(alias);
            string full = System.IO.Path.GetFullPath(path);
            if (!modulesByPath.TryGetValue(full, out Module imported))
            {
                if (!File.Exists(full)) throw new KScriptException($"There is no script at {path}.", "FileNotFound") { File = full };
                imported = Load(full);
            }
            RunModule(imported);
            Extensions.Import(module, imported);
            if (alias != null)
            {
                module.Aliases[alias] = new ImportNamespace(alias, imported);
                return;
            }
            DeclareExports(imported, names, scope);
        }

        private static void CheckAlias(string alias)
        {
            if (alias != null && (alias.Length == 0 || !Lexer.IsIdentStart(alias[0]) || !alias.All(Lexer.IsIdentChar)))
                throw new KScriptException($"as=\"{alias}\" isn't a name. Use letters, digits and _, such as as=\"math\".", "Import");
        }

        private KScriptPackage GetPackage(string name, Module module, MarkupElement import)
        {
            KScriptPackage package = Packages.Get(name);
            if (package != null) return package;
            int at = import?.GetAttribute("package")?.Value?.SourceSpan.Start ?? 0;
            if (Packages.IsKnown(name))
            {
                throw Located(new KScriptException($"{PackageRegistry.Normalise(name)} isn't installed in this copy of kscript.", "Import"), module, at);
            }
            string close = Values.Closest(PackageRegistry.Normalise(name), Packages.Names);
            throw Located(new KScriptException($"There is no package called {name}." + (close != null ? $" Did you mean {close}?" : string.Empty)
                + $" Packages: {string.Join(", ", Packages.Names.Where(n => n != "KScript.Core"))}.", "Import"), module, at);
        }

        private Module LoadImport(string src, Module from, MarkupElement import)
        {
            string full = System.IO.Path.GetFullPath(System.IO.Path.Combine(from.Directory, src));
            int at = import.GetAttribute("path").Value?.SourceSpan.Start ?? import.Span.Start;
            if (loading.Contains(full))
                throw Located(new KScriptException($"{src} imports this script back (directly or through other scripts). Imports can't go round in a circle.", "Import"), from, at);
            if (modulesByPath.TryGetValue(full, out Module cached)) return cached;
            if (!File.Exists(full))
                throw Located(new KScriptException($"There is no script at {src} (looked for {full}).", "FileNotFound"), from, at);
            return Load(full);
        }

        #endregion

        #region Checking

        private static Wording SuggestElement(string name, IEnumerable<string> names)
        {
            string close = Values.Closest(name, names);
            return close != null ? Wording.Of($" Did you mean <{close}>?") : null;
        }

        /// <summary>Finds an element's definition, or explains why it can't be used here.</summary>
        internal ElementDefinition ResolveElement(Module module, MarkupElement e)
        {
            if (module.Resolved.TryGetValue(e, out ElementDefinition known)) return known;

            string name = e.Name.StartsWith("KScript.", StringComparison.Ordinal) ? e.Name.Substring(8) : e.Name;
            if (module.Elements.TryGetValue(name, out ElementDefinition def))
            {
                module.Resolved[e] = def;
                return def;
            }

            if (Retired.RenamedElement(name) is RetiredInfo renamed)
                throw Located(new KScriptException(Wording.Prose(renamed.Message(module.ElementHint(name))), "Retired"), module, e.NameSpan.Start);
            if (Retired.Elements.TryGetValue(name, out RetiredInfo retired))
                throw Located(new KScriptException(Wording.Prose(retired.Message($"<{name}>")), "Retired"), module, e.NameSpan.Start);

            KScriptPackage owner = Packages.FindElement(name, module.Packages).FirstOrDefault();
            if (owner != null)
                throw Located(new KScriptException($"{module.ElementHint(name)} is part of {owner.Name}. Add {module.ImportHint(owner.Name)} at the top of the script.", "Import"), module, e.NameSpan.Start);

            throw Located(new KScriptException(Wording.Of($"Unknown element <{name}>.") + SuggestElement(name, module.Elements.Keys), "Syntax"), module, e.NameSpan.Start);
        }

        /// <summary>Checks an element, its attributes, its expressions and (for statement blocks) its children.</summary>
        private void CheckElement(Module module, MarkupElement e, bool isRoot = false)
        {
            if (isRoot && !module.IsPackageRoot)
            {
                foreach (MarkupAttribute a in e.Attributes)
                {
                    if (Retired.Attributes.TryGetValue(a.Name, out RetiredInfo r))
                        throw Located(new KScriptException(Wording.Prose(r.Message($"{a.Name}=\"...\"")), "Retired"), module, a.NameSpan.Start);
                    throw Located(new KScriptException($"<kscript> has no attributes, but this one has {a.Name}=\"...\".", "Syntax"), module, a.NameSpan.Start);
                }
                CheckStatements(module, e);
                return;
            }

            ElementDefinition def = ResolveElement(module, e);
            ElementInfo info = def.Info;

            if ((info.Context & ElementContext.Script) == 0 && def.Handler == null)
                throw Located(new KScriptException($"<{info.Name}> only works in web pages served by kscript serve, not in scripts.", "Syntax"), module, e.NameSpan.Start);

            NormaliseArguments(module, e, info);
            CheckAttributes(module, e, info);

            if (info.MustBeInside != null && info.MustBeInside.Count > 0 && !(info.Name == "catch"
                ? e.Parent != null && info.MustBeInside.Contains(BareName(e.Parent.Name))
                : e.Ancestors().Any(a => info.MustBeInside.Contains(BareName(a.Name))
                    || (info.Name == "return" && a.Name == "catch" && a.Parent?.Name is "type" or "model")
                    || (info.MustBeInside.Contains("fn") && module.Elements.TryGetValue(BareName(a.Name), out ElementDefinition outer) && outer.Info.IsFunctionBody))))
                throw Located(new KScriptException($"<{info.Name}> must be inside {Values.OrList(info.MustBeInside.Select(n => module.ElementHint(n)).ToList())}.", "Syntax"), module, e.NameSpan.Start);

            if (def.OwnsContent) return;

            DecodedText content = ContentOf(module, e);
            switch (info.Content)
            {
                case ContentKind.Statements:
                    CheckStatements(module, e);
                    break;

                case ContentKind.Markup:
                    // Output markup (for example a window's controls): only the KScript elements in it are checked.
                    foreach (MarkupElement child in e.ChildElements)
                    {
                        if (module.Elements.ContainsKey(BareName(child.Name))) CheckElement(module, child);
                    }
                    break;

                case ContentKind.Empty:
                    if (e.ChildElements.Any() || (content != null && content.Value.Trim().Length > 0))
                        throw Located(new KScriptException($"<{info.Name}> has no content; write it as <{info.Name} ... />.", "Syntax"), module, e.ContentSpan.Start);
                    break;

                default:
                    MarkupElement inner = e.ChildElements.FirstOrDefault();
                    if (inner != null)
                        throw Located(new KScriptException($"<{info.Name}> can only hold text, not <{inner.Name}>.", "Syntax"), module, inner.Span.Start);
                    if (content == null) break;
                    if (info.Content == ContentKind.Template)
                    {
                        // <echo format="json">{ "a": 1 }</echo> is JSON written out, used as it is.
                        bool literalJson = e.GetAttributeValue("format")?.Trim() == "json" && JsonText.IsValid(content.Value);
                        if (!literalJson) GetTemplate(module, content, bareReferences: true, check: true, info.LiteralText);
                    }
                    else if (info.Content == ContentKind.AssignmentStatements
                        || (info.Content == ContentKind.Expression && e.GetAttribute("name") == null && info.GetAttribute("name") != null))
                    {
                        GetParse(module, content, ParseMode.Statements, check: true);
                    }
                    else if (info.Content == ContentKind.Expression && content.Value.Trim().Length > 0)
                    {
                        GetParse(module, content, ParseMode.Expression, check: true);
                    }
                    break;
            }
        }

        /// <summary>Reads .kscript arguments by the one rule (see <see cref="CodeArguments"/>): quotes are strings, the rest is code.</summary>
        internal void NormaliseArguments(Module module, MarkupElement e, ElementInfo info)
        {
            if (e.Attributes.Count == 0) return;
            CodeArguments.Normalise(e, info);
            foreach (MarkupAttribute a in e.Attributes)
            {
                if (a.FormError != null) throw Located(new KScriptException(a.FormError, "Syntax"), module, a.Span.Start);
            }
        }

        private static string BareName(string name) => name.StartsWith("KScript.", StringComparison.Ordinal) ? name.Substring(8) : name;

        private void CheckStatements(Module module, MarkupElement parent)
        {
            foreach (MarkupNode node in parent.Children)
            {
                if (node is MarkupElement child)
                {
                    CheckElement(module, child);
                }
                else if (node is MarkupText text && !text.IsWhitespace)
                {
                    string shown = text.Value.Trim();
                    if (shown.Length > 40) shown = shown.Substring(0, 40) + "…";
                    throw Located(new KScriptException($"Text can't go directly inside <{parent.Name}>: '{shown}'. To print it, put it in <echo>.", "Syntax"), module, text.Content.SourceSpan.Start);
                }
            }
        }

        private void CheckAttributes(Module module, MarkupElement e, ElementInfo info)
        {
            if (module.IsCode && BareName(info.Name) == "model" && OldSpellings.ModelOptionLines(e, module.Source.Text, info.GetAttribute) is { Lines.Count: > 0 } lines)
                throw Located(new KScriptException(OldSpellings.ModelOptionsInBrackets(e.GetAttributeValue("name")?.Trim(), lines.Options), "Syntax"), module, lines.Lines[0].NameSpan.Start);

            foreach (MarkupAttribute a in e.Attributes)
            {
                AttributeInfo spec = info.GetAttribute(a.Name);
                if (module.IsCode && spec != null && OldSpellings.InArgument(e, a, spec, module.Source.Text).FirstOrDefault() is OldSpelling old)
                    throw Located(new KScriptException(old.Message, "Syntax"), module, old.Span.Start);
                if (spec == null)
                {
                    if (Retired.RenamedAttribute(info, a.Name) is RetiredInfo renamed)
                        throw Located(new KScriptException(Wording.Prose(renamed.Message($"{module.ElementHint(info.Name)}: '{a.Name}'")), "Retired"), module, a.NameSpan.Start);
                    if (info.AllowsExtraAttributes || a.Name.StartsWith("on-", StringComparison.Ordinal) && info.Content == ContentKind.Markup) continue;
                    if (Retired.Attributes.TryGetValue(a.Name, out RetiredInfo r))
                        throw Located(new KScriptException(Wording.Prose(r.Message($"{a.Name}=\"...\"")), "Retired"), module, a.NameSpan.Start);
                    string close = Values.Closest(a.Name, info.Attributes.Select(x => x.Name));
                    Wording list = info.Attributes.Count == 0 ? Wording.Of($"<{info.Name}> has no attributes.") : Wording.Of($"Its attributes are: {string.Join(", ", info.Attributes.Select(x => x.Name))}.");
                    throw Located(new KScriptException(Wording.Of($"<{info.Name}> has no attribute '{a.Name}'.") + (close != null ? $" Did you mean {close}?" : string.Empty) + " " + list, "Syntax"), module, a.NameSpan.Start);
                }

                if (a.Value == null) continue;
                switch (spec.Kind)
                {
                    case AttributeKind.Expression:
                        // A typed value may be raw text for its type (size="1920x1080"), read when it runs.
                        if ((a.Name == "value" || a.Name == "default") && e.GetAttribute("type") != null) break;
                        GetParse(module, a.Value, ParseMode.Expression, check: true);
                        break;
                    case AttributeKind.ExpressionList:
                        GetParse(module, a.Value, ParseMode.List, check: true);
                        break;
                    case AttributeKind.Template:
                        GetTemplate(module, a.Value, bareReferences: true, check: true);
                        break;
                    case AttributeKind.Enum:
                        if (spec.Options.Count > 0 && !spec.Options.Contains(a.Value.Value.Trim()) && !a.Value.Value.Contains('{'))
                            throw Located(new KScriptException($"{a.Name}=\"{a.Value.Value}\" isn't one of: {string.Join(", ", spec.Options)}.", "Syntax"), module, a.Value.SourceSpan.Start);
                        break;
                    case AttributeKind.Bool:
                        if (Values.ParseBool(a.Value.Value) == null && !a.Value.Value.Contains('{'))
                            throw Located(new KScriptException($"{a.Name}=\"{a.Value.Value}\" should be true or false.", "Syntax"), module, a.Value.SourceSpan.Start);
                        break;
                }
            }

            foreach (AttributeInfo required in info.Attributes.Where(x => x.Required))
            {
                if (e.GetAttribute(required.Name) != null) continue;
                if (required.SatisfiedByContent && ContentOf(module, e) is DecodedText t && t.Value.Trim().Length > 0) continue;
                throw Located(new KScriptException(Wording.Of($"<{info.Name}> needs a {required.Name} attribute: {Wording.Prose(required.Description)}") + (info.Example != null ? Wording.Of($" For example: {Wording.Snippet(info.Example.Split('\n')[0])}") : null), "Syntax"), module, e.NameSpan.Start);
            }
        }

        #endregion

        #region Parsing helpers

        internal enum ParseMode { Expression, List, Statements }

        internal ParseResult GetParse(Module module, DecodedText text, ParseMode mode, bool check = false)
        {
            ParseResult result;
            lock (module.ParseCache)
            {
                if (!module.ParseCache.TryGetValue(text, out result))
                {
                    result = mode switch
                    {
                        ParseMode.List => ExpressionParser.ParseExpressionList(text.Value),
                        ParseMode.Statements => ExpressionParser.ParseStatements(text.Value),
                        _ => ExpressionParser.ParseExpression(text.Value),
                    };
                    module.ParseCache[text] = result;
                    if (module.IsCode && result.Success && OldSpellings.InExpression(result.Nodes, text.Value, codeFile: true).FirstOrDefault() is OldSpelling found)
                        module.OldSpellingCache[text] = found;
                }
            }
            RefuseOldSpelling(module, text);
            if (!result.Success)
            {
                SyntaxError error = result.Errors[0];
                throw Located(new KScriptException(error.Words, "Syntax"), module, text.ToSource(error.Span.Start));
            }
            return result;
        }

        /// <summary>
        /// A .kscript file written with an old spelling doesn't run: the error is the one kscript check gives, and
        /// kscript check --fix rewrites it.
        /// </summary>
        private void RefuseOldSpelling(Module module, DecodedText text)
        {
            if (!module.IsCode || module.OldSpellingCache.Count == 0) return;
            OldSpelling found;
            lock (module.ParseCache)
            {
                if (!module.OldSpellingCache.TryGetValue(text, out found)) return;
            }
            throw Located(new KScriptException(found.Message, "Syntax"), module, text.ToSource(found.Span.Start));
        }

        internal Template GetTemplate(Module module, DecodedText text, bool bareReferences, bool check = false, TextLiterals literals = TextLiterals.None)
        {
            Template template;
            lock (module.TemplateCache)
            {
                if (!module.TemplateCache.TryGetValue(text, out template))
                {
                    template = TemplateParser.Parse(text.Value, bareReferences, literals);
                    module.TemplateCache[text] = template;
                    if (module.IsCode && !template.Errors.Any() && OldSpellings.InExpression(template.Expressions.Select(s => s.Node), text.Value, codeFile: true).FirstOrDefault() is OldSpelling found)
                        module.OldSpellingCache[text] = found;
                }
            }
            RefuseOldSpelling(module, text);
            SyntaxError error = template.Errors.FirstOrDefault();
            if (error != null)
            {
                throw Located(new KScriptException(error.Words, "Syntax"), module, text.ToSource(error.Span.Start));
            }
            return template;
        }

        /// <summary>The text inside an element (comments skipped), with positions mapped back to the document.</summary>
        internal DecodedText ContentOf(Module module, MarkupElement e)
        {
            lock (module.ContentCache)
            {
                if (module.ContentCache.TryGetValue(e, out DecodedText cached)) return cached;
                List<MarkupText> texts = e.Children.OfType<MarkupText>().ToList();
                DecodedText result = null;
                if (texts.Count == 1)
                {
                    result = texts[0].Content;
                }
                else if (texts.Count > 1)
                {
                    var value = new System.Text.StringBuilder();
                    var map = new List<int>();
                    foreach (MarkupText t in texts)
                    {
                        for (int i = 0; i < t.Value.Length; i++) map.Add(t.Content.ToSource(i));
                        value.Append(t.Value);
                    }
                    map.Add(texts[^1].Content.ToSource(texts[^1].Value.Length));
                    result = new DecodedText(value.ToString(), map.ToArray());
                }
                module.ContentCache[e] = result;
                return result;
            }
        }

        #endregion

        #region Errors

        /// <summary>Fills in an error's file, line and column from a document offset (if it has none yet).</summary>
        /// <summary>Fills in an error's file, line and column from where it happened, if it doesn't have them yet.</summary>
        public static KScriptException WithLocation(KScriptException ex, Module module) => Located(ex, module, null);

        internal static KScriptException Located(KScriptException ex, Module module, int? offset)
        {
            ex.InFile(ex.OffsetModule ?? module);
            if (ex.HasLocation) return ex;
            // The error's own position (in the module it came from), or else this one.
            (Module owner, int? at) = ex.Offset != null ? (ex.OffsetModule ?? module, ex.Offset) : (module, offset);
            if (owner != null && at != null)
            {
                (int line, int column) = owner.PositionOf(at.Value);
                ex.Line = line;
                ex.Column = column;
                ex.File = owner.Path ?? owner.DisplayName;
            }
            return ex;
        }

        /// <summary>Turns .NET exceptions from file, network and conversion code into KScript errors with a type a script can catch.</summary>
        internal static KScriptException Wrap(Exception ex) => ex switch
        {
            KScriptException k => k,
            // Sorting wraps a comparison's error ("Passwords can't be compared ..."): the error itself is the one to show.
            InvalidOperationException { InnerException: KScriptException inner } => inner,
            FileNotFoundException f => new KScriptException(f.Message, "FileNotFound", f),
            DirectoryNotFoundException d => new KScriptException(d.Message, "FileNotFound", d),
            UnauthorizedAccessException u => new KScriptException(u.Message, "Access", u),
            IOException io => new KScriptException(io.Message, "IO", io),
            HttpRequestException h => new KScriptException(h.Message, "Network", h),
            TaskCanceledException t => new KScriptException("The request timed out.", "Timeout", t),
            FormatException fe => new KScriptException(fe.Message, "Format", fe),
            OverflowException o => new KScriptException(o.Message, "Type", o),
            ArgumentException a => new KScriptException(a.Message, "Argument", a),
            InvalidOperationException i => new KScriptException(i.Message, "Error", i),
            PlatformNotSupportedException p => new KScriptException(p.Message, "Platform", p),
            NotSupportedException n => new KScriptException(n.Message, "Platform", n),
            _ => null,
        };

        #endregion

        #region Running

        /// <summary>Runs a loaded script and returns its exit code (0, or the code given to &lt;exit&gt;).</summary>
        public int Run(Module module)
        {
            if (onRunThread) return RunHere(module);

            // Scripts run on a thread with a large stack, so recursion reaches MaxCallDepth on every platform
            // (secondary threads get only 512 KB on macOS).
            int result = 0;
            System.Runtime.ExceptionServices.ExceptionDispatchInfo failure = null;
            // The thread that called Run waits for the script, running any work the script hands it with
            // InvokeOnStartingThread (desktop windows must live on the process's first thread on macOS).
            var queue = new System.Collections.Concurrent.BlockingCollection<Action>();
            var thread = new Thread(() =>
            {
                onRunThread = true;
                startingThread = queue;
                try { result = RunHere(module); }
                catch (Exception ex) { failure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex); }
                finally
                {
                    onRunThread = false;
                    startingThread = null;
                    queue.CompleteAdding();
                }
            }, RunStackSize);
            thread.Start();
            foreach (Action work in queue.GetConsumingEnumerable()) work();
            thread.Join();
            failure?.Throw();
            return result;
        }

        [ThreadStatic]
        private static System.Collections.Concurrent.BlockingCollection<Action> startingThread;

        /// <summary>
        /// Runs <paramref name="work"/> on the thread that called <see cref="Run"/> (in the kscript command, the
        /// process's main thread) and waits for it. Packages use it for work the operating system ties to that thread,
        /// such as desktop windows on macOS. Outside a run, or on that thread already, the work runs here.
        /// </summary>
        public void InvokeOnStartingThread(Action work)
        {
            var queue = startingThread;
            if (queue == null || queue.IsAddingCompleted)
            {
                work();
                return;
            }
            System.Runtime.ExceptionServices.ExceptionDispatchInfo failure = null;
            using var done = new ManualResetEventSlim();
            queue.Add(() =>
            {
                try { work(); }
                catch (Exception ex) { failure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex); }
                finally { done.Set(); }
            });
            done.Wait();
            failure?.Throw();
        }

        private const int RunStackSize = 256 * 1024 * 1024;

        [ThreadStatic]
        private static bool onRunThread;

        private int RunHere(Module module)
        {
            try
            {
                RunModule(module);
                WaitForSpawned();
                return 0;
            }
            catch (ExitSignal exit)
            {
                return exit.Code;
            }
            catch (BreakSignal)
            {
                throw Located(new KScriptException($"<break/> must be inside a loop (<for> or <while>).", "Syntax"), module, null);
            }
            catch (ContinueSignal)
            {
                throw Located(new KScriptException($"<continue/> must be inside a loop (<for> or <while>).", "Syntax"), module, null);
            }
            finally
            {
                Host.FlushLogs();
            }
        }

        /// <summary>Loads and runs a script file.</summary>
        public int RunFile(string path) => Run(Load(path));

        /// <summary>Loads and runs a script from text.</summary>
        public int RunText(string text, string path = null) => Run(LoadText(text, path));

        /// <summary>Runs a script once (imports run their script once, however many scripts import it).</summary>
        internal void RunModule(Module module)
        {
            if (module.HasRun || module.Running) return;
            module.Running = true;
            try
            {
                ConfigValue.PrepareProject(Host, module.Directory);
                module.ProjectExtensions = ProjectExtensionsFor(module);
                module.Globals = new Scope(module.ProjectExtensions?.NativeScope);
                foreach (VariableDefinition v in module.Variables.Values)
                {
                    module.Globals.Declare(v.Name, v.Value(this, module));
                }

                if (module.IsPackageRoot)
                {
                    ExecuteElement(module.Root, module.Globals, module, null);
                }
                else
                {
                    ExecuteNodes(module.Root.Children, module.Globals, module);
                }
                CollectExports(module);
            }
            finally
            {
                module.Running = false;
                module.HasRun = true;
            }
        }

        /// <summary>
        /// The kinds of thing a script declares by name at its top level, which scripts that import it see:
        /// functions, constants, enums and types. kscript make has a generator for each one.
        /// </summary>
        public static readonly IReadOnlyList<string> Declarations = new[] { "fn", "const", "enum", "type", "error", "model", "job" };

        private void CollectExports(Module module)
        {
            if (module.IsPackageRoot) return;
            foreach (MarkupElement e in module.Root.ChildElements)
            {
                string name = e.GetAttributeValue("name")?.Trim().TrimStart('$');
                if (name == null) continue;
                bool export = Declarations.Contains(e.Name) || (e.Name == "let" && Values.ParseBool(e.GetAttributeValue("const")) == true);
                if (export && module.Globals.TryGet(name, out object value)) module.Exports[name] = value;
            }
        }

        internal void ImportScript(MarkupElement import, Scope scope, Module module)
        {
            if (!module.ScriptImports.TryGetValue(import, out Module imported)) return;
            RunModule(imported);
            Extensions.Import(module, imported);
            if (import.GetAttribute("as") != null) return;
            DeclareExports(imported, import.GetAttributeValue("names"), scope);
        }

        private static void DeclareExports(Module imported, string names, Scope scope)
        {
            IEnumerable<string> wanted = names == null ? imported.Exports.Keys : NameList.Names(names).Select(n => n.TrimStart('@', '$')).Where(n => n.Length > 0);
            foreach (string name in wanted)
            {
                if (!imported.Exports.TryGetValue(name, out object value))
                {
                    string close = Values.Closest(name, imported.Exports.Keys);
                    throw new KScriptException(Wording.Of($"{imported.DisplayName} doesn't export '{name}'.") + (close != null ? $" Did you mean {close}?" : string.Empty)
                        + Wording.Of($" Scripts export their top-level <fn> functions, <const> values, <enum>s, <type>s, <error>s, <model>s and <job>s."), "Import");
                }
                scope.Declare(name, value);
            }
        }

        /// <summary>Runs the elements inside an element, in a new child scope.</summary>
        public void ExecuteChildren(MarkupElement parent, Scope scope, Module module) => ExecuteNodes(parent.Children, scope, module);

        /// <summary>
        /// Runs a list of nodes. &lt;fn&gt; declarations are hoisted (so a function can be called before it is
        /// written), and &lt;if&gt;/&lt;elif&gt;/&lt;else&gt; chains are followed.
        /// </summary>
        public void ExecuteNodes(IReadOnlyList<MarkupNode> nodes, Scope scope, Module module)
        {
            foreach (MarkupNode node in nodes)
            {
                if (node is MarkupElement fn && IsFunctionDeclaration(module, fn))
                {
                    try
                    {
                        DeclareFunction(fn, scope, module);
                    }
                    catch (KScriptException ex)
                    {
                        throw Located(ex, module, fn.Span.Start);
                    }
                }
            }

            // A library can supply the base of a type declared below its import.
            int firstStatement = 0;
            if (module.ScriptImports.Count > 0)
            {
                for (; firstStatement < nodes.Count; firstStatement++)
                {
                    if (nodes[firstStatement] is not MarkupElement import) continue;
                    if (import.Name != "import") break;
                    ExecuteElement(import, scope, module, null);
                }
            }

            // Type declarations run in source order so a refinement can use an earlier enum or type as its base.
            foreach (MarkupNode node in nodes)
            {
                if (node is MarkupElement type && IsTypeDeclaration(module, type) && !IsExtension(module, type)) ExecuteElement(type, scope, module, null);
            }

            // extend blocks come after the types, since they can add methods to one.
            foreach (MarkupNode node in nodes)
            {
                if (node is MarkupElement extend && IsExtension(module, extend)) ExecuteElement(extend, scope, module, null);
            }

            bool? chain = null;
            for (int i = firstStatement; i < nodes.Count; i++)
            {
                if (nodes[i] is MarkupElement e)
                {
                    if (IsFunctionDeclaration(module, e) || IsTypeDeclaration(module, e))
                    {
                        chain = null;
                        continue;
                    }
                    chain = ExecuteElement(e, scope, module, chain);
                }
            }
        }

        private bool IsFunctionDeclaration(Module module, MarkupElement e) =>
            e.Name == "fn" && module.DefinitionOf(e)?.Package is CorePackage;

        private static bool IsExtension(Module module, MarkupElement e) =>
            e.Name == "extend" && module.DefinitionOf(e)?.Package is CorePackage;

        private bool IsTypeDeclaration(Module module, MarkupElement e) =>
            (e.Name is "type" or "enum" or "error" or "extend" && module.DefinitionOf(e)?.Package is CorePackage)
            || module.DefinitionOf(e)?.Hoisted == true
            || (e.Name == "job" && module.DefinitionOf(e)?.Package?.Name == "KScript.Queue");

        /// <summary>Runs one element. <paramref name="chain"/> is the result of the element before it, for &lt;elif&gt; and &lt;else&gt;.</summary>
        public bool? ExecuteElement(MarkupElement e, Scope scope, Module module, bool? chain)
        {
            Step();
            ElementDefinition def = ResolveElement(module, e);
            if (def.Handler == null)
                throw Located(new KScriptException($"<{def.Name}> only works in web pages served by kscript serve.", "Syntax"), module, e.Span.Start);
            NormaliseArguments(module, e, def.Info);
            try
            {
                return def.Handler(new ElementRun(this, module, e, def, scope, chain));
            }
            catch (KScriptException ex)
            {
                throw Located(ex, module, e.Span.Start);
            }
            catch (ControlSignal)
            {
                throw;
            }
            catch (OperationCanceledException) when (Options.Cancellation.IsCancellationRequested)
            {
                throw Located(new KScriptException("The script was stopped.", "Cancelled"), module, e.Span.Start);
            }
            catch (Exception ex) when (Wrap(ex) is KScriptException wrapped)
            {
                throw Located(wrapped, module, e.Span.Start);
            }
        }

        internal UserFunction DeclareFunction(MarkupElement e, Scope scope, Module module,
            IReadOnlyList<TypeCatch> typeCatches = null, object receiver = null)
        {
            string name = e.GetAttributeValue("name")?.Trim().TrimStart('@');
            if (string.IsNullOrEmpty(name)) throw new KScriptException($"<fn> needs a name, such as <fn name=\"greet\" params=\"who\">.", "Syntax");
            List<Parameter> parameters = Parameter.ParseList(e.GetAttributeValue("params"), out string error);
            if (error != null) throw new KScriptException(error, "Syntax") { Offset = e.GetAttribute("params")?.Value?.SourceSpan.Start };

            ExprNode value = null;
            EvalContext valueContext = default;
            if (e.GetAttribute("value")?.Value is DecodedText v)
            {
                value = GetParse(module, v, ParseMode.Expression).Root;
                valueContext = new EvalContext(module, v);
            }
            var fn = new UserFunction(name, parameters, e, value, valueContext, scope, module, e.Documentation)
            {
                TypeCatches = typeCatches,
                Receiver = receiver,
            };
            scope.Declare(name, fn);
            return fn;
        }

        /// <summary>Starts a function on another thread (&lt;spawn&gt;). The run waits for spawned functions before it ends.</summary>
        internal void Spawn(ICallable fn, IReadOnlyList<object> args, Module module)
        {
            var thread = new Thread(() =>
            {
                try
                {
                    Call(fn, args, null, null, module);
                }
                catch (ControlSignal)
                {
                }
                catch (Exception ex)
                {
                    // A spawned function's error can't reach the script, so it is printed (as a KScript error).
                    ErrorReport.Write(Host.Error, ex);
                }
            }, 16 * 1024 * 1024) { IsBackground = true, Name = "kscript spawn " + fn.Name };
            lock (spawned) spawned.Add(thread);
            thread.Start();
        }

        private void WaitForSpawned()
        {
            while (true)
            {
                Thread next;
                lock (spawned)
                {
                    next = spawned.FirstOrDefault(t => t.IsAlive);
                }
                if (next == null) return;
                next.Join();
            }
        }

        internal void Step()
        {
            long n = Interlocked.Increment(ref steps);
            if (Options.MaxSteps is long max && n > max)
                throw new KScriptException($"The script took more than {max:N0} steps. Is there an endless loop?", "Limit");
            if ((n & 1023) == 0)
            {
                Options.Cancellation.ThrowIfCancellationRequested();
                if (Options.Timeout is TimeSpan limit && DateTime.UtcNow - started > limit)
                    throw new KScriptException($"The script ran for more than {limit.TotalSeconds:0.#} seconds.", "Limit");
            }
        }

        /// <summary>Calls a function value.</summary>
        public object Call(ICallable fn, IReadOnlyList<object> args, IReadOnlyDictionary<string, object> named = null, Scope scope = null, Module module = null)
        {
            int depth = callDepth.Value + 1;
            if (depth > Options.MaxCallDepth || !System.Runtime.CompilerServices.RuntimeHelpers.TryEnsureSufficientExecutionStack())
                throw new KScriptException($"Too much recursion: more than {Math.Min(depth - 1, Options.MaxCallDepth)} function calls inside each other.", "Limit");
            callDepth.Value = depth;
            try
            {
                return fn.Call(new CallContext(this, fn.Name, args, named, scope, module));
            }
            finally
            {
                callDepth.Value = depth - 1;
            }
        }

        #endregion
    }
}
