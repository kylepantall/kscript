using KScript.Language.Catalog;
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
        internal readonly record struct ParameterSlot(string Name, bool Required, bool Variadic, bool Flag = false, IReadOnlyList<string> Values = null, string TypeName = null)
        {
            public bool IsEnum => Values != null && Values.Count > 0;

            public string Match(string word) => IsEnum ? Values.FirstOrDefault(v => string.Equals(v, word.Trim(), StringComparison.OrdinalIgnoreCase)) : null;
        }

        /// <summary>Bare-word arguments (<c>downloads</c>, <c>not recursive</c>) waiting for their call to say what they are.</summary>
        private readonly HashSet<ArgumentNode> pendingBare = new HashSet<ArgumentNode>();

        private static List<List<ParameterSlot>> SlotsOf(IEnumerable<CallableInfo> callables) =>
            callables.SelectMany(c => c.Signatures)
                .Select(s => s.Parameters.Select(p => new ParameterSlot(p.Name, !p.Optional && !p.Variadic, p.Variadic, p.IsFlag, p.Values.Count > 0 ? p.Values : null)).ToList())
                .ToList();

        private static List<List<ParameterSlot>> SlotsOf(Symbol fn, Scope scope) =>
            new List<List<ParameterSlot>>
            {
                fn.Parameters.Select(p =>
                {
                    Symbol type = p.Type != null && !KType.Declarable.Contains(p.Type) && p.Type != "number" && p.Type != "date" && p.Type != "any"
                        ? scope?.Lookup(p.Type, int.MaxValue) : null;
                    return new ParameterSlot(p.Name, !p.HasDefault, false, p.Type == KType.Bool, type?.EnumValues, type?.EnumValues != null ? type.Name : null);
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
        private void FlushBare(IEnumerable<ArgumentNode> args, ExpressionRegion region, Scope scope)
        {
            foreach (ArgumentNode a in args)
            {
                if (pendingBare.Remove(a) && BareWord(a) is var (_, _, node))
                {
                    AnalyzeBareIdentifier(node, region, scope, ExprContext.Argument);
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
            bool special = args.Any(a => a.Name != null || BareWord(a) != null);

            static int IndexOf(List<ParameterSlot> s, string name) => s.FindIndex(p => !p.Variadic && p.Name == name);
            List<ParameterSlot> signature = signatures.FirstOrDefault(s => args.Where(a => a.Name != null).All(a => IndexOf(s, a.Name) >= 0))
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
                    Fill(index, a, a.Name);
                    continue;
                }

                if (bare is var (w, neg, _) && pendingBare.Contains(a))
                {
                    ParameterSlot? slot = SlotAt(positional);
                    if (!seenNamed && !neg && slot?.Match(w) != null)
                    {
                        pendingBare.Remove(a);
                        Fill(positional, a, slot.Value.Name);
                        positional++;
                        continue;
                    }
                    int flag = IndexOf(signature, w);
                    if (flag >= 0)
                    {
                        pendingBare.Remove(a);
                        ParameterSlot target = signature[flag];
                        if (!target.Flag && (target.IsEnum || target.TypeName != null))
                        {
                            Report(region.Source.ToSource(a.Span), DiagnosticSeverity.Error, DiagnosticCodes.NamedArgument,
                                $"'{w}' isn't a yes/no parameter of {display}, so it can't be written as a flag. Give it a value: {w}: ...");
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
                    string close = Suggestions.Closest(w, signature.Where(p => !p.Variadic).Select(p => p.Name));
                    if (close != null)
                    {
                        pendingBare.Remove(a);
                        TextSpan span = region.Source.ToSource(BareWord(a).Value.Node.Span);
                        Report(span, DiagnosticSeverity.Error, DiagnosticCodes.UnknownParameter, $"{display} has no flag '{w}'. Did you mean '{close}'?",
                            new[] { new QuickFix($"Change to {close}", new TextChange(span, close)) });
                        continue;
                    }
                    positional++; // Left pending: reported as an unknown name after the call.
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
                Report(span, DiagnosticSeverity.Error, DiagnosticCodes.BareIdentifier,
                    $"'{word}' isn't a value. '{slot.Name}' of {display} isn't an enum, so write text in quotes ('{word}') or a variable (${word}).",
                    new[] { new QuickFix($"Quote as '{word}'", new TextChange(span, "'" + word + "'")) });
                return;
            }
            ReportBadValue(display, slot, word, span, quoted: false);
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
