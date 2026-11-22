using KScript.Syntax;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Language.Analysis
{
    // One spelling for each thing in an expression. The parser still reads the second spellings below, so a file
    // written with them runs; these rules say which spelling is the language's and offer the fix, and
    // kscript check --fix rewrites them. (else if, key = value and catch A, B are reported by the .kscript parser
    // itself, under the same code.)
    public sealed partial class Analyzer
    {
        /// <summary>
        /// KS6010: a name is declared bare, so a lambda is <c>fn(n) =&gt; $n * 2</c> and an assignment is
        /// <c>total += 1</c>. KS6012: in a .kscript file comparisons are symbols (<c>&lt;</c>, <c>==</c>), and logic is
        /// words (<c>and</c>, <c>or</c>, <c>not</c>).
        /// </summary>
        private void CheckSpellings()
        {
            foreach (ExpressionRegion region in result.Regions.ToList())
            {
                IEnumerable<ExprNode> roots = region.Parse != null
                    ? region.Parse.Nodes
                    : region.Template?.Expressions.Select(s => s.Node) ?? Enumerable.Empty<ExprNode>();
                foreach (ExprNode node in roots.Where(n => n != null).SelectMany(n => n.DescendantsAndSelf()))
                {
                    switch (node)
                    {
                        case BinaryNode binary when IsCodeFile && binary.OperatorText != null && CodeSpellings.ComparisonSymbols.TryGetValue(binary.OperatorText, out string symbol)
                            && SourceIs(region, binary.OperatorSpan, binary.OperatorText):
                        {
                            TextSpan span = region.Source.ToSource(binary.OperatorSpan);
                            Report(span, DiagnosticSeverity.Warning, DiagnosticCodes.OneSpelling,
                                $"Write {symbol}: in a .kscript file comparisons are symbols ('{binary.OperatorText}' is the XML spelling).",
                                new[] { new QuickFix($"Change to {symbol}", new TextChange(span, symbol)) });
                            break;
                        }

                        case LambdaNode lambda:
                            foreach (ParameterNode p in lambda.Parameters.Where(p => StartsWithSigil(region, p.Span)))
                            {
                                TextSpan span = region.Source.ToSource(p.Span);
                                Report(span, DiagnosticSeverity.Warning, DiagnosticCodes.BareDeclaration,
                                    $"Declare the parameter as {p.Name}: a name is declared without $, and read with it (fn({p.Name}) => ${p.Name}).",
                                    new[] { new QuickFix($"Change to {p.Name}", new TextChange(new TextSpan(span.Start, 1), string.Empty)) });
                            }
                            break;

                        case AssignmentNode assignment when AssignmentRoot(assignment.Target) is VariableNode target && StartsWithSigil(region, target.Span):
                        {
                            TextSpan span = region.Source.ToSource(target.Span);
                            Report(span, DiagnosticSeverity.Warning, DiagnosticCodes.BareDeclaration,
                                $"Assign to {target.Name}, without $: {target.Name} {assignment.Operator} ... ($ reads a value).",
                                new[] { new QuickFix($"Change to {target.Name}", new TextChange(new TextSpan(span.Start, 1), string.Empty)) });
                            break;
                        }
                    }
                }
            }
        }

        private static ExprNode AssignmentRoot(ExprNode target)
        {
            while (target is MemberNode || target is IndexNode)
            {
                target = target is MemberNode m ? m.Target : ((IndexNode)target).Target;
            }
            return target;
        }

        private static bool StartsWithSigil(ExpressionRegion region, TextSpan span) =>
            span.Length > 1 && span.Start < region.Source.Value.Length && region.Source.Value[span.Start] == '$';

        private static bool SourceIs(ExpressionRegion region, TextSpan span, string text) =>
            span.End <= region.Source.Value.Length && string.CompareOrdinal(region.Source.Value, span.Start, text, 0, text.Length) == 0 && span.Length == text.Length;
    }
}
