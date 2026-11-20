using KScript.Language.Catalog;
using KScript.Syntax;
using KScript.Syntax.Text;

namespace KScript.Language.Analysis
{
    /// <summary>
    /// Checks named arguments (<c>@known_folder(name: 'downloads')</c>) the way the runtime binds them: positional
    /// arguments first, then each name fills the parameter of that name.
    /// </summary>
    public sealed partial class Analyzer
    {
        private readonly record struct ParameterSlot(string Name, bool Required, bool Variadic);

        private static List<List<ParameterSlot>> SlotsOf(IEnumerable<CallableInfo> callables) =>
            callables.SelectMany(c => c.Signatures)
                .Select(s => s.Parameters.Select(p => new ParameterSlot(p.Name, !p.Optional && !p.Variadic, p.Variadic)).ToList())
                .ToList();

        private static List<List<ParameterSlot>> SlotsOf(Symbol fn) =>
            new List<List<ParameterSlot>> { fn.Parameters.Select(p => new ParameterSlot(p.Name, !p.HasDefault, false)).ToList() };

        /// <summary>
        /// Checks a call that has named arguments. Returns false (and reports nothing) when no argument is named, so
        /// the caller can check the argument count as usual.
        /// </summary>
        private bool CheckNamedArguments(string display, IReadOnlyList<ArgumentNode> args, int piped, List<List<ParameterSlot>> signatures, ExpressionRegion region, TextSpan callSpan)
        {
            if (!args.Any(a => a.Name != null) || signatures.Count == 0)
            {
                return false;
            }

            bool seenNamed = false, misordered = false;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (ArgumentNode a in args)
            {
                if (a.Name == null)
                {
                    if (seenNamed)
                    {
                        Report(region.Source.ToSource(a.Span), DiagnosticSeverity.Error, DiagnosticCodes.NamedArgument,
                            "Positional arguments must come before named ones (name: value).");
                        misordered = true;
                    }
                    continue;
                }
                seenNamed = true;
                if (!names.Add(a.Name))
                {
                    Report(region.Source.ToSource(a.NameSpan), DiagnosticSeverity.Error, DiagnosticCodes.NamedArgument, $"'{a.Name}' is given twice.");
                }
            }

            if (misordered)
            {
                return true;
            }

            List<ArgumentNode> named = args.Where(a => a.Name != null).ToList();
            int positional = piped + args.Count(a => a.Name == null);

            static int IndexOf(List<ParameterSlot> s, string name) => s.FindIndex(p => !p.Variadic && p.Name == name);
            List<ParameterSlot> signature = signatures.FirstOrDefault(s => named.All(a => IndexOf(s, a.Name) >= 0))
                ?? signatures.FirstOrDefault(s => s.Any(p => p.Variadic));
            if (signature == null)
            {
                List<string> known = signatures.SelectMany(s => s).Where(p => !p.Variadic).Select(p => p.Name).Distinct().ToList();
                foreach (ArgumentNode a in named.Where(a => IndexOf(signatures[0], a.Name) < 0))
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

            int max = signature.Any(p => p.Variadic) ? int.MaxValue : signature.Count;
            if (positional > max)
            {
                Report(callSpan, DiagnosticSeverity.Error, DiagnosticCodes.ArgumentCount,
                    $"{display} takes at most {max} argument{(max == 1 ? string.Empty : "s")}, but {positional} {(positional == 1 ? "was" : "were")} given before the named ones.");
            }

            var filled = new HashSet<int>(Enumerable.Range(0, Math.Min(positional, signature.Count)));
            foreach (ArgumentNode a in named)
            {
                int index = IndexOf(signature, a.Name);
                if (index < 0)
                {
                    continue;
                }
                if (index < positional)
                {
                    Report(region.Source.ToSource(a.NameSpan), DiagnosticSeverity.Error, DiagnosticCodes.NamedArgument,
                        $"'{a.Name}' is already given as argument {index + 1}" + (piped > 0 && index == 0 ? " (the piped value)." : "."));
                }
                filled.Add(index);
            }

            List<string> missing = signature.Where((p, i) => p.Required && !filled.Contains(i)).Select(p => p.Name).ToList();
            if (missing.Count > 0)
            {
                Report(callSpan, DiagnosticSeverity.Error, DiagnosticCodes.ArgumentCount,
                    $"{display} needs a value for {string.Join(" and ", missing.Select(m => "'" + m + "'"))}.");
            }
            return true;
        }
    }
}
