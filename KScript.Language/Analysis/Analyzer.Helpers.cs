using KScript.Metadata;
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
                        attribute != null ? $"'{attribute.Name}' needs an expression." : Wording.Of($"<{element.Name}> needs a value."));
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

        private void AnalyzeTemplate(DecodedText text, Scope scope, MarkupElement element, MarkupAttribute attribute, bool allowBare, TextLiterals literals = TextLiterals.None)
        {
            if (literals == TextLiterals.All) return;
            if (text.Value.IndexOf('{') < 0 && (!allowBare || (text.Value.IndexOf('$') < 0 && text.Value.IndexOf('@') < 0)))
            {
                return;
            }

            Template template = TemplateParser.Parse(text.Value, allowBare, literals);
            if (!template.Expressions.Any())
            {
                return;
            }

            ExpressionRegion region = NewRegion(RegionKind.Template, text, element, attribute, scope, template: template);
            bool doubleQuoted = attribute != null ? attribute.Quote == '"'
                : element?.ContentSpan.Start is int start && start < source.Length && source.Text[start] == '"';
            foreach (TemplateSegment segment in template.Expressions)
            {
                if (doubleQuoted) CheckQuotedInterpolation(region, segment, report: true);
                ReportSyntaxErrors(region, segment.Expression.Errors);
                if (segment.Node != null)
                {
                    AnalyzeExpr(segment.Node, region, scope, ExprContext.Value);
                }
            }
        }

        private void CheckQuotedInterpolation(ExpressionRegion region, TemplateSegment segment, bool report = false)
        {
            if (result.Markup.Syntax != SourceSyntax.Code || segment.Kind != TemplateSegmentKind.Reference
                || !segment.Expression.Success) return;
            TextSpan span = region.Source.ToSource(segment.Span);
            if (!result.BareQuotedReferences.Add(span) || !report) return;
            Report(span, DiagnosticSeverity.Error, DiagnosticCodes.BareStringInterpolation,
                "Wrap an interpolated value in { } inside a double-quoted string.",
                new[] { new QuickFix("Wrap in interpolation braces", new TextChange(span, "{ " + source[span] + " }")) });
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
                Report(span, DiagnosticSeverity.Error, error.Code, error.Words);
            }
        }

        #endregion

        #region Names

        private Symbol NewVariable(string name, TextSpan span, MarkupElement element, SymbolKind kind, TypeShape shape)
        {
            Symbol symbol = new Symbol { Name = name, Kind = kind, DeclarationSpan = span, DeclaringElement = element, Shape = shape ?? TypeShape.Unknown };
            symbol.IsTypeDeclaration = kind == SymbolKind.Constant && element?.Name is "type" or "model" or "enum" or "error" or "job";
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
                    Report(span, DiagnosticSeverity.Error, DiagnosticCodes.AssignConst, $"${name} is a constant (const=\"true\") and can't be changed.");
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
                ? (IsCode
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

        private Symbol ReferenceFunction(string name, TextSpan span, Scope scope, Signature contract = null)
        {
            int dot = name.IndexOf('.');
            if (dot > 0)
            {
                Symbol imported = ReferenceImportedFunction(name, dot, span, scope);
                if (contract != null && imported?.Kind == SymbolKind.Function) CheckSignature(new[] { imported.PositionalSignature });
                else if (contract != null && scope.Lookup(name[..dot], span.Start)?.Package is string package
                    && catalog.GetCommand(name[(dot + 1)..]) is CallableInfo aliased && aliased.Package == package)
                    CheckSignature(aliased.Signatures);
                return imported;
            }
            Symbol fn = scope.Lookup(name, int.MaxValue, includeMethods: true);
            if (fn != null && fn.Kind == SymbolKind.Function)
            {
                result.References.Add(new SymbolReference(fn, span, false, false));
                ReportIfDeprecated(fn, span);
                if (contract != null) CheckSignature(new[] { fn.PositionalSignature });
                return fn;
            }

            if (catalog.GetCommand(name) is CallableInfo command)
            {
                ReportDeprecated(command.Deprecated, $"@{command.Name}", span, span);
                CheckSignature(command.Signatures);
                if (command.Package != null)
                {
                    usedPackages.Add(command.Package);
                }
                return null;
            }

            IEnumerable<string> candidates = result.VisibleFunctions(span.Start).Select(s => s.Name);
            string suggestion = Suggestions.Closest(name, candidates);
            Report(span, DiagnosticSeverity.Error, DiagnosticCodes.UndefinedFunction,
                $"No function named '{name}'. Declare it with {FnHint(name)}." + (suggestion != null ? $" Did you mean '{suggestion}'?" : string.Empty),
                SuggestFix(span, name, candidates, n => n));
            return null;

            void CheckSignature(IReadOnlyList<Signature> signatures)
            {
                if (contract != null && !signatures.Any(candidate => candidate.AcceptsPositionalCall(contract)))
                    Report(span, DiagnosticSeverity.Error, DiagnosticCodes.TypeMismatch,
                        $"'{name}' must accept {contract.FunctionLabel} here.");
            }
        }

        /// <summary><c>people.show</c> in a function name attribute: a function of a script (or package) imported with as="people".</summary>
        private Symbol ReferenceImportedFunction(string name, int dot, TextSpan span, Scope scope)
        {
            string aliasName = name.Substring(0, dot), member = name.Substring(dot + 1);
            TextSpan aliasSpan = new TextSpan(span.Start, dot);
            TextSpan memberSpan = TextSpan.FromBounds(span.Start + dot + 1, span.End);
            Symbol import = scope.Lookup(aliasName, aliasSpan.Start);
            if (import?.Kind != SymbolKind.Import)
            {
                IEnumerable<string> aliases = result.Symbols.Where(s => s.Kind == SymbolKind.Import).Select(s => s.Name);
                Report(aliasSpan, DiagnosticSeverity.Error, DiagnosticCodes.UndefinedFunction,
                    $"Nothing is imported as '{aliasName}'. Import it with " + (IsCode ? $"import \"...\" as {aliasName}." : $"<import path=\"...\" as=\"{aliasName}\" />."),
                    SuggestFix(aliasSpan, aliasName, aliases, n => n));
                return null;
            }
            result.References.Add(new SymbolReference(import, aliasSpan, false, false));
            if (member.Length == 0)
            {
                Report(span, DiagnosticSeverity.Error, DiagnosticCodes.UndefinedFunction, $"Name a function of the import '{aliasName}' after the dot.");
                return null;
            }
            ReferenceMember(import, member, memberSpan);
            if (import.Shape.Fields.Count > 0 && !import.Shape.Fields.ContainsKey(member))
            {
                Report(memberSpan, DiagnosticSeverity.Error, DiagnosticCodes.UndefinedFunction, $"The import '{aliasName}' has no '{member}'.",
                    SuggestFix(memberSpan, member, import.Shape.Fields.Keys, n => n));
            }
            return import.Members.TryGetValue(member, out Symbol fn) ? fn : null;
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
                    if (Quotes.IsEscape(text, i, text.Length, quote))
                    {
                        i++;
                    }
                    else if (c == quote)
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

            if (report && type != null)
            {
                int typeStart = start + colon + 1 + (head.Length - colon - 1 - head.Substring(colon + 1).TrimStart().Length);
                CheckMoneyRates(type, value.ToSource(new TextSpan(typeStart, type.Length)));
                ReferenceImportedType(type, value.ToSource(new TextSpan(typeStart, TypeInference.WithoutTypeArguments(type).TrimEnd('?').TrimEnd().Length)));
            }
            if (report && type != null && !IsBuiltInTypeName(TypeBase(type)) && !DeclaredEnums().Contains(TypeBase(type)) && !TypeNames().Contains(TypeBase(type)))
            {
                Report(value.ToSource(new TextSpan(start + colon + 1 + (head.Length - colon - 1 - head.Substring(colon + 1).TrimStart().Length), type.Length)),
                    DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue, $"Unknown type '{type}'. Use one of: {string.Join(", ", BuiltInTypes.Concat(SemanticTypeNames))}, or declare it with <enum name=\"{type}\" values=\"...\" />.");
            }

            if (report && def != null)
            {
                ParseResult parsed = ExpressionParser.ParseExpression(def);
                if (!parsed.Success)
                {
                    Report(span, DiagnosticSeverity.Error, DiagnosticCodes.ExpressionSyntax, $"The default for '{name}' isn't a valid expression: {parsed.Errors[0].Words}");
                }
            }

            return new FunctionParameter { Name = name, Type = type, Default = def, Span = span };
        }

        /// <summary>Splits <c>[a, b]</c> (or <c>a, b</c>) into names and their document spans.</summary>
        private static IEnumerable<(string Name, TextSpan Span)> SplitNames(DecodedText value)
        {
            foreach ((string listed, int at) in KScript.Metadata.NameList.Split(value.Value))
            {
                string name = listed;
                int start = at;
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

        private string siteRoot;

        private string SiteRoot() => siteRoot ??= TemplateContracts.SiteRoot(files, directory);

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

        /// <summary>
        /// Reports a problem. A message written as an interpolated string is a <see cref="Wording"/>, so a .kscript file
        /// reads it in its own terms (statements and arguments, no tags).
        /// </summary>
        private void Report(TextSpan span, DiagnosticSeverity severity, string code, Wording wording, IReadOnlyList<QuickFix> fixes = null)
        {
            string message = wording?.For(IsCode);
            // An old spelling is an error in a .kscript file, whatever the rule that found it asked for.
            if (IsCode && OldSpellings.IsOldSpelling(code)) severity = DiagnosticSeverity.Error;
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
            string t = TypeInference.WithoutTypeArguments(type.Trim()).TrimEnd('?').TrimEnd();
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
