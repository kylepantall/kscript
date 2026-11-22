using KScript.Metadata;
using System.Globalization;
using KScript.Language.Catalog;
using KScript.Syntax.Markup;
using KScript.Syntax;
using KScript.Syntax.Text;

namespace KScript.Language.Analysis
{
    public sealed partial class Analyzer
    {
        private enum ExprContext
        {
            Value,
            Argument,
        }


        #region Checking

        private void AnalyzeExpr(ExprNode node, ExpressionRegion region, Scope scope, ExprContext context)
        {
            switch (node)
            {
                case null:
                case ErrorNode _:
                    return;

                case LiteralNode literal:
                    if (literal.Template != null)
                    {
                        // "Hello $name": each part is a value of its own.
                        foreach (TemplateSegment part in literal.Template.Expressions.Where(p => p.Node != null))
                        {
                            AnalyzeExpr(part.Node, region, scope, ExprContext.Value);
                        }
                        return;
                    }
                    CheckTextIsNotATemplate(literal, region, scope);
                    return;

                case VariableNode v:
                {
                    TextSpan span = VariableNameSpan(v, region);
                    CheckTypeIsBare(ReferenceVariable(v.Name, span, scope, isWrite: false, mustBeMutable: false, offset: span.Start), span);
                    return;
                }

                case IdentifierNode id:
                    AnalyzeBareIdentifier(id, region, scope, context);
                    return;

                case CommandCallNode c:
                    AnalyzeCommand(c, region, scope, piped: 0);
                    FlushBare(c.Arguments, region, scope);
                    return;

                case MethodCallNode m:
                {
                    AnalyzeExpr(m.Target, region, scope, ExprContext.Value);
                    TypeShape receiver = Infer(m.Target, region);
                    AnalyzeArguments(m.Arguments, region, scope, LambdaParameterShapes(m.Name, receiver));
                    if (CheckRetiredConstructor(m, receiver, region))
                    {
                        FlushBare(m.Arguments, region, scope);
                        return;
                    }
                    CheckMethod(m.Name, region.Source.ToSource(m.NameSpan), receiver, m.ArgumentList == null ? -1 : m.Arguments.Count, pipe: false, m.Arguments, region);
                    if (m.Name == "sql" && receiver.Type == KType.Database)
                    {
                        CheckSqlTextIsNotFilledIn(m.Arguments, region);
                        ReferenceSqlValues(m.Arguments, region, scope);
                    }
                    FlushBare(m.Arguments, region, scope);
                    return;
                }

                case CallNode call:
                    AnalyzeCall(call, region, scope, piped: false, left: null);
                    FlushBare(call.Arguments, region, scope);
                    return;

                case MemberNode member:
                    if (member.Target is IdentifierNode alias && scope.Lookup(alias.Name, region.Source.ToSource(alias.Span.Start)) is Symbol s && s.Kind == SymbolKind.Import)
                    {
                        result.References.Add(new SymbolReference(s, region.Source.ToSource(alias.Span), false, false));
                        NoteMemberUse(s, member.Name);
                    }
                    else
                    {
                        AnalyzeExpr(member.Target, region, scope, ExprContext.Value);
                        CheckField(member, region);
                    }
                    return;

                case IndexNode index:
                    AnalyzeExpr(index.Target, region, scope, ExprContext.Value);
                    AnalyzeExpr(index.Index, region, scope, ExprContext.Value);
                    return;

                case PipeNode pipe:
                    AnalyzeExpr(pipe.Left, region, scope, ExprContext.Value);
                    AnalyzePipeTarget(pipe, region, scope);
                    return;

                case LambdaNode lambda:
                    AnalyzeLambda(lambda, region, scope);
                    return;

                case BinaryNode { Operator: "==" or "!=" or "===" or "!==" } compare
                    when HandleSide(compare.Left, compare.Right, region, scope) is CommandCallNode handle:
                {
                    // $user.handle == @kyle: next to a handle, an @name that isn't a function is a handle.
                    AnalyzeExpr(handle == compare.Right ? compare.Left : compare.Right, region, scope, ExprContext.Value);
                    CheckLiteralFits("@" + handle.Name, "handle", region.Source.ToSource(handle.Span), null, 0);
                    return;
                }

                case BinaryNode { Operator: "+" or "-" or "*" or "/" or "%" or "**" } arithmetic:
                    AnalyzeExpr(arithmetic.Left, region, scope, ExprContext.Value);
                    AnalyzeExpr(arithmetic.Right, region, scope, ExprContext.Value);
                    CheckArithmetic(arithmetic, region);
                    return;

                case BinaryNode { Operator: "<" or "<=" or ">" or ">=" } comparison:
                    AnalyzeExpr(comparison.Left, region, scope, ExprContext.Value);
                    AnalyzeExpr(comparison.Right, region, scope, ExprContext.Value);
                    CheckNativeOrdering(comparison, region);
                    return;

                default:
                    foreach (ExprNode child in node.Children)
                    {
                        AnalyzeExpr(child, region, scope, ExprContext.Value);
                    }
                    return;
            }
        }

        /// <summary>
        /// The shapes of a lambda's parameters when it is passed to a list method: in
        /// <c>$people-&gt;filter(fn(p) =&gt; $p.age &gt; 18)</c>, $p is one of $people's items, so $p. completes its fields.
        /// </summary>
        private static TypeShape[] LambdaParameterShapes(string method, TypeShape receiver)
        {
            // User->where(fn(u) => u.age >= 18): u is one of the model's records.
            if (method is "where" or "or_where" or "where_not" && DataColumns.RecordOf(receiver) is TypeShape record)
            {
                return new[] { record };
            }
            if (receiver.Type != KType.List || receiver.Element == null || receiver.Element.Type == KType.Any)
            {
                return null;
            }
            TypeShape item = receiver.Element;
            return method switch
            {
                "reduce" => new[] { TypeShape.Unknown, item },
                "sort" => new[] { item, item },
                _ => new[] { item },
            };
        }

        private void AnalyzeArguments(IEnumerable<ArgumentNode> args, ExpressionRegion region, Scope scope, TypeShape[] lambdaParameters = null)
        {
            // What a command does with its text is its own business (@sql binds { } itself), so arguments aren't warned about.
            argumentTexts.UnionWith(args.Select(a => a.Value).OfType<LiteralNode>());
            foreach (ArgumentNode arg in args)
            {
                if (BareWord(arg) is (_, false, IdentifierNode typeNode) && (ReferenceDeclaredType(typeNode, region, scope) || ReferenceBareErrorType(typeNode, region, scope)))
                {
                    continue;
                }
                if (BareWord(arg) is var (word, _, node) && scope.Lookup(word, region.Source.ToSource(node.Span.Start))?.Kind != SymbolKind.Import)
                {
                    pendingBare.Add(arg);
                    continue;
                }
                if ((arg.Value as MemberNode)?.DottedName != null)
                {
                    // posts.tags: names for a method that reads them (->with), otherwise checked as an expression.
                    pendingBare.Add(arg);
                    continue;
                }
                if (IsUnknownReference(arg.Value, scope, region))
                {
                    pendingHandles.Add(arg);
                    continue;
                }
                if (arg.Value is ListNode list && list.Items.Any(i => i is IdentifierNode))
                {
                    // [large, small]: bare words mean something only if the parameter is a list of an enum.
                    var bare = new List<IdentifierNode>();
                    foreach (ExprNode item in list.Items)
                    {
                        if (item is IdentifierNode id && id.Name != "_" && scope.Lookup(id.Name, region.Source.ToSource(id.Span.Start)) is not { Kind: SymbolKind.Import }
                            && !ReferenceDeclaredType(id, region, scope))
                        {
                            bare.Add(id);
                            continue;
                        }
                        AnalyzeExpr(item, region, scope, ExprContext.Value);
                    }
                    if (bare.Count > 0)
                    {
                        pendingBareItems[arg] = bare;
                    }
                    continue;
                }
                if (lambdaParameters != null && arg.Value is LambdaNode lambda)
                {
                    AnalyzeLambda(lambda, region, scope, lambdaParameters);
                    continue;
                }
                AnalyzeExpr(arg.Value, region, scope, ExprContext.Argument);
            }
        }

        private void AnalyzeBareIdentifier(IdentifierNode id, ExpressionRegion region, Scope scope, ExprContext context)
        {
            if ((id.Name == "_" && context == ExprContext.Argument) || ReferenceDeclaredType(id, region, scope) || ReferenceBareErrorType(id, region, scope))
            {
                return;
            }

            TextSpan span = region.Source.ToSource(id.Span);
            Symbol symbol = scope.Lookup(id.Name, span.Start);
            List<QuickFix> fixes = new List<QuickFix>();
            string hint;
            if (symbol != null && symbol.Kind != SymbolKind.Function && symbol.Kind != SymbolKind.Import)
            {
                fixes.Add(new QuickFix($"Change to ${id.Name}", new TextChange(span, "$" + id.Name)));
                hint = $" Did you mean ${id.Name}?";
            }
            else if (symbol?.Kind == SymbolKind.Function)
            {
                fixes.Add(new QuickFix($"Change to @{id.Name}", new TextChange(span, "@" + id.Name)));
                hint = $" To pass the function, write @{id.Name}.";
            }
            else
            {
                fixes.Add(new QuickFix($"Quote as '{id.Name}'", new TextChange(span, "'" + id.Name + "'")));
                if (char.IsUpper(id.Name[0]))
                {
                    // A capitalised word reads as a type, model, enum, error or job: one that isn't declared or imported here.
                    Report(span, DiagnosticSeverity.Error, DiagnosticCodes.UndefinedVariable,
                        $"No type named {id.Name} is declared or imported here. Text needs quotes ('{id.Name}').", fixes);
                    return;
                }
                hint = $" Variables start with $ ($" + id.Name + "), and text needs quotes ('" + id.Name + "').";
            }
            Report(span, DiagnosticSeverity.Error, DiagnosticCodes.BareIdentifier, $"'{id.Name}' isn't a value." + hint, fixes);
        }

        private static readonly System.Text.RegularExpressions.Regex SqlVariable = new System.Text.RegularExpressions.Regex(@"\$([A-Za-z_][A-Za-z0-9_]*)");

        /// <summary>
        /// @sql("... where email = {$email}"): the { } parts are worked out when the query runs, so the variables in them
        /// are used.
        /// </summary>
        private void ReferenceSqlValues(IReadOnlyList<ArgumentNode> args, ExpressionRegion region, Scope scope)
        {
            if (args.Count == 0 || args[0].Name != null || args[0].Value is not LiteralNode { Kind: LiteralKind.String } literal)
            {
                return;
            }
            string text = literal.Value;
            int depth = 0;
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '{' && depth == 0 && i + 1 < text.Length && text[i + 1] == '{')
                {
                    i++;
                    continue;
                }
                if (text[i] == '{' && depth++ == 0)
                {
                    start = i + 1;
                }
                else if (text[i] == '}' && depth > 0 && --depth == 0)
                {
                    foreach (System.Text.RegularExpressions.Match match in SqlVariable.Matches(text.Substring(start, i - start)))
                    {
                        TextSpan span = region.Source.ToSource(literal.Span);
                        ReferenceVariable(match.Groups[1].Value, span, scope, isWrite: false, mustBeMutable: false, offset: span.Start);
                    }
                }
            }
        }

        private void AnalyzeCommand(CommandCallNode c, ExpressionRegion region, Scope scope, int piped, TypeShape pipedShape = null)
        {
            AnalyzeArguments(c.Arguments, region, scope);
            TextSpan full = region.Source.ToSource(c.NameSpan);
            TextSpan nameSpan = TextSpan.FromBounds(full.Start + 1, full.End);
            int count = c.ArgumentList == null ? -1 : c.Arguments.Count + piped;

            Symbol fn = scope.Lookup(c.Name, nameSpan.Start);
            if (fn != null && fn.Kind == SymbolKind.Function)
            {
                result.References.Add(new SymbolReference(fn, nameSpan, false, false));
                ReportIfDeprecated(fn, nameSpan);
                if (count >= 0)
                {
                    NoteCallArguments(fn, c.Arguments, piped > 0 ? pipedShape ?? TypeShape.Unknown : null, region);
                }
                if (count >= 0 && !(fn.SourcePath != null && fn.Parameters.Count == 0 && !result.ExternalDeclarations.ContainsKey(fn))
                    && !CheckNamedArguments($"@{fn.Name}()", c.Arguments, piped, SlotsOf(fn, scope), region, full))
                {
                    CheckFunctionArgumentCount(fn, count, full);
                }
                return;
            }

            int dot = c.Name.IndexOf('.');
            if (dot > 0)
            {
                AnalyzeAliasCommand(c, dot, nameSpan, scope);
                return;
            }

            CallableInfo command = catalog.GetCommand(c.Name);
            if (command != null)
            {
                if (command.Planned)
                {
                    Report(nameSpan, DiagnosticSeverity.Error, DiagnosticCodes.PlannedFeature, $"@{command.Name}() is planned but not built yet, so it can't run.");
                }
                RequirePackage(command.Package, full, $"@{command.Name}()");
                ReportDeprecatedCall(command, $"@{command.Name}", nameSpan, count >= 0 ? c.Arguments : null, piped, region);
                if (command.KeepsBareWords)
                {
                    KeepBareWords(c.Arguments);
                }
                if (command.Package == "KScript.Data" && command.Name == "sql")
                {
                    CheckSqlTextIsNotFilledIn(c.Arguments, region);
                    ReferenceSqlValues(c.Arguments, region, scope);
                }
                if (count >= 0 && CheckNamedArguments($"@{command.Name}()", c.Arguments, piped, SlotsOf(new[] { command }), region, full))
                {
                    return;
                }
                if (count >= 0 && command.Signatures.Count > 0 && !command.Signatures.Any(s => count >= s.MinArgs && count <= s.MaxArgs))
                {
                    Report(full, DiagnosticSeverity.Error, DiagnosticCodes.ArgumentCount,
                        $"@{command.Name} doesn't take {count} argument{(count == 1 ? string.Empty : "s")}{(piped > 0 ? " (including the piped value)" : string.Empty)}. Use {string.Join(" or ", command.Labels)}.");
                }
                return;
            }

            RetiredInfo retired = catalog.GetRetiredCommand(c.Name);
            if (retired != null)
            {
                Report(full, DiagnosticSeverity.Error, DiagnosticCodes.RetiredCommand, $"@{c.Name} was removed in KScript 2. {Wording.Prose(retired.Replacement)}");
                return;
            }

            if (ForeignCommandHint(c.Name) is string hint)
            {
                Report(full, DiagnosticSeverity.Error, DiagnosticCodes.UnknownCommand, $"KScript has no @{c.Name}. {hint}");
                return;
            }

            IEnumerable<string> candidates = catalog.Commands.Select(x => x.Name).Concat(result.Symbols.Where(s => s.Kind == SymbolKind.Function).Select(s => s.Name));
            string suggestion = Suggestions.Closest(c.Name, candidates);
            Report(full, DiagnosticSeverity.Error, DiagnosticCodes.UnknownCommand,
                $"Unknown command @{c.Name}." + (suggestion != null ? $" Did you mean @{suggestion}?" : " Declare it with " + FnHint(c.Name) + "."),
                suggestion != null ? new[] { new QuickFix($"Change to @{suggestion}", new TextChange(nameSpan, suggestion)) } : null);
        }

        /// <summary><c>@math.sqrt(2)</c> or <c>@str.slugify($t)</c>: a command of a package or a function of a script imported with as="...".</summary>
        private void AnalyzeAliasCommand(CommandCallNode c, int dot, TextSpan nameSpan, Scope scope)
        {
            string aliasName = c.Name.Substring(0, dot), member = c.Name.Substring(dot + 1);
            TextSpan aliasSpan = new TextSpan(nameSpan.Start, dot);
            TextSpan memberSpan = TextSpan.FromBounds(nameSpan.Start + dot + 1, nameSpan.End);
            Symbol import = scope.Lookup(aliasName, aliasSpan.Start);
            if (import?.Kind != SymbolKind.Import)
            {
                IEnumerable<string> aliases = result.Symbols.Where(s => s.Kind == SymbolKind.Import).Select(s => s.Name);
                Report(aliasSpan, DiagnosticSeverity.Error, DiagnosticCodes.UndefinedFunction,
                    $"Nothing is imported as '{aliasName}'. Import it with " + (IsCode
                        ? $"import KScript.Name as {aliasName} or import \"...\" as {aliasName}."
                        : $"<import package=\"...\" as=\"{aliasName}\" /> or <import path=\"...\" as=\"{aliasName}\" />."),
                    SuggestFix(aliasSpan, aliasName, aliases, n => n));
                return;
            }
            result.References.Add(new SymbolReference(import, aliasSpan, false, false));
            NoteMemberUse(import, member);
            if (import.Shape.Fields.Count > 0 && !import.Shape.Fields.ContainsKey(member))
            {
                Report(memberSpan, DiagnosticSeverity.Error, DiagnosticCodes.UndefinedFunction, $"The import '{aliasName}' has no '{member}'.",
                    SuggestFix(memberSpan, member, import.Shape.Fields.Keys, n => n));
            }
        }

        private void AnalyzeCall(CallNode call, ExpressionRegion region, Scope scope, bool piped, TypeShape left)
        {
            AnalyzeArguments(call.Arguments, region, scope,
                piped && left != null && call.Callee is IdentifierNode method ? LambdaParameterShapes(method.Name, left) : null);

            switch (call.Callee)
            {
                case IdentifierNode id:
                {
                    TextSpan span = region.Source.ToSource(id.Span);
                    Symbol fn = scope.Lookup(id.Name, span.Start);
                    if (piped)
                    {
                        if (fn != null && fn.Kind == SymbolKind.Function)
                        {
                            result.References.Add(new SymbolReference(fn, span, false, false));
                            ReportIfDeprecated(fn, span);
                            NoteCallArguments(fn, call.Arguments, left ?? TypeShape.Unknown, region);
                            if (!CheckNamedArguments($"@{fn.Name}()", call.Arguments, 1, SlotsOf(fn, scope), region, region.Source.ToSource(call.Span)))
                            {
                                CheckFunctionArgumentCount(fn, call.Arguments.Count + 1, region.Source.ToSource(call.Span));
                            }
                            return;
                        }
                        if (AnalyzeTypeCall(call, id, region, scope, piped: true))
                        {
                            return;
                        }
                        CheckMethod(id.Name, span, left ?? TypeShape.Unknown, call.Arguments.Count, pipe: true, call.Arguments, region);
                        return;
                    }

                    if (AnalyzeErrorCall(call, id, region, scope) || AnalyzeJobCall(call, id, region, scope) || AnalyzeTypeCall(call, id, region, scope, piped: false))
                    {
                        return;
                    }
                    if (fn != null && fn.Kind == SymbolKind.Function)
                    {
                        result.References.Add(new SymbolReference(fn, span, false, false));
                        Report(span, DiagnosticSeverity.Error, DiagnosticCodes.UnknownCommand, $"Call functions with @: @{id.Name}(...).",
                            new[] { new QuickFix($"Change to @{id.Name}", new TextChange(new TextSpan(span.Start, 0), "@")) });
                    }
                    else if (!char.IsUpper(id.Name[0]) && catalog.GetCommand(id.Name) != null)
                    {
                        // A capitalised name is a type being called (User($data)), never a command spelt without @.
                        Report(span, DiagnosticSeverity.Error, DiagnosticCodes.UnknownCommand, $"Commands start with @: @{id.Name}(...).",
                            new[] { new QuickFix($"Change to @{id.Name}", new TextChange(new TextSpan(span.Start, 0), "@")) });
                    }
                    else if (catalog.GetMethods(id.Name).Any())
                    {
                        Report(span, DiagnosticSeverity.Error, DiagnosticCodes.UnknownMethod,
                            $"{id.Name}() is a method: call it on a value ($x->{id.Name}()) or pipe into it ($x |> {id.Name}()).");
                    }
                    else
                    {
                        Report(span, DiagnosticSeverity.Error, DiagnosticCodes.UnknownCommand, $"Unknown function '{id.Name}'. Declare it with {FnHint(id.Name)}.");
                    }
                    return;
                }

                case MemberNode member when member.Target is IdentifierNode alias:
                {
                    TextSpan aliasSpan = region.Source.ToSource(alias.Span);
                    Symbol import = scope.Lookup(alias.Name, aliasSpan.Start);
                    if (import?.Kind == SymbolKind.Import)
                    {
                        result.References.Add(new SymbolReference(import, aliasSpan, false, false));
                        NoteMemberUse(import, member.Name);
                        if (import.Shape.Fields.Count > 0 && !import.Shape.Fields.ContainsKey(member.Name))
                        {
                            TextSpan memberSpan = region.Source.ToSource(member.NameSpan);
                            Report(memberSpan, DiagnosticSeverity.Error, DiagnosticCodes.UndefinedFunction, $"The import '{alias.Name}' has no function '{member.Name}'.",
                                SuggestFix(memberSpan, member.Name, import.Shape.Fields.Keys, n => n));
                        }
                        return;
                    }
                    AnalyzeExpr(member, region, scope, ExprContext.Value);
                    return;
                }

                default:
                    // $fn(args): calling a lambda held in a variable.
                    AnalyzeExpr(call.Callee, region, scope, ExprContext.Value);
                    return;
            }
        }

        private void AnalyzePipeTarget(PipeNode pipe, ExpressionRegion region, Scope scope)
        {
            switch (pipe.Right)
            {
                case CallNode call:
                    AnalyzeCall(call, region, scope, piped: true, left: Infer(pipe.Left, region));
                    FlushBare(call.Arguments, region, scope);
                    return;

                case CommandCallNode command:
                    AnalyzeCommand(command, region, scope, piped: 1, pipedShape: Infer(pipe.Left, region));
                    FlushBare(command.Arguments, region, scope);
                    return;

                case LambdaNode lambda:
                    AnalyzeLambda(lambda, region, scope, new[] { Infer(pipe.Left, region) });
                    if (lambda.Parameters.Count != 1)
                    {
                        Report(region.Source.ToSource(lambda.Span), DiagnosticSeverity.Error, DiagnosticCodes.ArgumentCount, "A lambda that is piped into takes exactly one parameter.");
                    }
                    return;

                case VariableNode _:
                case MethodCallNode _:
                case MemberNode _:
                case IndexNode _:
                    AnalyzeExpr(pipe.Right, region, scope, ExprContext.Value);
                    return;

                case IdentifierNode id:
                    Report(region.Source.ToSource(id.Span), DiagnosticSeverity.Error, DiagnosticCodes.ExpressionSyntax, $"Add parentheses to call it: |> {id.Name}().",
                        new[] { new QuickFix($"Change to {id.Name}()", new TextChange(new TextSpan(region.Source.ToSource(id.Span.End), 0), "()")) });
                    return;

                case ErrorNode _:
                    return;

                default:
                    Report(region.Source.ToSource(pipe.Right.Span), DiagnosticSeverity.Error, DiagnosticCodes.ExpressionSyntax,
                        "A pipe needs a method, function, command or lambda on its right, as in |> upper() or |> json().");
                    return;
            }
        }

        private void AnalyzeLambda(LambdaNode lambda, ExpressionRegion region, Scope scope, TypeShape[] parameterShapes = null)
        {
            TextSpan span = region.Source.ToSource(lambda.Span);
            Scope inner = new Scope(scope, span);
            List<Symbol> declared = new List<Symbol>();
            lambdaParameters.Add(declared);
            for (int i = 0; i < lambda.Parameters.Count; i++)
            {
                ParameterNode p = lambda.Parameters[i];
                TextSpan pSpan = region.Source.ToSource(p.Span);
                if (region.Source.Value.Length > p.Span.Start && region.Source.Value[p.Span.Start] == '$')
                {
                    pSpan = TextSpan.FromBounds(pSpan.Start + 1, pSpan.End);
                }
                TypeShape shape = parameterShapes != null && i < parameterShapes.Length ? parameterShapes[i] : TypeShape.Unknown;
                Symbol symbol = NewVariable(p.Name, pSpan, region.Element, SymbolKind.Parameter, shape);
                inner.Declare(symbol, span.Start);
                declared.Add(symbol);
            }
            AnalyzeExpr(lambda.Body, region, inner, ExprContext.Value);
        }

        private void CheckMethod(string name, TextSpan nameSpan, TypeShape receiver, int count, bool pipe, IReadOnlyList<ArgumentNode> args = null, ExpressionRegion region = null)
        {
            if (string.IsNullOrEmpty(name) || name == "call")
            {
                return;
            }

            string arrow = pipe ? "|> " : "->";
            // An extend block's method (this file's, an import's or the project's) comes first, as when the script runs.
            Symbol own = result.ExtensionMethod(receiver, name);
            if (own != null && own.SourcePath != null)
            {
                result.References.Add(new SymbolReference(own, nameSpan, false, false));
            }
            if (own != null && own.SourcePath != null && count >= 0 && args != null)
            {
                int needed = own.Parameters.Count(p => !p.HasDefault);
                if (args.Any(a => a.Name != null) || (count >= needed && count <= own.Parameters.Count))
                {
                    return;
                }
                Report(nameSpan, DiagnosticSeverity.Error, DiagnosticCodes.ArgumentCount,
                    $"{arrow}{name}() takes {(needed == own.Parameters.Count ? needed.ToString(CultureInfo.InvariantCulture) : $"{needed} to {own.Parameters.Count}")} argument{(own.Parameters.Count == 1 ? string.Empty : "s")}, but {count} {(count == 1 ? "was" : "were")} given.");
                return;
            }
            if (own != null && own.SourcePath != null)
            {
                return;
            }
            if (own != null || receiver.Methods.TryGetValue(name, out own) || TypeInference.ScopeOf(receiver, name) is Symbol scope && (own = scope) != null)
            {
                // A method the record's <type> declares with <fn>, or a model's scope (which uses the fn it was made from).
                result.References.Add(new SymbolReference(scopeDeclarations.GetValueOrDefault(own) ?? own, nameSpan, false, false));
                if (count >= 0 && args != null && CheckNamedArguments($"{arrow}{name}()", args, 0, SlotsOf(own, result.RootScope), region, nameSpan))
                {
                    return;
                }
                int required = own.Parameters.Count(p => !p.HasDefault);
                if (count >= 0 && (count < required || count > own.Parameters.Count))
                {
                    Report(nameSpan, DiagnosticSeverity.Error, DiagnosticCodes.ArgumentCount,
                        $"{arrow}{name}() takes {(required == own.Parameters.Count ? required.ToString(CultureInfo.InvariantCulture) : $"{required} to {own.Parameters.Count}")} argument{(own.Parameters.Count == 1 ? string.Empty : "s")}, but {count} {(count == 1 ? "was" : "were")} given.");
                }
                return;
            }

            string type = (types ??= new TypeInference(catalog, result)).ReceiverType(name, receiver);
            List<CallableInfo> candidates = catalog.GetMethods(name, type).ToList();
            if (!pipe && (candidates.Count == 0 || (receiver.Type != KType.Any && !candidates.Any(m => m.ReceiverType == receiver.Type || m.ReceiverType == KType.Any))) && FieldOrRetired(name, nameSpan, receiver))
            {
                return;
            }
            // A record or query of a model this file can't see (declared in another file): its relations and scopes aren't known here.
            // A value of no known type may be such a record too, once KScript.Data is imported: $user->posts() in fn(user).
            if (candidates.Count == 0 && (receiver.Type is KType.Record or KType.Query && receiver.Instance == null && receiver.Name == null
                || receiver.Type == KType.Any && !receiver.Declared && importedPackages.Contains("KScript.Data")))
            {
                return;
            }
            if (candidates.Count == 0)
            {
                IEnumerable<string> names = catalog.MethodsFor(receiver.Type).Concat(receiver.Kind != null ? catalog.MethodsFor(receiver.Kind) : Enumerable.Empty<CallableInfo>()).Select(m => m.Name).Concat(receiver.Methods.Keys)
                    .Concat(pipe ? result.Symbols.Where(s => s.Kind == SymbolKind.Function).Select(s => s.Name) : Enumerable.Empty<string>());
                string suggestion = Suggestions.Closest(name, names);
                Report(nameSpan, DiagnosticSeverity.Error, DiagnosticCodes.UnknownMethod,
                    $"Unknown method {arrow}{name}()." + (suggestion != null ? $" Did you mean {arrow}{suggestion}()?" : string.Empty),
                    suggestion != null ? new[] { new QuickFix($"Change to {suggestion}", new TextChange(nameSpan, suggestion)) } : null);
                return;
            }

            // A declared type is held to: an int parameter has no string methods.
            // Text with a meaning of its own (an email, a uuid) keeps only the string methods that make sense for it.
            List<CallableInfo> applicable = candidates.Where(m => receiver.Declared
                ? m.ReceiverType == KType.Any || m.ReceiverType == type || (m.ReceiverType == KType.String && IsSemanticText(type) && KType.AcceptsMethod(m.ReceiverType, m.Name, type))
                : KType.AcceptsMethod(m.ReceiverType, m.Name, type)).ToList();
            usedPackages.UnionWith(applicable.Select(m => m.Package).Where(p => p != null));
            if (IsSemanticText(receiver.Type) && candidates.FirstOrDefault(m => m.ReceiverType == KType.String) is CallableInfo text)
            {
                // ->to_number() is on numbers and strings: a uuid is told about the string one, since it is text.
                candidates.Remove(text);
                candidates.Insert(0, text);
            }
            if (applicable.Count == 0 && receiver.Declared)
            {
                Report(nameSpan, DiagnosticSeverity.Error, DiagnosticCodes.TypeMismatch,
                    $"{arrow}{name}() works on {Plural(candidates[0].ReceiverType)}, but this value is declared as {Article(type)}." + ToStringHint(candidates[0], type, arrow, name));
                return;
            }
            if (applicable.Count == 0)
            {
                Report(nameSpan, DiagnosticSeverity.Warning, DiagnosticCodes.TypeMismatch,
                    $"{arrow}{name}() works on {Plural(candidates[0].ReceiverType)}, but this value is {Article(type)}." + ToStringHint(candidates[0], type, arrow, name));
                return;
            }

            if (applicable.Any(m => m.KeepsBareWords) && args != null)
            {
                KeepBareWords(args);
            }
            if (applicable.Any(m => m.Package == "KScript.Data"))
            {
                CheckColumns(name, receiver, args, region);
            }
            if (ChecksImports && !applicable.Any(m => m.Package == null || importedPackages.Contains(m.Package)))
            {
                RequirePackage(applicable[0].Package, nameSpan, $"{arrow}{name}()");
                return;
            }

            if (applicable.Count == 1 || applicable.All(m => m.Deprecated != null))
            {
                ReportDeprecatedCall(applicable[0], $"->{name}", nameSpan, count >= 0 ? args : null, 0, region);
            }
            if (count >= 0 && args != null && CheckNamedArguments($"{arrow}{name}()", args, 0, SlotsOf(applicable), region, nameSpan))
            {
                return;
            }
            if (count >= 0 && !applicable.Any(m => count >= m.MinArgs && count <= m.MaxArgs))
            {
                Report(nameSpan, DiagnosticSeverity.Error, DiagnosticCodes.ArgumentCount,
                    $"{arrow}{name}() doesn't take {count} argument{(count == 1 ? string.Empty : "s")}. Use {string.Join(" or ", applicable.SelectMany(m => m.Labels).Distinct())}.");
            }
        }

        /// <summary>
        /// <c>->status()</c> where status is a field (<c>.status</c>), or a method that was renamed (<c>->put()</c> is
        /// <c>->set()</c>): reports it with a fix and returns true.
        /// </summary>
        private bool FieldOrRetired(string name, TextSpan nameSpan, TypeShape receiver)
        {
            bool unknown = receiver.Type == KType.Any;
            // A renamed method of this very type comes first: storage's ->path(name) is ->path_of(name), though .path is a field.
            RetiredInfo retired = catalog.GetRetiredMethod(receiver.Type, name) ?? catalog.GetRetiredMethod(receiver.Kind, name);
            ValueFieldInfo field = retired != null ? null
                : catalog.GetField(receiver.Type, name) ?? catalog.GetField(receiver.Kind, name) ?? (unknown ? catalog.Fields.FirstOrDefault(f => f.Name == name) : null);
            if (field != null)
            {
                Report(nameSpan, DiagnosticSeverity.Error, DiagnosticCodes.UnknownMethod,
                    $"{name} is a field of {Article(field.Receiver)}, not a method. Read it with a dot and no brackets: .{name}",
                    CallToField(nameSpan, name) is TextChange change ? new[] { new QuickFix($"Change to .{name}", change) } : null);
                return true;
            }
            retired ??= unknown ? catalog.GetRetiredMethod(name) : null;
            if (retired == null)
            {
                return false;
            }
            QuickFix fix = retired.Rename == null ? null
                : retired.Rename.StartsWith('.') ? (CallToField(nameSpan, retired.Rename.Substring(1)) is TextChange toField ? new QuickFix($"Change to {retired.Rename}", toField) : null)
                : new QuickFix($"Change to {retired.Rename}", new TextChange(nameSpan, retired.Rename));
            Report(nameSpan, DiagnosticSeverity.Error, DiagnosticCodes.UnknownMethod, $"->{name}() is no longer part of KScript. {Wording.Prose(retired.Replacement)}", fix != null ? new[] { fix } : null);
            return true;
        }

        /// <summary>The edit that turns <c>->name()</c> at <paramref name="nameSpan"/> into <c>.field</c>, or null when it has arguments.</summary>
        private TextChange CallToField(TextSpan nameSpan, string field)
        {
            string text = result.Source.Text;
            int start = nameSpan.Start;
            while (start > 0 && char.IsWhiteSpace(text[start - 1])) start--;
            if (start < 2 || text[start - 2] != '-' || text[start - 1] != '>') return null;
            start -= 2;
            int end = nameSpan.End;
            while (end < text.Length && char.IsWhiteSpace(text[end])) end++;
            if (end >= text.Length || text[end] != '(') return null;
            end++;
            while (end < text.Length && char.IsWhiteSpace(text[end])) end++;
            if (end >= text.Length || text[end] != ')') return null;
            return new TextChange(TextSpan.FromBounds(start, end + 1), "." + field);
        }

        private void CheckField(MemberNode member, ExpressionRegion region)
        {
            TypeShape target = Infer(member.Target, region);
            CheckRecordField(target, member, region);
            CheckMapField(target, member, region);
            if ((target.Type == KType.Request || target.Type == KType.Error) && member.Name.Length > 0 && !target.Fields.ContainsKey(member.Name))
            {
                TextSpan span = region.Source.ToSource(member.NameSpan);
                Report(span, DiagnosticSeverity.Warning, DiagnosticCodes.UnknownMethod,
                    $"{(target.Type == KType.Request ? "$request" : "An error")} has no field '{member.Name}'. It has: {string.Join(", ", target.Fields.Keys)}.",
                    SuggestFix(span, member.Name, target.Fields.Keys, n => n));
                return;
            }
            // $response.json: a method of the value, read as a field.
            if (member.Name.Length > 0 && !target.Fields.ContainsKey(member.Name) && catalog.FieldsOf(target.Type) is { Count: > 0 } builtIn && !builtIn.ContainsKey(member.Name)
                && target.Type != KType.Type && catalog.GetMethods(member.Name, target.Type).Any(m => m.ReceiverType == target.Type))
            {
                TextSpan at = region.Source.ToSource(TextSpan.FromBounds(member.NameSpan.Start - 1, member.NameSpan.End));
                Report(at, DiagnosticSeverity.Warning, DiagnosticCodes.UnknownField,
                    $"{member.Name} is a method of {Article(target.Type)}, not a field. Call it with ->{member.Name}().",
                    member.NullSafe ? null : new[] { new QuickFix($"Change to ->{member.Name}()", new TextChange(at, "->" + member.Name + "()")) });
            }
        }

        /// <summary>The <c>@name</c> side of a comparison with a handle, when nothing is called name; otherwise null.</summary>
        private CommandCallNode HandleSide(ExprNode left, ExprNode right, ExpressionRegion region, Scope scope)
        {
            if (IsUnknownReference(right, scope, region) && Infer(left, region).Type == KType.Handle)
            {
                return (CommandCallNode)right;
            }
            if (IsUnknownReference(left, scope, region) && Infer(right, region).Type == KType.Handle)
            {
                return (CommandCallNode)left;
            }
            return null;
        }

        /// <summary>
        /// For a string method on a value that isn't a string (a uuid, a date, a number): how to call it on the text anyway.
        /// <c>->json()</c> reads JSON text, so a value that isn't text is pointed at <c>->to_json()</c> instead.
        /// </summary>
        private static string ToStringHint(CallableInfo method, string type, string arrow, string name)
        {
            if (method.ReceiverType != KType.String || !(IsSemanticText(type) || HasTextForm(type)))
            {
                return string.Empty;
            }
            return string.Equals(name, "json", StringComparison.OrdinalIgnoreCase) && !IsSemanticText(type)
                ? " To write it as JSON text, use ->to_json()."
                : $" Use ->to_string(){arrow}{name}() to work on its text.";
        }

        /// <summary>A number, bool, date or file isn't text, but prints as text with <c>->to_string()</c>.</summary>
        private static bool HasTextForm(string type) => type == KType.Number || type == KType.Bool || type == KType.Date || type == KType.File;

        private static bool IsSemanticText(string type) => KType.IsNativeText(type);

        private static string Plural(string type) => type switch
        {
            _ => type + "s",
        };

        private static string Article(string type) =>
            NativeTypes.Get(type)?.WithArticle ?? (type.Length > 0 && "aeiou".Contains(char.ToLowerInvariant(type[0])) ? "an " + type : "a " + type);

        private static TextSpan VariableNameSpan(VariableNode v, ExpressionRegion region)
        {
            TextSpan span = region.Source.ToSource(v.Span);
            return TextSpan.FromBounds(Math.Min(span.Start + 1, span.End), span.End);
        }

        #endregion

        #region Types

        internal TypeShape Infer(ExprNode node, ExpressionRegion region) => (types ??= new TypeInference(catalog, result)).Infer(node, region);

        private TypeInference types;

        #endregion
    }
}
