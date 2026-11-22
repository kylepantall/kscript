using KScript.Syntax;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Language.Analysis
{
    // One spelling for each thing in an expression. OldSpellings finds the old ones, for this checker and for the
    // interpreter, which refuses to run them; kscript check --fix rewrites them. (else if, key = value and
    // catch A, B are reported by the .kscript parser itself, under the same code.)
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
                foreach (OldSpelling found in OldSpellings.InExpression(roots, region.Source.Value, IsCodeFile))
                {
                    Report(region.Source.ToSource(found.Span), OldSpellings.Severity(IsCodeFile), found.Code, found.Message,
                        found.Fix == null ? null : new[] { new QuickFix(found.Fix.Title, found.Fix.Changes.Select(c => new TextChange(region.Source.ToSource(c.Span), c.NewText)).ToArray()) });
                }
            }
        }
    }
}
