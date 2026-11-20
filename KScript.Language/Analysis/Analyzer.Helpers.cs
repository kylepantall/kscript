using KScript.Language.Catalog;
using KScript.Syntax.Markup;
using KScript.Syntax;
using KScript.Syntax.Text;

namespace KScript.Language.Analysis
{
    public sealed partial class Analyzer
    {
        #region Regions

        private ExpressionRegion NewRegion(RegionKind kind, DecodedText text, MarkupElement element, MarkupAttribute attribute, Scope scope, ParseResult parse = null, Template template = null)
        {
            ExpressionRegion region = new ExpressionRegion
            {
                Kind = kind,
                Source = text,
                Element = element,
                Attribute = attribute,
                Scope = scope,
                Parse = parse,
                Template = template,
            };
            result.Regions.Add(region);
            return region;
        }

        private ExpressionRegion AnalyzeExpressionRegion(DecodedText text, RegionKind kind, Scope scope, MarkupElement element, MarkupAttribute attribute)
        {
            ParseResult parse;
            if (string.IsNullOrWhiteSpace(text.Value))
            {
                parse = new ParseResult(text.Value, new List<ExprNode>(), new List<SyntaxError>());
                if (kind == RegionKind.Expression)
                {
                    Report(attribute != null ? attribute.Span : element.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.ExpressionSyntax,
                        attribute != null ? $"'{attribute.Name}' needs an expression." : $"<{element.Name}> needs a value.");
                }
            }
            else
            {
                parse = kind == RegionKind.ExpressionList ? ExpressionParser.ParseExpressionList(text.Value) : ExpressionParser.ParseExpression(text.Value);
            }

            ExpressionRegion region = NewRegion(kind, text, element, attribute, scope, parse);
            ReportSyntaxErrors(region, parse.Errors);
            foreach (ExprNode node in parse.Nodes)
            {
                AnalyzeExpr(node, region, scope, ExprContext.Value);
            }
            return region;
        }

        private void AnalyzeTemplate(DecodedText text, Scope scope, MarkupElement element, MarkupAttribute attribute, bool allowBare)
        {
            if (text.Value.IndexOf('{') < 0 && (!allowBare || (text.Value.IndexOf('$') < 0 && text.Value.IndexOf('@') < 0)))
            {
                return;
            }

            Template template = TemplateParser.Parse(text.Value, allowBare);
            if (!template.Expressions.Any())
            {
                return;
            }

            ExpressionRegion region = NewRegion(RegionKind.Template, text, element, attribute, scope, template: template);
            foreach (TemplateSegment segment in template.Expressions)
            {
                ReportSyntaxErrors(region, segment.Expression.Errors);
                if (segment.Node != null)
                {
                    AnalyzeExpr(segment.Node, region, scope, ExprContext.Value);
                }
            }
        }

        private void ReportSyntaxErrors(ExpressionRegion region, IEnumerable<SyntaxError> errors)
        {
            foreach (SyntaxError error in errors)
            {
                TextSpan span = region.Source.ToSource(error.Span);
                if (span.Length == 0)
                {
                    // Point at something visible: the character before an "expected more" error.
                    span = span.Start > region.SourceSpan.Start ? new TextSpan(span.Start - 1, 1) : new TextSpan(span.Start, Math.Min(1, source.Length - span.Start));
                }
                Report(span, DiagnosticSeverity.Error, error.Code, error.Message);
            }
        }

        #endregion

        #region Names

        private Symbol NewVariable(string name, TextSpan span, MarkupElement element, SymbolKind kind, TypeShape shape)
        {
            Symbol symbol = new Symbol { Name = name, Kind = kind, DeclarationSpan = span, DeclaringElement = element, Shape = shape ?? TypeShape.Unknown };
            result.Symbols.Add(symbol);
            result.References.Add(new SymbolReference(symbol, span, true, true));
            return symbol;
        }

        private Symbol DeclareFromAttribute(MarkupElement e, string attribute, Scope scope, int visibleFrom, SymbolKind kind, TypeShape shape)
        {
            MarkupAttribute attr = e.GetAttribute(attribute);
            if (attr?.Value == null || string.IsNullOrWhiteSpace(attr.ValueText))
            {
                return null;
            }

            if (!CheckIdentifier(attr.Value))
            {
                return null;
            }

            string name = attr.ValueText.Trim().TrimStart('$');
            TextSpan span = TrimmedSpan(attr.Value);
            if (attr.ValueText.Trim().StartsWith("$", StringComparison.Ordinal))
            {
                span = TextSpan.FromBounds(span.Start + 1, span.End);
            }

            // Re-declaring in the same scope is fine at the top of a script (<let> twice), but it is usually a mistake.
            Symbol existing = scope.Symbols.Select(s => s.Symbol).LastOrDefault(s => s.Name == name && s.Kind != SymbolKind.Function && s.Kind != SymbolKind.BuiltIn);
            if (existing != null && existing.Kind == SymbolKind.Constant)
            {
                Report(span, DiagnosticSeverity.Error, DiagnosticCodes.AssignConst, $"${name} is a constant and is already declared.");
            }

            Symbol symbol = NewVariable(name, span, e, kind, shape);
            scope.Declare(symbol, visibleFrom);
            return symbol;
        }

        private bool CheckIdentifier(DecodedText value)
        {
            if (value == null)
            {
                return false;
            }
            string name = value.Value.Trim().TrimStart('$');
            if (name.Length == 0)
            {
                Report(TrimmedSpan(value), DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue, "A name is needed here.");
                return false;
            }
            if (!Lexer.IsIdentStart(name[0]) || !name.All(Lexer.IsIdentChar))
            {
                Report(TrimmedSpan(value), DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue,
                    $"'{name}' isn't a valid name. Use letters, digits and _, starting with a letter.");
                return false;
            }
            if (Lexer.Keywords.Contains(name))
            {
                Report(TrimmedSpan(value), DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue, $"'{name}' is a keyword and can't be used as a name.");
                return false;
            }
            return true;
        }

        private Symbol ReferenceVariableByName(DecodedText value, Scope scope, bool isWrite, bool mustBeMutable)
        {
            if (value == null || !CheckIdentifier(value))
            {
                return null;
            }
            TextSpan span = TrimmedSpan(value);
            string name = value.Value.Trim();
            if (name.StartsWith("$", StringComparison.Ordinal))
            {
                name = name.Substring(1);
                span = TextSpan.FromBounds(span.Start + 1, span.End);
            }
            return ReferenceVariable(name, span, scope, isWrite, mustBeMutable, span.Start);
        }

        private Symbol ReferenceVariable(string name, TextSpan span, Scope scope, bool isWrite, bool mustBeMutable, int offset)
        {
            Symbol symbol = scope.Lookup(name, offset);
            if (symbol != null && symbol.Kind != SymbolKind.Function && symbol.Kind != SymbolKind.Import)
            {
                result.References.Add(new SymbolReference(symbol, span, false, isWrite));
                if (mustBeMutable && symbol.IsConst)
                {
                    Report(span, DiagnosticSeverity.Error, DiagnosticCodes.AssignConst, $"${name} is a constant (const=\"yes\") and can't be changed.");
                }
                else if (mustBeMutable && (symbol.Kind == SymbolKind.BuiltIn || symbol.Kind == SymbolKind.FrontMatter))
                {
                    Report(span, DiagnosticSeverity.Error, DiagnosticCodes.AssignConst, $"${name} is provided by KScript and can't be changed. Declare your own with <let>.");
                }
                return symbol;
            }

            if (result.IsPartial && !isWrite)
            {
                // Layouts and components get their variables from the page that uses them: these are its props.
                if (!result.Inputs.ContainsKey(name))
                {
                    result.Inputs[name] = span;
                }
                return null;
            }

            string package = catalog.PackageOfVariable(name);
            if (!isWrite && ChecksImports && package != null && !importedPackages.Contains(package))
            {
                RequirePackage(package, span, $"${name}");
                return null;
            }

            IEnumerable<string> candidates = scope.Innermost(offset).VisibleAt(offset)
                .Where(s => s.Kind != SymbolKind.Function && s.Kind != SymbolKind.Import).Select(s => s.Name);
            string suggestion = Suggestions.Closest(name, candidates);
            string message = isWrite
                ? (result.Markup.Syntax == SourceSyntax.Code
                    ? $"${name} is not defined, so it can't be updated. Give it a starting value first: {name} = ..."
                    : $"${name} is not defined. Declare it first with <let name=\"{name}\" ... />.")
                : $"${name} is not defined." + (suggestion != null ? $" Did you mean ${suggestion}?" : string.Empty);
            Report(span, DiagnosticSeverity.Error, isWrite ? DiagnosticCodes.SetUndeclared : DiagnosticCodes.UndefinedVariable, message,
                suggestion != null ? new[] { new QuickFix($"Change to ${suggestion}", new TextChange(span, suggestion)) } : null);
            return null;
        }

        private void ReferenceFunctionByName(DecodedText value, Scope scope)
        {
            if (value == null || string.IsNullOrWhiteSpace(value.Value))
            {
                return;
            }
            string name = value.Value.Trim().TrimStart('@');
            TextSpan span = TrimmedSpan(value);
            if (value.Value.Trim().StartsWith("@", StringComparison.Ordinal))
            {
                span = TextSpan.FromBounds(span.Start + 1, span.End);
            }
            ReferenceFunction(name, span, scope);
        }

        /// <summary>A call to a function whose doc comment says @deprecated gets a warning with the reason.</summary>
        private void ReportIfDeprecated(Symbol fn, TextSpan span)
        {
            if (fn.Doc?.Deprecated is string reason)
            {
                Report(span, DiagnosticSeverity.Warning, DiagnosticCodes.Deprecated, $"@{fn.Name} is deprecated." + (reason.Length > 0 ? " " + reason : string.Empty));
            }
        }

        private Symbol ReferenceFunction(string name, TextSpan span, Scope scope)
        {
            int dot = name.IndexOf('.');
            if (dot > 0 && dot < name.Length - 1)
            {
                ReferenceImportedFunction(name, dot, span, scope);
                return null;
            }
            Symbol fn = scope.Lookup(name, int.MaxValue);
            if (fn != null && fn.Kind == SymbolKind.Function)
            {
                result.References.Add(new SymbolReference(fn, span, false, false));
                ReportIfDeprecated(fn, span);
                return fn;
            }

            if (catalog.GetCommand(name) != null)
            {
                return null;
            }

            IEnumerable<string> candidates = result.Symbols.Where(s => s.Kind == SymbolKind.Function).Select(s => s.Name);
            string suggestion = Suggestions.Closest(name, candidates);
            Report(span, DiagnosticSeverity.Error, DiagnosticCodes.UndefinedFunction,
                $"No function named '{name}'. Declare it with <fn name=\"{name}\">." + (suggestion != null ? $" Did you mean '{suggestion}'?" : string.Empty),
                SuggestFix(span, name, candidates, n => n));
            return null;
        }

        /// <summary><c>people.show</c> in a function name attribute: a function of a script (or package) imported with as="people".</summary>
        private void ReferenceImportedFunction(string name, int dot, TextSpan span, Scope scope)
        {
            string aliasName = name.Substring(0, dot), member = name.Substring(dot + 1);
            TextSpan aliasSpan = new TextSpan(span.Start, dot);
            TextSpan memberSpan = TextSpan.FromBounds(span.Start + dot + 1, span.End);
            Symbol import = scope.Lookup(aliasName, aliasSpan.Start);
            if (import?.Kind != SymbolKind.Import)
            {
                IEnumerable<string> aliases = result.Symbols.Where(s => s.Kind == SymbolKind.Import).Select(s => s.Name);
                Report(aliasSpan, DiagnosticSeverity.Error, DiagnosticCodes.UndefinedFunction,
                    $"Nothing is imported as '{aliasName}'. Import it with <import src=\"...\" as=\"{aliasName}\" />.",
                    SuggestFix(aliasSpan, aliasName, aliases, n => n));
                return;
            }
            result.References.Add(new SymbolReference(import, aliasSpan, false, false));
            if (import.Shape.Fields.Count > 0 && !import.Shape.Fields.ContainsKey(member))
            {
                Report(memberSpan, DiagnosticSeverity.Error, DiagnosticCodes.UndefinedFunction, $"The import '{aliasName}' has no '{member}'.",
                    SuggestFix(memberSpan, member, import.Shape.Fields.Keys, n => n));
            }
        }

        private void CheckFunctionArgumentCount(Symbol fn, int count, TextSpan span)
        {
            int required = fn.RequiredParameterCount;
            int max = fn.Parameters.Count;
            if (count < required || count > max)
            {
                string expected = required == max ? $"{max}" : $"{required} to {max}";
                Report(span, DiagnosticSeverity.Error, DiagnosticCodes.ArgumentCount,
                    $"{fn.Signature} takes {expected} argument{(max == 1 && required == max ? string.Empty : "s")}, but {count} {(count == 1 ? "was" : "were")} given.");
            }
        }

        private static IReadOnlyList<QuickFix> SuggestFix(TextSpan span, string name, IEnumerable<string> candidates, Func<string, string> format)
        {
            string suggestion = Suggestions.Closest(name, candidates);
            return suggestion != null ? new[] { new QuickFix($"Change to {format(suggestion)}", new TextChange(span, suggestion)) } : null;
        }

        /// <summary>Parses <c>who, greeting = 'Hello', count: int = 1</c>.</summary>
        private List<FunctionParameter> ParseParameters(DecodedText value, bool report)
        {
            List<FunctionParameter> list = new List<FunctionParameter>();
            string text = value.Value;
            int depth = 0;
            char quote = '\0';
            int start = 0;
            for (int i = 0; i <= text.Length; i++)
            {
                char c = i < text.Length ? text[i] : ',';
                if (quote != '\0')
                {
                    if (c == quote)
                    {
                        quote = '\0';
                    }
                    continue;
                }
                if (c == '\'' || c == '"')
                {
                    quote = c;
                }
                else if (c == '(' || c == '[' || c == '{')
                {
                    depth++;
                }
                else if (c == ')' || c == ']' || c == '}')
                {
                    depth--;
                }
                else if (c == ',' && depth <= 0)
                {
                    FunctionParameter p = ParseParameter(value, start, i, report);
                    if (p != null)
                    {
                        if (list.Any(x => x.Name == p.Name) && report)
                        {
                            Report(p.Span, DiagnosticSeverity.Error, DiagnosticCodes.DuplicateDeclaration, $"The parameter '{p.Name}' is listed twice.");
                        }
                        list.Add(p);
                    }
                    start = i + 1;
                }
            }

            if (report)
            {
                bool sawDefault = false;
                foreach (FunctionParameter p in list)
                {
                    if (p.HasDefault)
                    {
                        sawDefault = true;
                    }
                    else if (sawDefault)
                    {
                        Report(p.Span, DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue, $"'{p.Name}' needs a default value because an earlier parameter has one.");
                    }
                }
            }
            return list;
        }

        private FunctionParameter ParseParameter(DecodedText value, int start, int end, bool report)
        {
            string part = value.Value.Substring(start, end - start);
            if (string.IsNullOrWhiteSpace(part))
            {
                return null;
            }

            int eq = part.IndexOf('=');
            string head = eq >= 0 ? part.Substring(0, eq) : part;
            string def = eq >= 0 ? part.Substring(eq + 1).Trim() : null;
            int colon = head.IndexOf(':');
            string namePart = colon >= 0 ? head.Substring(0, colon) : head;
            string type = colon >= 0 ? head.Substring(colon + 1).Trim() : null;

            int lead = namePart.Length - namePart.TrimStart().Length;
            string name = namePart.Trim();
            int nameStart = start + lead;
            if (name.StartsWith("$", StringComparison.Ordinal))
            {
                name = name.Substring(1);
                nameStart++;
            }

            TextSpan span = value.ToSource(new TextSpan(nameStart, name.Length));
            if (name.Length == 0 || !Lexer.IsIdentStart(name[0]) || !name.All(Lexer.IsIdentChar))
            {
                if (report)
                {
                    Report(value.ToSource(new TextSpan(start, end - start)), DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue,
                        $"'{part.Trim()}' isn't a valid parameter. Write name, name: type or name = default.");
                }
                return null;
            }

            if (report && type != null && !KType.Declarable.Contains(TypeBase(type)) && !DeclaredEnums().Contains(TypeBase(type)) && !TypeNames().Contains(TypeBase(type)))
            {
                Report(value.ToSource(new TextSpan(start + colon + 1 + (head.Length - colon - 1 - head.Substring(colon + 1).TrimStart().Length), type.Length)),
                    DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue, $"Unknown type '{type}'. Use one of: {string.Join(", ", KType.Declarable)}, or declare it with <enum name=\"{type}\" values=\"...\" />.");
            }

            if (report && def != null)
            {
                ParseResult parsed = ExpressionParser.ParseExpression(def);
                if (!parsed.Success)
                {
                    Report(span, DiagnosticSeverity.Error, DiagnosticCodes.ExpressionSyntax, $"The default for '{name}' isn't a valid expression: {parsed.Errors[0].Message}");
                }
            }

            return new FunctionParameter { Name = name, Type = type, Default = def, Span = span };
        }

        /// <summary>Splits <c>a, b</c> into names and their document spans.</summary>
        private static IEnumerable<(string Name, TextSpan Span)> SplitNames(DecodedText value)
        {
            string text = value.Value;
            int i = 0;
            while (i < text.Length)
            {
                while (i < text.Length && (char.IsWhiteSpace(text[i]) || text[i] == ','))
                {
                    i++;
                }
                int start = i;
                while (i < text.Length && text[i] != ',')
                {
                    i++;
                }
                string name = text.Substring(start, i - start).TrimEnd();
                if (name.StartsWith("$", StringComparison.Ordinal) || name.StartsWith("@", StringComparison.Ordinal))
                {
                    name = name.Substring(1);
                    start++;
                }
                if (name.Length > 0)
                {
                    yield return (name, value.ToSource(new TextSpan(start, name.Length)));
                }
            }
        }

        #endregion

        #region Paths and misc

        private string ResolvePath(string relative)
        {
            // GetFullPath only rejects a path with a NUL in it.
            if (string.IsNullOrWhiteSpace(relative) || directory == null || relative.Contains('{') || relative.Contains('\0'))
            {
                return null;
            }
            string trimmed = relative.Trim().TrimStart('/');
            return System.IO.Path.GetFullPath(System.IO.Path.Combine(Kind == DocumentKind.Script ? directory : SiteRoot(), trimmed));
        }

        private static readonly string[] SiteMarkers = { "kscript.json", "_layout.html", "_components", "_layouts" };

        private string siteRoot;

        /// <summary>
        /// kscript serve resolves &lt;layout src&gt; and &lt;include src&gt; against the site's root folder, not the page's
        /// folder, so people/[id].html still uses _layout.html. The root is the nearest folder above the page with
        /// kscript.json, _layout.html, _components/ or _layouts/; without one, the page's own folder.
        /// </summary>
        private string SiteRoot()
        {
            if (siteRoot != null)
            {
                return siteRoot;
            }
            for (string dir = directory; dir != null; dir = System.IO.Path.GetDirectoryName(dir))
            {
                if (SiteMarkers.Any(m => files.FileExists(System.IO.Path.Combine(dir, m)) || files.DirectoryExists(System.IO.Path.Combine(dir, m))))
                {
                    return siteRoot = dir;
                }
            }
            return siteRoot = directory;
        }

        private void CheckPath(string relative, TextSpan span, string kind)
        {
            string full = ResolvePath(relative);
            if (full == null)
            {
                return;
            }
            if (files.FileExists(full))
            {
                result.Links.Add(new DocumentLink(span, full, kind));
            }
            else
            {
                Report(span, DiagnosticSeverity.Warning, DiagnosticCodes.InvalidAttributeValue, $"File not found: {relative}");
            }
        }

        private static bool IsYes(string value) =>
            value != null && new[] { "yes", "y", "true", "t", "1" }.Contains(value.Trim().ToLowerInvariant());

        private static TextSpan TrimmedSpan(DecodedText value)
        {
            string v = value.Value;
            int start = 0;
            int end = v.Length;
            while (start < end && char.IsWhiteSpace(v[start]))
            {
                start++;
            }
            while (end > start && char.IsWhiteSpace(v[end - 1]))
            {
                end--;
            }
            return value.ToSource(TextSpan.FromBounds(start, end));
        }

        private void Report(TextSpan span, DiagnosticSeverity severity, string code, string message, IReadOnlyList<QuickFix> fixes = null)
        {
            if (result.Diagnostics.Any(d => d.Span.Equals(span) && d.Code == code))
            {
                return;
            }
            result.Diagnostics.Add(new Diagnostic(span, severity, code, message, fixes));
        }

        #endregion
    
        /// <summary>A type without its [] and ?: Repo for Repo[]?.</summary>
        private static string TypeBase(string type)
        {
            string t = type.TrimEnd('?').TrimEnd();
            while (t.EndsWith("[]", StringComparison.Ordinal))
            {
                t = t.Substring(0, t.Length - 2).TrimEnd();
            }
            return t;
        }

        private HashSet<string> declaredEnums;

        /// <summary>The names of the &lt;enum&gt; types declared anywhere in this document.</summary>
        private HashSet<string> DeclaredEnums() => declaredEnums ??= new HashSet<string>(
            result.Markup.DescendantElements().Where(x => x.Name == "enum").Select(x => x.GetAttributeValue("name")?.Trim().TrimStart('$')).Where(n => !string.IsNullOrEmpty(n)),
            StringComparer.Ordinal);
    }
}
