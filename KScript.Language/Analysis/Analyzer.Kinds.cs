using System.Globalization;
using KScript.Syntax;
using KScript.Syntax.Text;

namespace KScript.Language.Analysis
{
    /// <summary>
    /// Checks for values of the wrong kind, found before the script runs: a misspelt field of a map, arithmetic on text
    /// or a list, and text given where a number is needed. They only speak up when the value is certain (a literal, or
    /// a variable that only ever holds one), so a value that might be right at run time is left alone.
    /// </summary>
    public sealed partial class Analyzer
    {
        /// <summary>Variables declared with plain text: <c>name = 'Joan'</c>.</summary>
        private readonly Dictionary<Symbol, string> literalTexts = new Dictionary<Symbol, string>();

        /// <summary>Field names the document assigns somewhere: <c>$person.email = ...</c>, <c>$person['email'] = ...</c>.</summary>
        private readonly HashSet<string> writtenFields = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Warnings that only hold if something stays true to the end of the document.</summary>
        private readonly List<(Func<bool> StillTrue, Action Report)> deferred = new List<(Func<bool>, Action)>();

        private void ReportDeferred()
        {
            foreach ((Func<bool> stillTrue, Action report) in deferred)
            {
                if (stillTrue())
                {
                    report();
                }
            }
            deferred.Clear();
        }

        /// <summary>True while the variable is never given another value after it is declared.</summary>
        private bool NeverChanges(Symbol symbol) =>
            !result.References.Any(r => r.Symbol == symbol && r.IsWrite && !r.IsDeclaration);

        private static bool IsNumberText(string text) =>
            text.Trim().Length > 0 && double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _);

        /// <summary>
        /// Says what <paramref name="node"/> is when it can't be a number: text that isn't one (<c>'Joan'</c>, or a
        /// variable that only holds it), a list, a map or a function. Null when it could be a number.
        /// </summary>
        private string NotANumber(ExprNode node, ExpressionRegion region, out Symbol holder)
        {
            holder = null;
            switch (node)
            {
                case LiteralNode { Kind: LiteralKind.String, Template: null } text:
                    return IsNumberText(text.Value) ? null : $"'{Shown(text.Value)}' is text";

                case VariableNode v:
                {
                    int offset = region.Source.ToSource(v.Span).Start;
                    Symbol symbol = result.RootScope.Innermost(offset).Lookup(v.Name, offset);
                    if (symbol != null && literalTexts.TryGetValue(symbol, out string held))
                    {
                        holder = symbol;
                        return IsNumberText(held) ? null : $"${v.Name} holds the text '{Shown(held)}'";
                    }
                    break;
                }

                case ListNode _:
                    return "this is a list";

                case MapNode _:
                    return "this is a map";

                case LambdaNode _:
                    return "this is a function";
            }

            TypeShape shape = Infer(node, region);
            return shape.Type == KType.List ? (node is VariableNode list ? $"${list.Name} is a list" : "this is a list")
                : shape.Type == KType.Map && shape.Name == null ? (node is VariableNode map ? $"${map.Name} is a map" : "this is a map")
                : null;
        }

        private static string Shown(string text) => text.Length > 24 ? text.Substring(0, 23) + "…" : text;

        private void WarnUnless(Symbol holder, TextSpan span, string code, string message, IReadOnlyList<QuickFix> fixes = null)
        {
            if (holder == null)
            {
                Report(span, DiagnosticSeverity.Warning, code, message, fixes);
                return;
            }
            deferred.Add((() => NeverChanges(holder), () => Report(span, DiagnosticSeverity.Warning, code, message, fixes)));
        }

        /// <summary>
        /// <c>'Joan' - 1</c>, <c>$names / 2</c>, <c>[1, 2] + 3</c>: arithmetic that stops the script when it runs. Text
        /// that reads as a number (<c>'5' - 1</c>), text joined with + and text repeated with * are all fine.
        /// </summary>
        private void CheckArithmetic(BinaryNode binary, ExpressionRegion region)
        {
            string op = binary.Operator;
            if (op == "+")
            {
                CheckAddition(binary, region);
                return;
            }

            string left = NotANumber(binary.Left, region, out Symbol leftHolder);
            string right = NotANumber(binary.Right, region, out Symbol rightHolder);
            bool repeats = op == "*" && left != null && left.Contains("text", StringComparison.Ordinal);
            if (repeats && right == null)
            {
                return; // 'ab' * 3 repeats the text.
            }
            if (right != null)
            {
                bool swapped = op == "*" && left == null && right.Contains("text", StringComparison.Ordinal);
                WarnUnless(rightHolder, region.Source.ToSource(binary.Right.Span), DiagnosticCodes.TypeMismatch,
                    $"'{binary.OperatorText}' needs a number here, but {right}." + (swapped ? " To repeat text, put the text first: 'ab' * 3." : string.Empty));
            }
            if (left != null && !repeats)
            {
                WarnUnless(leftHolder, region.Source.ToSource(binary.Left.Span), DiagnosticCodes.TypeMismatch,
                    $"'{binary.OperatorText}' needs a number here, but {left}.");
            }
        }

        /// <summary><c>$list + 1</c>: + joins two lists, two maps or text, but a list and a number is an error.</summary>
        private void CheckAddition(BinaryNode binary, ExpressionRegion region)
        {
            TypeShape left = Infer(binary.Left, region);
            TypeShape right = Infer(binary.Right, region);
            if (left.Type == KType.List && right.Type == KType.Number)
            {
                Report(region.Source.ToSource(binary.Right.Span), DiagnosticSeverity.Warning, DiagnosticCodes.TypeMismatch,
                    "'+' joins two lists, but this is a number. To add an item to a list, use ->push(item).");
            }
            else if (left.Type == KType.Number && right.Type == KType.List)
            {
                Report(region.Source.ToSource(binary.Left.Span), DiagnosticSeverity.Warning, DiagnosticCodes.TypeMismatch,
                    "'+' joins two lists, but this is a number. To add an item to the start of a list, use ->prepend(item).");
            }
        }

        /// <summary>An argument that reached a parameter which needs a number: <c>->repeat('x')</c>, <c>@double('five')</c> for <c>fn double(n: int)</c>.</summary>
        private void CheckNumberArgument(ArgumentNode a, ParameterSlot slot, ExpressionRegion region)
        {
            if (slot.Type is not ("number" or "int" or "float") || a.Value == null)
            {
                return;
            }
            string problem = NotANumber(a.Value, region, out Symbol holder);
            if (problem != null)
            {
                WarnUnless(holder, region.Source.ToSource(a.Value.Span), DiagnosticCodes.TypeMismatch,
                    $"The '{slot.Name}' argument needs a number, but {problem}.");
            }
        }

        /// <summary>
        /// <c>$person.nmae</c> where $person was written as <c>{ name: ..., age: ... }</c>: a field the map doesn't have
        /// reads as nothing, so a near miss of a field it does have is almost always a typo.
        /// </summary>
        private void CheckMapField(TypeShape target, MemberNode member, ExpressionRegion region)
        {
            if (target.Type == KType.String || target.Type == KType.Number || target.Type == KType.Bool || (target.Type == KType.List && member.Name is "count" or "length"))
            {
                // $name.upper: a method needs -> and ( ).
                TextSpan at = region.Source.ToSource(TextSpan.FromBounds(member.NameSpan.Start - 1, member.NameSpan.End));
                // .length is the habit from other languages: the method is ->count().
                string called = member.Name == "length" && target.Type is KType.String or KType.List ? "count" : member.Name;
                bool method = catalog.GetMethods(called, target.Type).Any(m => KType.AcceptsMethod(m.ReceiverType, m.Name, target.Type));
                string what = target.Type == KType.String ? "Text has" : target.Type == KType.List ? "A list has" : target.Type == KType.Number ? "A number has" : "A true/false value has";
                Report(at, DiagnosticSeverity.Warning, DiagnosticCodes.UnknownField,
                    $"{what} no fields, so .{member.Name} stops the script." + (method ? $" To call the method, write ->{called}()." : string.Empty),
                    method && !member.NullSafe ? new[] { new QuickFix($"Change to ->{called}()", new TextChange(at, "->" + called + "()")) } : null);
                return;
            }
            if (target.Type != KType.Map || target.Name != null || target.Closed || target.Kind != null || target.IsFrozen || member.Name.Length == 0
                || target.Fields.Count == 0 || target.Fields.ContainsKey(member.Name) || target.Fields.Values.Any(f => f.Declaration == null))
            {
                return;
            }
            // Short names (x and y, a and b) are close to each other without being typos.
            string close = member.Name.Length < 3 ? null : Suggestions.Closest(member.Name, target.Fields.Keys.Where(k => k.Length >= 3));
            if (close == null)
            {
                return;
            }
            TextSpan span = region.Source.ToSource(member.NameSpan);
            string name = member.Name;
            IReadOnlyList<QuickFix> fixes = new[] { new QuickFix($"Change to {close}", new TextChange(span, close)) };
            deferred.Add((() => !writtenFields.Contains(name), () => Report(span, DiagnosticSeverity.Warning, DiagnosticCodes.UnknownField,
                $"This map has no field '{name}', so this reads as nothing. Did you mean {close}?", fixes)));
        }
    }
}
