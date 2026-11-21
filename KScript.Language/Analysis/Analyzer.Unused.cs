using KScript.Metadata;
using System.Text.RegularExpressions;

using KScript.Language.Catalog;
using KScript.Projects;
using KScript.Syntax.Markup;
using KScript.Syntax;
using KScript.Syntax.Text;

namespace KScript.Language.Analysis
{
    // Unused code: functions nothing calls (KS6002), variables nothing reads (KS6003), parameters (KS6004) and
    // imports (KS6005). They are style rules, so kscript:recommended makes them warnings and a project can turn them
    // off. Editors show the code faded (DiagnosticCodes.IsUnnecessary). A name that starts with _ is never reported.
    public sealed partial class Analyzer
    {
        private readonly HashSet<string> usedPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<MarkupElement, List<Symbol>> functionParameters = new Dictionary<MarkupElement, List<Symbol>>();
        private readonly List<List<Symbol>> lambdaParameters = new List<List<Symbol>>();

        // Variables a control or input is bound to (bind="draft"): written, but read too.
        private readonly HashSet<Symbol> boundVariables = new HashSet<Symbol>();

        private static readonly string[] ResourceActions = { "index", "store", "show", "update", "destroy" };

        // Uses of each symbol (references that are not its declaration).
        private ILookup<Symbol, SymbolReference> uses;

        // Symbols by declaration: the same name can be declared more than once at one place (a region analysed twice).
        private ILookup<(string, TextSpan), Symbol> declarations;

        /// <summary><c>lib.slugify</c> through an import alias: a use of slugify from lib's file.</summary>
        private void NoteMemberUse(Symbol import, string member)
        {
            if (import.SourcePath != null && !string.IsNullOrEmpty(member))
            {
                result.ExternalUses.Add((import.SourcePath, member));
            }
        }

        /// <summary>
        /// &lt;Resource path="/people" to="people" /&gt; (KScript.Http) routes to the index, store, show, update and
        /// destroy functions of the script imported as people (or only="..." of them, or all but except="...").
        /// </summary>
        private void NoteResourceUses(MarkupElement e, ElementInfo info, Scope scope)
        {
            if (info.Name != "Resource" || info.Package != "KScript.Http" || !(e.GetAttribute("to")?.Value is DecodedText to))
            {
                return;
            }
            TextSpan span = TrimmedSpan(to);
            if (!(scope.Lookup(to.Value.Trim(), span.Start) is Symbol alias) || alias.Kind != SymbolKind.Import)
            {
                return;
            }
            result.References.Add(new SymbolReference(alias, span, false, false));
            HashSet<string> only = ActionNames(e.GetAttributeValue("only"));
            HashSet<string> except = ActionNames(e.GetAttributeValue("except"));
            foreach (string action in ResourceActions.Where(a => (only.Count == 0 || only.Contains(a)) && !except.Contains(a)))
            {
                NoteMemberUse(alias, action);
            }
        }

        private static HashSet<string> ActionNames(string list) =>
            new HashSet<string>(NameList.Names(list), StringComparer.Ordinal);

        private void CheckUnused(bool projectWide)
        {
            uses = result.References.Where(r => !r.IsDeclaration).ToLookup(r => r.Symbol);
            declarations = result.Symbols.Where(s => s.DeclarationSpan.Length > 0).ToLookup(s => (s.Name, s.DeclarationSpan));
            foreach (SymbolReference r in result.References)
            {
                if (!r.IsDeclaration && r.Symbol.SourcePath != null && (r.Symbol.Kind == SymbolKind.Function || r.Symbol.Kind == SymbolKind.Constant))
                {
                    result.ExternalUses.Add((r.Symbol.SourcePath, r.Symbol.Name));
                }
            }

            ProjectUses project = null;
            ProjectUses Project() => project ??= ProjectUsage.Find(result.Path, source.Text, files, catalog);

            HashSet<Symbol> unusedFunctions = projectWide ? CheckUnusedFunctions(Project) : new HashSet<Symbol>();
            CheckUnusedVariables(projectWide, Project);
            CheckUnusedParameters(unusedFunctions);
            CheckUnusedImports();
        }

        /// <summary>True if anything reads the symbol (or a symbol declared at the same place). A function calling itself doesn't count.</summary>
        private bool IsRead(Symbol symbol) =>
            SameDeclaration(symbol).Any(s => boundVariables.Contains(s) || uses[s].Any(r => !r.IsWrite && !(s.Kind == SymbolKind.Function && s.DeclaringElement.Span.Contains(r.Span.Start))));

        private IEnumerable<Symbol> SameDeclaration(Symbol symbol) =>
            symbol.DeclarationSpan.Length > 0 ? declarations[(symbol.Name, symbol.DeclarationSpan)] : new[] { symbol };

        private static bool Ignored(Symbol symbol) => symbol.Name.StartsWith("_", StringComparison.Ordinal);

        #region Functions

        private HashSet<Symbol> CheckUnusedFunctions(Func<ProjectUses> project)
        {
            HashSet<Symbol> unused = new HashSet<Symbol>();
            if (Kind != DocumentKind.Script)
            {
                return unused;
            }

            List<Symbol> candidates = result.Symbols
                .Where(s => s.Kind == SymbolKind.Function && s.SourcePath == null && s.DeclaringElement?.Name == "fn" && s.DeclaringElement.Parent?.Name != "extend" && !Ignored(s) && s.Doc?.IsPublic != true && !IsRead(s))
                .ToList();
            if (candidates.Count == 0)
            {
                return unused;
            }

            // Middleware named in kscript.json (http.middleware) is called by the server.
            HashSet<string> mentioned = ConfigMentions();
            candidates.RemoveAll(s => mentioned.Contains(s.Name));
            foreach (Symbol fn in candidates)
            {
                ProjectUses uses = project();
                if (uses.Names.Contains(fn.Name))
                {
                    continue;
                }
                unused.Add(fn);
                string message = $"The function {fn.Name} is never called."
                    + (uses.Importers.Count > 0 ? $" {ImporterText(uses)} this script but {(uses.Importers.Count == 1 ? "doesn't" : "don't")} call it." : string.Empty);
                Report(fn.DeclarationSpan, DiagnosticSeverity.Warning, DiagnosticCodes.UnusedFunction, message,
                    new[] { RemoveElementFix(fn.DeclaringElement, $"Remove the function {fn.Name}"), PrefixFix(fn) });
            }
            return unused;
        }

        private string ImporterText(ProjectUses uses)
        {
            string first = directory != null ? System.IO.Path.GetRelativePath(directory, uses.Importers[0]).Replace('\\', '/') : uses.Importers[0];
            return uses.Importers.Count == 1 ? first + " imports" : $"{first} and {uses.Importers.Count - 1} other file{(uses.Importers.Count == 2 ? string.Empty : "s")} import";
        }

        /// <summary>Every string in the nearest kscript.json, and the part of each before a ':' (throttle:30).</summary>
        private HashSet<string> ConfigMentions()
        {
            HashSet<string> found = new HashSet<string>(StringComparer.Ordinal);
            for (string dir = directory; dir != null; dir = System.IO.Path.GetDirectoryName(dir))
            {
                string file = System.IO.Path.Combine(dir, ProjectConfig.FileName);
                if (!files.FileExists(file))
                {
                    continue;
                }
                foreach (Match m in Regex.Matches(files.ReadText(file) ?? string.Empty, "\"((?:[^\"\\\\]|\\\\.)*)\""))
                {
                    string value = m.Groups[1].Value.Trim();
                    found.Add(value);
                    found.Add(value.Split(':')[0].Trim());
                }
                break;
            }
            return found;
        }

        #endregion

        #region Variables

        private void CheckUnusedVariables(bool projectWide, Func<ProjectUses> project)
        {
            MarkupElement root = result.Markup.RootElements.FirstOrDefault();
            List<Symbol> exported = new List<Symbol>();
            foreach (Symbol s in result.Symbols)
            {
                if ((s.Kind != SymbolKind.Variable && s.Kind != SymbolKind.Constant && s.Kind != SymbolKind.LoopVariable)
                    || s.SourcePath != null || s.DeclaringElement == null || s.DeclarationSpan.Length == 0 || Ignored(s))
                {
                    continue;
                }
                // Types and enums are used by name in params and type attributes, which aren't references.
                if (s.EnumValues != null || s.Shape.Type == KType.Type || s.DeclaringElement.Name == "type" || s.DeclaringElement.Name == "enum" || s.DeclaringElement.Name == "error" || s.DeclaringElement.Name == "job")
                {
                    continue;
                }
                // A page's variables can be read by its layout and components, so only a function's locals are checked there.
                if (Kind != DocumentKind.Script && s.DeclaringElement.Name != "fn" && !s.DeclaringElement.Ancestors().Any(a => a.Name == "fn"))
                {
                    continue;
                }
                if (IsRead(s))
                {
                    continue;
                }
                // A script's top-level constants are exported, like its functions.
                if (Kind == DocumentKind.Script && s.IsConst && s.DeclaringElement.Parent == root)
                {
                    exported.Add(s);
                    continue;
                }
                ReportUnusedVariable(s, null);
            }

            if (!projectWide)
            {
                return;
            }
            foreach (Symbol s in exported)
            {
                ProjectUses uses = project();
                if (!uses.Names.Contains(s.Name))
                {
                    ReportUnusedVariable(s, uses);
                }
            }
        }

        private void ReportUnusedVariable(Symbol s, ProjectUses project)
        {
            bool written = SameDeclaration(s).Any(x => uses[x].Any(r => r.IsWrite));
            string what = s.IsConst ? "The constant" : s.Kind == SymbolKind.LoopVariable ? "The loop variable" : "The variable";
            string message = written ? $"{what} ${s.Name} is set but its value is never read." : $"{what} ${s.Name} is never used.";
            if (project?.Importers.Count > 0)
            {
                message += $" {ImporterText(project)} this script but {(project.Importers.Count == 1 ? "doesn't" : "don't")} use it.";
            }

            List<QuickFix> fixes = new List<QuickFix>();
            if (!written && IsRemovableDeclaration(s))
            {
                fixes.Add(RemoveElementFix(s.DeclaringElement, $"Remove ${s.Name}"));
            }
            fixes.Add(PrefixFix(s));
            Report(s.DeclarationSpan, DiagnosticSeverity.Warning, DiagnosticCodes.UnusedVariable, message, fixes);
        }

        /// <summary>A &lt;let&gt; or &lt;const&gt; (or a .kscript assignment) that declares only this name can be deleted.</summary>
        private bool IsRemovableDeclaration(Symbol s)
        {
            MarkupElement e = s.DeclaringElement;
            bool removable = e.Name == "let" || e.Name == "const" || (e.Name == "do" && result.Markup.Syntax == SourceSyntax.Code);
            return removable && result.Symbols.Where(x => x.DeclaringElement == e && x.Kind != SymbolKind.Parameter).Select(x => x.DeclarationSpan).Distinct().Count() == 1;
        }

        #endregion

        #region Parameters

        private void CheckUnusedParameters(HashSet<Symbol> unusedFunctions)
        {
            foreach ((MarkupElement fn, List<Symbol> parameters) in functionParameters)
            {
                // An unused function is reported once, not once more for each of its parameters.
                if (unusedFunctions.Any(f => f.DeclaringElement == fn))
                {
                    continue;
                }
                for (int i = 0; i < parameters.Count; i++)
                {
                    Symbol p = parameters[i];
                    if (Ignored(p) || IsRead(p))
                    {
                        continue;
                    }
                    List<QuickFix> fixes = new List<QuickFix>();
                    if (i == parameters.Count - 1)
                    {
                        fixes.Add(RemoveParameterFix(fn, p, first: i == 0));
                    }
                    fixes.Add(PrefixFix(p));
                    Report(p.DeclarationSpan, DiagnosticSeverity.Warning, DiagnosticCodes.UnusedParameter, $"The parameter ${p.Name} is never used.", fixes);
                }
            }

            // A lambda's caller decides what it is passed, so only the parameters after the last one it reads are reported.
            foreach (List<Symbol> parameters in lambdaParameters)
            {
                int lastRead = parameters.FindLastIndex(IsRead);
                foreach (Symbol p in parameters.Skip(lastRead + 1).Where(p => !Ignored(p)))
                {
                    Report(p.DeclarationSpan, DiagnosticSeverity.Warning, DiagnosticCodes.UnusedParameter, $"The lambda parameter ${p.Name} is never used.", new[] { PrefixFix(p) });
                }
            }
        }

        /// <summary>Removes the last parameter of a function, with the comma before it (or the whole params="..." when it is the only one).</summary>
        private QuickFix RemoveParameterFix(MarkupElement fn, Symbol p, bool first)
        {
            string title = $"Remove the parameter ${p.Name}";
            MarkupAttribute attr = fn.GetAttribute("params");
            TextSpan value = TrimmedSpan(attr.Value);
            if (!first)
            {
                int comma = source.Text.LastIndexOf(',', p.DeclarationSpan.Start - 1, p.DeclarationSpan.Start - value.Start);
                return new QuickFix(title, new TextChange(TextSpan.FromBounds(comma, value.End), string.Empty));
            }
            if (result.Markup.Syntax == SourceSyntax.Code)
            {
                return new QuickFix(title, new TextChange(value, string.Empty));
            }
            int start = attr.Span.Start;
            while (start > 0 && (source.Text[start - 1] == ' ' || source.Text[start - 1] == '\t'))
            {
                start--;
            }
            return new QuickFix(title, new TextChange(TextSpan.FromBounds(start, attr.Span.End), string.Empty));
        }

        #endregion

        #region Imports

        private void CheckUnusedImports()
        {
            foreach (MarkupElement e in result.Markup.DescendantElements().Where(x => IsKScriptElementName(x, "import")).ToList())
            {
                if (e.GetAttribute("package")?.Value is DecodedText package)
                {
                    CheckPackageImport(e, package);
                    continue;
                }

                Symbol alias = result.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Import && s.DeclaringElement == e);
                List<Symbol> brought = result.Symbols.Where(s => s.DeclaringElement == e && s.SourcePath != null && s.Kind != SymbolKind.Import).ToList();
                // names="a, b" declares each name where it is written; a bare import brings every export in at src.
                List<Symbol> named = brought.Where(s => result.References.Any(r => r.IsDeclaration && r.Symbol == s)).ToList();
                List<Symbol> all = brought.Except(named).ToList();
                string src = e.GetAttributeValue("src")?.Trim();

                bool aliasUnused = alias != null && alias.Shape.Fields.Count > 0 && !uses[alias].Any();
                List<Symbol> unusedNames = named.Where(s => !IsRead(s) && !result.Diagnostics.Any(d => d.Span.Equals(s.DeclarationSpan) && d.Code == DiagnosticCodes.UndefinedFunction)).ToList();
                bool nothingUsed = (alias == null || aliasUnused) && unusedNames.Count == named.Count;

                if (aliasUnused)
                {
                    Report(alias.DeclarationSpan, DiagnosticSeverity.Warning, DiagnosticCodes.UnusedImport, $"Nothing imported as {alias.Name} is used.",
                        new[] { nothingUsed ? RemoveElementFix(e, "Remove the import") : RemoveAttributeFix(e.GetAttribute("as"), "Remove as=\"" + alias.Name + "\"") });
                }
                foreach (Symbol s in unusedNames)
                {
                    QuickFix fix = nothingUsed && unusedNames.Count == 1 ? null : RemoveFromListFix(s.DeclarationSpan, $"Remove {s.Name} from the import");
                    fix ??= nothingUsed ? RemoveElementFix(e, "Remove the import") : RemoveAttributeFix(e.GetAttribute("names"), $"Remove names=\"{s.Name}\"");
                    Report(s.DeclarationSpan, DiagnosticSeverity.Warning, DiagnosticCodes.UnusedImport, $"{s.Name} is imported but never used.", new[] { fix });
                }
                if (alias == null && named.Count == 0 && all.Count > 0 && !all.Any(IsRead) && e.GetAttribute("src")?.Value is DecodedText srcValue)
                {
                    Report(TrimmedSpan(srcValue), DiagnosticSeverity.Warning, DiagnosticCodes.UnusedImport, $"Nothing from {src} is used.", new[] { RemoveElementFix(e, "Remove the import") });
                }
            }
        }

        private void CheckPackageImport(MarkupElement e, DecodedText value)
        {
            string package = catalog.FindPackage(value.Value.Trim());
            if (!ChecksImports || package == null || package == "KScript.Core")
            {
                return;
            }
            Symbol alias = result.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Import && s.DeclaringElement == e);
            if ((alias != null && uses[alias].Any()) || PackageUsed(package))
            {
                return;
            }
            // In a route file, importing KScript.Auth turns on the CSRF check for its web routes.
            if (package == "KScript.Auth" && result.Markup.RootElements.Any(r => r.Name == "KScript.Routes"))
            {
                return;
            }
            Report(TrimmedSpan(value), DiagnosticSeverity.Warning, DiagnosticCodes.UnusedImport, $"Nothing from {package} is used.", new[] { RemoveElementFix(e, "Remove the import") });
        }

        /// <summary>True if the script uses an element, command, method or variable of the package, or of a package that needs it.</summary>
        private bool PackageUsed(string package)
        {
            HashSet<string> used = new HashSet<string>(usedPackages, StringComparer.OrdinalIgnoreCase);
            foreach (SymbolReference r in result.References.Where(r => r.Symbol.Kind == SymbolKind.BuiltIn))
            {
                if (catalog.PackageOfVariable(r.Symbol.Name) is string p)
                {
                    used.Add(p);
                }
            }
            return used.Any(u => catalog.WithRequired(u).Contains(package, StringComparer.OrdinalIgnoreCase));
        }

        #endregion

        #region Fixes

        /// <summary>Renames the symbol (its declaration and every write) to _name, which marks it as unused on purpose.</summary>
        private QuickFix PrefixFix(Symbol symbol)
        {
            List<TextSpan> spans = new List<TextSpan> { symbol.DeclarationSpan };
            foreach (Symbol s in SameDeclaration(symbol))
            {
                spans.AddRange(uses[s].Select(r => r.Span));
            }
            TextChange[] changes = spans.Distinct().OrderBy(s => s.Start).Select(s => new TextChange(new TextSpan(s.Start, 0), "_")).ToArray();
            return new QuickFix($"Rename to _{symbol.Name} (unused on purpose)", changes);
        }

        /// <summary>Deletes an element, with its doc comment, and the lines it was on when nothing else is on them.</summary>
        private QuickFix RemoveElementFix(MarkupElement e, string title)
        {
            int start = e.Span.Start;
            List<MarkupNode> siblings = e.Parent?.Children ?? result.Markup.Children;
            for (int i = siblings.IndexOf(e) - 1; i >= 0; i--)
            {
                if (siblings[i] is MarkupText t && t.IsWhitespace)
                {
                    continue;
                }
                if (siblings[i] is MarkupComment c && c.IsDoc)
                {
                    start = c.Span.Start;
                }
                break;
            }
            if (result.Markup.Syntax == SourceSyntax.Code)
            {
                // /// doc lines above a .kscript declaration.
                for (int line = source.GetPosition(start).Line - 1; line >= 0; line--)
                {
                    int lineStart = source.GetOffset(line, 0);
                    int lineEnd = source.Text.IndexOf('\n', lineStart);
                    if (!source.Text.Substring(lineStart, lineEnd - lineStart).TrimStart().StartsWith("///", StringComparison.Ordinal))
                    {
                        break;
                    }
                    start = lineStart;
                }
            }
            return new QuickFix(title, new TextChange(WholeLines(TextSpan.FromBounds(start, e.Span.End)), string.Empty));
        }

        /// <summary>Deletes an attribute and the space before it.</summary>
        private QuickFix RemoveAttributeFix(MarkupAttribute attr, string title)
        {
            int start = attr.Span.Start;
            while (start > 0 && char.IsWhiteSpace(source.Text[start - 1]))
            {
                start--;
            }
            return new QuickFix(title, new TextChange(TextSpan.FromBounds(start, attr.Span.End), string.Empty));
        }

        /// <summary>Deletes a name from a comma-separated list (names="a, b"), with the comma that goes with it; null if it is the only name.</summary>
        private QuickFix RemoveFromListFix(TextSpan name, string title)
        {
            string t = source.Text;
            int after = name.End;
            while (after < t.Length && (t[after] == ' ' || t[after] == '\t'))
            {
                after++;
            }
            if (after < t.Length && t[after] == ',')
            {
                after++;
                while (after < t.Length && (t[after] == ' ' || t[after] == '\t'))
                {
                    after++;
                }
                return new QuickFix(title, new TextChange(TextSpan.FromBounds(name.Start, after), string.Empty));
            }
            int before = name.Start;
            while (before > 0 && (t[before - 1] == ' ' || t[before - 1] == '\t'))
            {
                before--;
            }
            return before > 0 && t[before - 1] == ','
                ? new QuickFix(title, new TextChange(TextSpan.FromBounds(before - 1, name.End), string.Empty))
                : null;
        }

        /// <summary>The span grown to whole lines (with the line break) when only spaces share its lines.</summary>
        private TextSpan WholeLines(TextSpan span)
        {
            string t = source.Text;
            int start = span.Start;
            int end = span.End;
            while (start > 0 && (t[start - 1] == ' ' || t[start - 1] == '\t'))
            {
                start--;
            }
            while (end < t.Length && (t[end] == ' ' || t[end] == '\t' || t[end] == '\r'))
            {
                end++;
            }
            if ((start > 0 && t[start - 1] != '\n') || (end < t.Length && t[end] != '\n'))
            {
                return span;
            }
            return TextSpan.FromBounds(start, end < t.Length ? end + 1 : end);
        }

        #endregion
    }
}
