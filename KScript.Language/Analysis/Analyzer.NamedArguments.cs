using KScript.Metadata;
using KScript.Language.Catalog;
using KScript.Runtime;
using KScript.Syntax;
using KScript.Syntax.Text;

namespace KScript.Language.Analysis
{
    /// <summary>
    /// Checks call arguments the way the runtime binds them: positional arguments fill parameters in order, each
    /// <c>name: value</c> fills the parameter of that name, positional arguments after named ones fill the
    /// parameters still empty, a bare word is a value of an enum parameter (<c>@known_folder(downloads)</c>) or a
    /// yes/no flag (<c>recursive</c>, <c>not recursive</c>).
    /// </summary>
    public sealed partial class Analyzer
    {
        /// <remarks><paramref name="RawText"/>: how a bare word is read for the parameter's type, as text ("word") or a handle ("handle").</remarks>
        internal readonly record struct ParameterSlot(string Name, bool Required, bool Variadic, bool Flag = false, IReadOnlyList<string> Values = null, string TypeName = null,
            IReadOnlyList<string> ItemValues = null, string RawText = null, string Type = null, bool AcceptsModel = false, bool OptionalNativeCallback = false,
            bool ScriptParameter = false, TypeArgumentConstraint TypeConstraint = TypeArgumentConstraint.None, Signature FunctionSignature = null, bool RequiresRouteName = false)
        {
            public bool IsEnum => Values != null && Values.Count > 0;

            /// <summary>A list of an enum (<c>sizes: Size[]</c>): its items can be written bare, <c>[large, small]</c>.</summary>
            public bool IsEnumList => ItemValues != null && ItemValues.Count > 0;

            public string Match(string word)
            {
                if (IsEnum)
                {
                    return Values.FirstOrDefault(v => string.Equals(v, word.Trim(), StringComparison.OrdinalIgnoreCase));
                }
                return RawText != null && (RawText == "handle" || !word.StartsWith('@')) ? word : null;
            }

            public string MatchItem(string word) => IsEnumList ? ItemValues.FirstOrDefault(v => string.Equals(v, word.Trim(), StringComparison.OrdinalIgnoreCase)) : null;

        }

        /// <summary>Bare words inside an array argument (<c>[large, small]</c>), waiting for their call to say whether the parameter is a list of an enum.</summary>
        private readonly Dictionary<ArgumentNode, List<IdentifierNode>> pendingBareItems = new Dictionary<ArgumentNode, List<IdentifierNode>>();

        /// <summary>Bare-word arguments (<c>downloads</c>, <c>not recursive</c>) waiting for their call to say what they are.</summary>
        private readonly HashSet<ArgumentNode> pendingBare = new HashSet<ArgumentNode>();

        /// <summary>Words joined by - . + or / (<c>dd-MM-yyyy</c>, <c>1.4.2</c>) waiting for their call: text for a parameter whose type reads it.</summary>
        private readonly HashSet<ArgumentNode> pendingRaw = new HashSet<ArgumentNode>();

        private static List<List<ParameterSlot>> SlotsOf(IEnumerable<CallableInfo> callables, bool knownReceiver = true) =>
            callables.SelectMany(c => c.Signatures.Select(s => s.Parameters.Select(p => new ParameterSlot(p.Name, !p.Optional && !p.Variadic, p.Variadic, p.IsFlag, p.Values.Count > 0 ? p.Values : null,
                    RawText: NativeTypes.Get(p.Type) is { } native && native.Name != "port" ? native.RawKind : p.Type == "word" ? "word" : null,
                    Type: p.Type, AcceptsModel: knownReceiver && p.AcceptsModel, OptionalNativeCallback: p.Optional && p.Type == KType.Fn,
                    TypeConstraint: p.TypeConstraint, FunctionSignature: p.FunctionSignature,
                    RequiresRouteName: c is { Package: "KScript.Http", Name: "route" } && p.Name == "name")).ToList()))
                .ToList();

        private List<List<ParameterSlot>> SlotsOf(Symbol fn, Scope scope) =>
            new List<List<ParameterSlot>>
            {
                fn.Parameters.Select(p =>
                {
                    Symbol type = p.Type != null && !KType.Declarable.Contains(p.Type) && p.Type != "number" && p.Type != "date" && p.Type != "any"
                        ? scope?.Lookup(p.Type, int.MaxValue) : null;
                    string raw = type?.EnumValues == null ? RawTextKindOf(p.Type, scope, int.MaxValue) : null;
                    string itemType = p.Type != null && p.Type.EndsWith("[]", StringComparison.Ordinal) ? p.Type.Substring(0, p.Type.Length - 2).Trim() : null;
                    Symbol item = itemType != null && !KType.Declarable.Contains(itemType) ? scope?.Lookup(itemType, int.MaxValue) : null;
                    return new ParameterSlot(p.Name, !p.HasDefault, false, p.Type == KType.Bool, type?.EnumValues, type?.EnumValues != null ? type.Name : item?.EnumValues != null ? item.Name : null,
                        item?.EnumValues, raw, p.Type, ScriptParameter: true);
                }).ToList(),
            };

        /// <summary>The word of a bare-word argument, and whether it was negated, or null.</summary>
        internal static (string Word, bool Negated, IdentifierNode Node)? BareWord(ArgumentNode a)
        {
            ExprNode v = a.Value;
            bool negated = false;
            if (v is UnaryNode { Operator: "not", Operand: IdentifierNode inner })
            {
                v = inner;
                negated = true;
            }
            return v is IdentifierNode id && id.Name != "_" ? (id.Name, negated, id) : null;
        }

        /// <summary>Reports the bare words of a call that nothing claimed, as before: they aren't values.</summary>
        /// <summary>Arguments such as <c>@kyle</c> that name no function: a handle, if the parameter takes one.</summary>
        private readonly HashSet<ArgumentNode> pendingHandles = new HashSet<ArgumentNode>();

        /// <summary>Each model scope's symbol, and the fn symbol it was made from.</summary>
        private readonly Dictionary<Symbol, Symbol> scopeDeclarations = new Dictionary<Symbol, Symbol>();

        /// <summary>True for <c>@name</c> (no parentheses) where nothing is called name: it can only be a handle.</summary>
        private bool IsUnknownReference(ExprNode node, Scope scope, ExpressionRegion region) =>
            node is CommandCallNode { ArgumentList: null } c && !c.Name.Contains('.')
            && catalog.GetCommand(c.Name) == null && catalog.GetRetiredCommand(c.Name) == null
            && scope.Lookup(c.Name, region.Source.ToSource(c.Span.Start))?.Kind != SymbolKind.Function;

        private void CheckTypeConstraint(ExprNode value, ParameterSlot slot, ExpressionRegion region)
        {
            if (!slot.Required && value is LiteralNode { Kind: LiteralKind.Null }) return;
            TypeShape supplied = Infer(value, region);
            TypeShape shape = supplied.Instance;
            if (slot.TypeConstraint != TypeArgumentConstraint.DeclaredRecord && value is LiteralNode { Kind: LiteralKind.String } literal)
                shape = new TypeInference(catalog, result).TypeNamed(literal.Value, region.Source.ToSource(value.Span.Start));
            if (supplied.Type == KType.Any && shape == null) return;
            bool accepts = slot.TypeConstraint switch
            {
                TypeArgumentConstraint.DeclaredRecord => shape is { Type: KType.Map, Name: not null, Kind: null },
                TypeArgumentConstraint.Array => shape?.Type == KType.List,
                TypeArgumentConstraint.Mappable => shape?.Type is KType.List or KType.Map,
                TypeArgumentConstraint.Iterable => shape?.Type is (KType.List or KType.Map or KType.Number or KType.String)
                    || (shape != null && NativeTypes.Get(shape.Type)?.Iterates != null),
                _ => true,
            };
            if (!accepts)
                Report(region.Source.ToSource(value.Span), DiagnosticSeverity.Error, DiagnosticCodes.TypeMismatch,
                    $"The '{slot.Name}' argument requires a type with the {slot.TypeConstraint} constraint.");
        }

        private void CheckRouteName(ExprNode value, ExpressionRegion region)
        {
            TypeShape actual = Infer(value, region);
            if (actual.Type is not (KType.Any or KType.String)
                || value is LiteralNode { Kind: LiteralKind.String, Template: null } name && string.IsNullOrWhiteSpace(name.Value))
            {
                Report(region.Source.ToSource(value.Span), DiagnosticSeverity.Error, DiagnosticCodes.TypeMismatch,
                    "@route() needs a nonempty route name as text, for example 'people.show'.");
            }
        }

        /// <summary>An argument that reached its parameter: a waiting <c>@kyle</c> is a handle there, and a literal is checked against the type.</summary>
        private void ClaimTyped(ArgumentNode a, ParameterSlot slot, ExpressionRegion region)
        {
            if (slot.TypeConstraint != TypeArgumentConstraint.None && a.Value != null)
                CheckTypeConstraint(a.Value, slot, region);

            if (slot.RequiresRouteName && a.Value != null)
            {
                CheckRouteName(a.Value, region);
                return;
            }
            if (slot.Type is "fn" or "fn?" && a.Value != null)
            {
                if (slot.FunctionSignature is Signature contract)
                {
                    Signature actualSignature = a.Value switch
                    {
                        LambdaNode lambda => new Signature(lambda.Parameters.Select(p => new ParameterInfo(p.Name)).ToArray()),
                        CommandCallNode { ArgumentList: null } command => region.Scope.Lookup(command.Name, region.Source.ToSource(command.Span.Start))?.PositionalSignature,
                        _ => null,
                    };
                    if (actualSignature != null && !actualSignature.AcceptsPositionalCall(contract))
                        Report(region.Source.ToSource(a.Value.Span), DiagnosticSeverity.Error, DiagnosticCodes.TypeMismatch,
                            $"The '{slot.Name}' argument must accept {contract.FunctionLabel}.");
                }
                string actual = Infer(a.Value, region).Type;
                bool acceptsNull = slot.Type == "fn?" || slot.OptionalNativeCallback;
                if (actual is not (KType.Any or KType.Fn) && !(actual == KType.Null && acceptsNull))
                    Report(region.Source.ToSource(a.Value.Span), DiagnosticSeverity.Error, DiagnosticCodes.TypeMismatch,
                        $"The '{slot.Name}' argument needs a function, but this value is {actual}. Pass a function reference such as @handler or an inline fn.");
                return;
            }
            if (slot.AcceptsModel)
            {
                CheckModelArgument(a.Value, region, slot.Type == ParameterInfo.ModelOrString);
                return;
            }
            if (slot.ScriptParameter && a.Value is LiteralNode { Kind: LiteralKind.Null }
                && TypeRef.Parse(slot.Type) is { Optional: false } parameterType
                && parameterType is not { IsSimple: true, IsBuiltIn: true, Name: KType.Any or KType.String })
            {
                // Plain string coerces null to empty text. A refinement, record or list goes through shape validation.
                Report(region.Source.ToSource(a.Value.Span), DiagnosticSeverity.Error, DiagnosticCodes.TypeMismatch,
                    $"The '{slot.Name}' argument needs {slot.Type}, but this value is null.");
                return;
            }
            if (slot.Type is "number" or "int" or "float")
            {
                CheckNumberArgument(a, slot, region);
                return;
            }
            if (pendingRaw.Contains(a))
            {
                if (slot.RawText == null || slot.RawText == "handle")
                {
                    return;
                }
                pendingRaw.Remove(a);
                if (slot.Type != null)
                {
                    string written = region.Source.Value.Substring(a.Value.Span.Start, a.Value.Span.Length).Trim();
                    CheckLiteralFits(written, slot.Type, region.Source.ToSource(a.Value.Span), null, 0);
                }
                return;
            }
            if (pendingHandles.Contains(a))
            {
                if (slot.RawText != "handle")
                {
                    return;
                }
                pendingHandles.Remove(a);
                CheckLiteralFits("@" + ((CommandCallNode)a.Value).Name, slot.Type, region.Source.ToSource(a.Value.Span), null, 0);
                return;
            }
            if (slot.Type == null || slot.IsEnum)
            {
                return;
            }
            // KS6018: timeout: 30 and ttl: '10m' are written 30s and 10m.
            if (slot.Type == DurationType && a.Value is LiteralNode { Kind: LiteralKind.Number or LiteralKind.String, Template: null } time
                && DurationAsWritten(time.Value) is string duration)
            {
                ReportDurationUnit(region.Source.ToSource(time.Span), slot.Name, duration);
                return;
            }
            if (BareWord(a) is (string word, false, IdentifierNode node) && slot.RawText != null)
            {
                CheckLiteralFits(word, slot.Type, region.Source.ToSource(node.Span), null, 0);
            }
            else
            {
                CheckTypedLiteral(a.Value, slot.Type, region.Source, null, 0);
            }
        }

        /// <summary>
        /// For calls that read bare words themselves, such as column names in KScript.Data (<c>->pluck(email)</c>,
        /// <c>->where(role: admin)</c>): the words are values, so they aren't reported as unknown names.
        /// </summary>
        private void KeepBareWords(IEnumerable<ArgumentNode> args)
        {
            foreach (ArgumentNode a in args)
            {
                pendingBare.Remove(a);
                pendingBareItems.Remove(a);
            }
        }

        private void FlushBare(IEnumerable<ArgumentNode> args, ExpressionRegion region, Scope scope)
        {
            foreach (ArgumentNode a in args)
            {
                if (pendingHandles.Remove(a))
                {
                    AnalyzeExpr(a.Value, region, scope, ExprContext.Argument);
                    continue;
                }
                if (pendingRaw.Remove(a))
                {
                    AnalyzeExpr(a.Value, region, scope, ExprContext.Argument);
                    continue;
                }
                if (pendingBareItems.Remove(a, out List<IdentifierNode> items))
                {
                    foreach (IdentifierNode item in items)
                    {
                        AnalyzeBareIdentifier(item, region, scope, ExprContext.Value);
                    }
                }
                if (!pendingBare.Remove(a))
                {
                    continue;
                }
                if (BareWord(a) is var (_, negated, node))
                {
                    AnalyzeBareIdentifier(node, region, scope, ExprContext.Argument, allowPattern: !negated);
                }
                else
                {
                    // posts.tags that no method claimed.
                    AnalyzeExpr(a.Value, region, scope, ExprContext.Argument);
                }
            }
        }

        /// <summary>
        /// Checks a call's arguments against its signatures. Returns true when it checked the argument count itself
        /// (the call names arguments or uses bare words), so the caller skips its plain count check.
        /// </summary>
        private bool CheckNamedArguments(string display, IReadOnlyList<ArgumentNode> args, int piped, List<List<ParameterSlot>> signatures, ExpressionRegion region, TextSpan callSpan)
        {
            // Every command, method and function has at least one signature.
            bool special = args.Any(a => a.Name != null || BareWord(a) != null || pendingRaw.Contains(a));

            static int IndexOf(List<ParameterSlot> s, string name) => s.FindIndex(p => !p.Variadic && p.Name == name);
            // Of the signatures that have every name given, one that takes as many arguments as were given (->with() on
            // a logger takes one, on a query any number).
            int given = piped + args.Count(a => a.Name == null);
            List<ParameterSlot> signature = signatures.Where(s => args.Where(a => a.Name != null).All(a => IndexOf(s, a.Name) >= 0))
                .OrderBy(s => s.Any(p => p.Variadic) || given <= s.Count ? 0 : 1).FirstOrDefault()
                ?? signatures.FirstOrDefault(s => s.Any(p => p.Variadic));

            if (signature == null)
            {
                List<string> known = signatures.SelectMany(s => s).Where(p => !p.Variadic).Select(p => p.Name).Distinct().ToList();
                foreach (ArgumentNode a in args.Where(a => a.Name != null && IndexOf(signatures[0], a.Name) < 0))
                {
                    TextSpan span = region.Source.ToSource(a.NameSpan);
                    string suggestion = Suggestions.Closest(a.Name, known);
                    Report(span, DiagnosticSeverity.Error, DiagnosticCodes.UnknownParameter,
                        $"{display} has no parameter named '{a.Name}'." + (suggestion != null ? $" Did you mean '{suggestion}'?" : string.Empty)
                        + (known.Count > 0 ? $" Its parameters are: {string.Join(", ", known)}." : $" {display} takes no named arguments."),
                        suggestion != null ? new[] { new QuickFix($"Change to {suggestion}", new TextChange(span, suggestion)) } : null);
                }
                return true;
            }

            ParameterSlot? SlotAt(int index) => index < signature.Count ? signature[index] : signature.Any(p => p.Variadic) ? signature.Last(p => p.Variadic) : null;

            var filled = new Dictionary<int, ArgumentNode>();
            for (int i = 0; i < Math.Min(piped, signature.Count); i++)
            {
                filled[i] = null;
            }
            int positional = piped;
            bool seenNamed = false;
            var late = new List<ArgumentNode>();
            var flagged = new HashSet<string>(StringComparer.Ordinal);

            void Fill(int index, ArgumentNode a, string name)
            {
                if (filled.TryGetValue(index, out ArgumentNode previous))
                {
                    TextSpan at = region.Source.ToSource(a.Name != null ? a.NameSpan : a.Span);
                    Report(at, DiagnosticSeverity.Error, DiagnosticCodes.NamedArgument,
                        previous == null && piped > 0 && index == 0 ? $"'{name}' is already given as argument 1 (the piped value)."
                        : previous != null && previous.Name == null && BareWord(previous) == null ? $"'{name}' is already given as argument {index + 1}."
                        : $"'{name}' is given twice.");
                    return;
                }
                filled[index] = a;
                if (a != null && index < signature.Count)
                {
                    ResolveEnumItems(display, a, signature[index], region);
                }
            }

            foreach (ArgumentNode a in args)
            {
                var bare = BareWord(a);
                if (a.Name != null)
                {
                    seenNamed = true;
                    int index = IndexOf(signature, a.Name);
                    if (index < 0)
                    {
                        continue; // Extra names on a variadic signature.
                    }
                    if (bare is var (word, negated, _) && !negated && pendingBare.Contains(a))
                    {
                        ResolveEnumWord(display, a, signature[index], word, region);
                    }
                    else
                    {
                        CheckEnumLiteral(display, a, signature[index], region);
                    }
                    ClaimTyped(a, signature[index], region);
                    Fill(index, a, a.Name);
                    continue;
                }

                if (bare is var (w, neg, _) && pendingBare.Contains(a))
                {
                    ParameterSlot? slot = SlotAt(positional);
                    if (!seenNamed && !neg && slot?.Match(w) != null)
                    {
                        pendingBare.Remove(a);
                        ClaimTyped(a, slot.Value, region);
                        Fill(positional, a, slot.Value.Name);
                        positional++;
                        continue;
                    }
                    int flag = IndexOf(signature, w);
                    if (flag >= 0)
                    {
                        pendingBare.Remove(a);
                        ParameterSlot target = signature[flag];
                        if (!target.Flag && (target.IsEnum || target.TypeName != null || target.RequiresRouteName))
                        {
                            Report(region.Source.ToSource(a.Span), DiagnosticSeverity.Error, DiagnosticCodes.NamedArgument,
                                $"'{w}' isn't a true/false parameter of {display}, so it can't be written as a flag. Give it a value: {w}: ...");
                        }
                        flagged.Add(w);
                        Fill(flag, a, w);
                        continue;
                    }
                    if (seenNamed)
                    {
                        late.Add(a);
                        continue;
                    }
                    if (slot?.IsEnum == true)
                    {
                        pendingBare.Remove(a);
                        ResolveEnumWord(display, a, slot.Value, w, region);
                        Fill(positional, a, slot.Value.Name);
                        positional++;
                        continue;
                    }
                    if (!neg && a.Value is IdentifierNode literal && TryPatternLiteral(literal, region))
                    {
                        pendingBare.Remove(a);
                        if (slot is { AcceptsModel: true } modelSlot)
                        {
                            ClaimTyped(a, modelSlot, region);
                        }
                        filled[positional++] = a;
                        continue;
                    }
                    string close = Suggestions.Closest(w, signature.Where(p => !p.Variadic).Select(p => p.Name));
                    if (close != null)
                    {
                        pendingBare.Remove(a);
                        TextSpan span = region.Source.ToSource(BareWord(a).Value.Node.Span);
                        Report(span, DiagnosticSeverity.Error, DiagnosticCodes.UnknownParameter, $"{display} has no flag '{w}'. Did you mean '{close}'?",
                            new[] { new QuickFix($"Change to {close}", new TextChange(span, close)) });
                        continue;
                    }
                    // Left pending: reported as an unknown name after the call. It is still the value given for this
                    // parameter, so the call isn't also told that it needs one.
                    filled[positional] = a;
                    positional++;
                    continue;
                }

                if (seenNamed)
                {
                    late.Add(a);
                    continue;
                }
                if (SlotAt(positional) is ParameterSlot s)
                {
                    CheckEnumLiteral(display, a, s, region);
                    ClaimTyped(a, s, region);
                    ResolveEnumItems(display, a, s, region);
                }
                filled[positional] = a;
                positional++;
            }

            int max = signature.Any(p => p.Variadic) ? int.MaxValue : signature.Count;
            if (!special)
            {
                return false;
            }
            if (positional > max)
            {
                Report(callSpan, DiagnosticSeverity.Error, DiagnosticCodes.ArgumentCount,
                    $"{display} takes at most {max} argument{(max == 1 ? string.Empty : "s")}, but {positional} {(positional == 1 ? "was" : "were")} given.");
                return true;
            }

            // Positional arguments after named ones fill the parameters still empty, in order.
            foreach (ArgumentNode a in late)
            {
                int free = 0;
                while (filled.ContainsKey(free))
                {
                    free++;
                }
                ParameterSlot? slot = SlotAt(free);
                if (slot == null)
                {
                    Report(region.Source.ToSource(a.Span), DiagnosticSeverity.Error, DiagnosticCodes.ArgumentCount, $"{display} has no parameter left for this argument.");
                    continue;
                }
                if (BareWord(a) is var (w, neg, _) && pendingBare.Contains(a))
                {
                    pendingBare.Remove(a);
                    if (neg || slot.Value.Match(w) == null)
                    {
                        ResolveEnumWord(display, a, slot.Value, w, region);
                    }
                }
                else
                {
                    CheckEnumLiteral(display, a, slot.Value, region);
                    ClaimTyped(a, slot.Value, region);
                    ResolveEnumItems(display, a, slot.Value, region);
                }
                filled[free] = a;
            }

            if (special)
            {
                List<string> missing = signature.Where((p, i) => p.Required && !filled.ContainsKey(i)).Select(p => p.Name).ToList();
                if (missing.Count > 0)
                {
                    Report(callSpan, DiagnosticSeverity.Error, DiagnosticCodes.ArgumentCount,
                        $"{display} needs a value for {string.Join(" and ", missing.Select(m => "'" + m + "'"))}.");
                }
            }
            return special;
        }

        /// <summary>A bare word on an enum parameter: fine if it is one of the values, otherwise a diagnostic with the closest.</summary>
        private void ResolveEnumWord(string display, ArgumentNode a, ParameterSlot slot, string word, ExpressionRegion region)
        {
            pendingBare.Remove(a);
            IdentifierNode node = BareWord(a)?.Node;
            TextSpan span = region.Source.ToSource(node?.Span ?? a.Value.Span);
            if (slot.Match(word) != null)
            {
                return;
            }
            if (!slot.IsEnum)
            {
                if (a.Value is IdentifierNode literal && TryPatternLiteral(literal, region)) return;
                Report(span, DiagnosticSeverity.Error, DiagnosticCodes.BareIdentifier,
                    $"'{word}' isn't a value. '{slot.Name}' of {display} isn't an enum, so write text in quotes ('{word}') or a variable (${word}).",
                    new[] { new QuickFix($"Quote as '{word}'", new TextChange(span, "'" + word + "'")) });
                return;
            }
            ReportBadValue(display, slot, word, span, quoted: false);
        }

        private void ClaimRawListItems(ArgumentNode argument, string type, ExpressionRegion region, Scope scope, int offset)
        {
            string list = type?.TrimEnd('?');
            if (list?.EndsWith("[]", StringComparison.Ordinal) != true || !pendingBareItems.ContainsKey(argument)) return;
            string itemType = list[..^2].TrimEnd();
            if (RawTextKindOf(itemType, scope, offset) == null) return;
            foreach (IdentifierNode item in pendingBareItems[argument])
            {
                CheckLiteralFits(item.Name, itemType, region.Source.ToSource(item.Span), scope, offset);
            }
            pendingBareItems.Remove(argument);
        }

        /// <summary>
        /// Bare words in an array on a list-of-enum parameter (<c>@pack([large, small])</c> for <c>sizes: Size[]</c>):
        /// each must be one of the values. On any other parameter they stay pending and are reported as unknown names.
        /// </summary>
        private void ResolveEnumItems(string display, ArgumentNode a, ParameterSlot slot, ExpressionRegion region)
        {
            if (a.Value is not ListNode) return;
            if (!slot.IsEnumList)
            {
                int offset = region.Source.ToSource(a.Span.Start);
                ClaimRawListItems(a, slot.Type, region, result.ScopeAt(offset), offset);
                return;
            }
            List<IdentifierNode> items = pendingBareItems.Remove(a, out List<IdentifierNode> pending) ? pending : new List<IdentifierNode>();
            var asItems = slot with { Values = slot.ItemValues };
            foreach (IdentifierNode item in items)
            {
                if (slot.MatchItem(item.Name) == null)
                {
                    ReportBadValue(display, asItems, item.Name, region.Source.ToSource(item.Span), quoted: false);
                }
            }
            foreach (ExprNode literal in ((ListNode)a.Value).Items)
            {
                if (literal is LiteralNode { Kind: LiteralKind.String } text && slot.MatchItem(text.Value) == null)
                {
                    ReportBadValue(display, asItems, text.Value, region.Source.ToSource(text.Span), quoted: true);
                }
            }
        }

        /// <summary>A text literal on an enum parameter must be one of its values.</summary>
        private void CheckEnumLiteral(string display, ArgumentNode a, ParameterSlot slot, ExpressionRegion region)
        {
            if (slot.IsEnum && a.Value is LiteralNode { Kind: LiteralKind.String } literal && slot.Match(literal.Value) == null)
            {
                ReportBadValue(display, slot, literal.Value, region.Source.ToSource(literal.Span), quoted: true);
            }
        }

        private void ReportBadValue(string display, ParameterSlot slot, string word, TextSpan span, bool quoted)
        {
            string suggestion = Suggestions.Closest(word, slot.Values);
            string what = slot.TypeName != null ? $"a {slot.TypeName}" : $"a valid {slot.Name} for {display}";
            Report(span, DiagnosticSeverity.Error, DiagnosticCodes.InvalidArgumentValue,
                $"'{word}' isn't {what}." + (suggestion != null ? $" Did you mean {suggestion}?" : string.Empty) + $" Use one of: {string.Join(", ", slot.Values)}.",
                suggestion != null ? new[] { new QuickFix($"Change to {suggestion}", new TextChange(span, quoted ? "'" + suggestion + "'" : suggestion)) } : null);
        }
    }
}
