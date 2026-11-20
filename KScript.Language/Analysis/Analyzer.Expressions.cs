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
                case LiteralNode _:
                case ErrorNode _:
                    return;

                case VariableNode v:
                {
                    TextSpan span = VariableNameSpan(v, region);
                    ReferenceVariable(v.Name, span, scope, isWrite: false, mustBeMutable: false, offset: span.Start);
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
                    CheckMethod(m.Name, region.Source.ToSource(m.NameSpan), receiver, m.ArgumentList == null ? -1 : m.Arguments.Count, pipe: false, m.Arguments, region);
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
        /// <c>$people-&gt;filter(fn($p) =&gt; $p.age gt 18)</c>, $p is one of $people's items, so $p. completes its fields.
        /// </summary>
        private static TypeShape[] LambdaParameterShapes(string method, TypeShape receiver)
        {
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
            foreach (ArgumentNode arg in args)
            {
                if (BareWord(arg) is (_, false, IdentifierNode typeNode) && ReferenceBareType(typeNode, region, scope))
                {
                    continue;
                }
                if (BareWord(arg) is var (word, _, node) && scope.Lookup(word, region.Source.ToSource(node.Span.Start))?.Kind != SymbolKind.Import)
                {
                    pendingBare.Add(arg);
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
            if ((id.Name == "_" && context == ExprContext.Argument) || ReferenceBareType(id, region, scope))
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
                hint = $" Variables start with $ ($" + id.Name + "), and text needs quotes ('" + id.Name + "').";
            }
            Report(span, DiagnosticSeverity.Error, DiagnosticCodes.BareIdentifier, $"'{id.Name}' isn't a value." + hint, fixes);
        }

        private void AnalyzeCommand(CommandCallNode c, ExpressionRegion region, Scope scope, int piped)
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
                RequirePackage(command.Package, full, $"@{command.Name}()");
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
                Report(full, DiagnosticSeverity.Error, DiagnosticCodes.RetiredCommand, $"@{c.Name} was removed in KScript 2. {retired.Replacement}");
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
                        : $"<import package=\"...\" as=\"{aliasName}\" /> or <import src=\"...\" as=\"{aliasName}\" />."),
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
                            if (!CheckNamedArguments($"@{fn.Name}()", call.Arguments, 1, SlotsOf(fn, scope), region, region.Source.ToSource(call.Span)))
                            {
                                CheckFunctionArgumentCount(fn, call.Arguments.Count + 1, region.Source.ToSource(call.Span));
                            }
                            return;
                        }
                        CheckMethod(id.Name, span, left ?? TypeShape.Unknown, call.Arguments.Count, pipe: true, call.Arguments, region);
                        return;
                    }

                    if (fn != null && fn.Kind == SymbolKind.Function)
                    {
                        result.References.Add(new SymbolReference(fn, span, false, false));
                        Report(span, DiagnosticSeverity.Error, DiagnosticCodes.UnknownCommand, $"Call functions with @: @{id.Name}(...).",
                            new[] { new QuickFix($"Change to @{id.Name}", new TextChange(new TextSpan(span.Start, 0), "@")) });
                    }
                    else if (catalog.GetCommand(id.Name) != null)
                    {
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
                    AnalyzeCommand(command, region, scope, piped: 1);
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
                        "A pipe needs a method, function, command or lambda on its right, as in |> upper() or |> @json().");
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

            List<CallableInfo> candidates = catalog.GetMethods(name, receiver.Type).ToList();
            string arrow = pipe ? "|> " : "->";
            if (candidates.Count == 0)
            {
                IEnumerable<string> names = catalog.MethodsFor(receiver.Type).Select(m => m.Name)
                    .Concat(pipe ? result.Symbols.Where(s => s.Kind == SymbolKind.Function).Select(s => s.Name) : Enumerable.Empty<string>());
                string suggestion = Suggestions.Closest(name, names);
                Report(nameSpan, DiagnosticSeverity.Error, DiagnosticCodes.UnknownMethod,
                    $"Unknown method {arrow}{name}()." + (suggestion != null ? $" Did you mean {arrow}{suggestion}()?" : string.Empty),
                    suggestion != null ? new[] { new QuickFix($"Change to {suggestion}", new TextChange(nameSpan, suggestion)) } : null);
                return;
            }

            // A declared type is held to: an int parameter has no string methods.
            List<CallableInfo> applicable = candidates.Where(m => receiver.Declared
                ? m.ReceiverType == KType.Any || m.ReceiverType == receiver.Type
                : KType.Accepts(m.ReceiverType, receiver.Type)).ToList();
            usedPackages.UnionWith(applicable.Select(m => m.Package).Where(p => p != null));
            if (applicable.Count == 0 && receiver.Declared)
            {
                Report(nameSpan, DiagnosticSeverity.Error, DiagnosticCodes.TypeMismatch,
                    $"{arrow}{name}() works on {Plural(candidates[0].ReceiverType)}, but this value is declared as {Article(receiver.Type)}.");
                return;
            }
            if (applicable.Count == 0)
            {
                Report(nameSpan, DiagnosticSeverity.Warning, DiagnosticCodes.TypeMismatch,
                    $"{arrow}{name}() works on {Plural(candidates[0].ReceiverType)}, but this value is {Article(receiver.Type)}.");
                return;
            }

            if (ChecksImports && !applicable.Any(m => m.Package == null || importedPackages.Contains(m.Package)))
            {
                RequirePackage(applicable[0].Package, nameSpan, $"{arrow}{name}()");
                return;
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

        private void CheckField(MemberNode member, ExpressionRegion region)
        {
            TypeShape target = Infer(member.Target, region);
            CheckRecordField(target, member, region);
            if ((target.Type == KType.Request || target.Type == KType.Error) && member.Name.Length > 0 && !target.Fields.ContainsKey(member.Name))
            {
                TextSpan span = region.Source.ToSource(member.NameSpan);
                Report(span, DiagnosticSeverity.Warning, DiagnosticCodes.UnknownMethod,
                    $"{(target.Type == KType.Request ? "$request" : "An error")} has no field '{member.Name}'. It has: {string.Join(", ", target.Fields.Keys)}.",
                    SuggestFix(span, member.Name, target.Fields.Keys, n => n));
            }
        }

        private static string Plural(string type) => type switch
        {
            _ => type + "s",
        };

        private static string Article(string type) =>
            type.Length > 0 && "aeiou".Contains(char.ToLowerInvariant(type[0])) ? "an " + type : "a " + type;

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
