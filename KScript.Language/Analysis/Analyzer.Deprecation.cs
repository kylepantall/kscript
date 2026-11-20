using KScript.Metadata;
using KScript.Syntax;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Language.Analysis
{
    /// <summary>
    /// Warnings (KS4008) for deprecated commands, methods, elements, attributes and parameters, which packages mark
    /// with <c>PackageBuilder.Deprecate</c>. The warning says why and what to use instead, with a quick fix when the
    /// replacement is a plain rename.
    /// </summary>
    public sealed partial class Analyzer
    {
        private void ReportDeprecated(Deprecation deprecation, string subject, TextSpan span, TextSpan? renameSpan = null)
        {
            if (deprecation == null)
            {
                return;
            }
            IReadOnlyList<QuickFix> fixes = renameSpan is TextSpan at && deprecation.RenameTo is string to
                ? new[] { new QuickFix($"Change to {to}", new TextChange(at, to)) }
                : null;
            Report(span, DiagnosticSeverity.Warning, DiagnosticCodes.Deprecated, deprecation.Describe(subject), fixes);
        }

        /// <summary>A deprecated command or method, and any deprecated parameters the call passes.</summary>
        private void ReportDeprecatedCall(CallableInfo callable, string display, TextSpan nameSpan, IReadOnlyList<ArgumentNode> args, int piped, ExpressionRegion region)
        {
            ReportDeprecated(callable.Deprecated, display, nameSpan, nameSpan);
            if (args == null || !callable.Signatures.Any(s => s.Parameters.Any(p => p.Deprecated != null)))
            {
                return;
            }

            ParameterInfo Named(string name) =>
                callable.Signatures.SelectMany(s => s.Parameters).FirstOrDefault(p => p.Name == name && p.Deprecated != null);

            int position = piped;
            bool seenNamed = false;
            foreach (ArgumentNode a in args)
            {
                if (a.Name != null)
                {
                    seenNamed = true;
                    if (Named(a.Name) is ParameterInfo named)
                    {
                        TextSpan span = region.Source.ToSource(a.NameSpan);
                        ReportDeprecated(named.Deprecated, $"The '{a.Name}' parameter of {display}", span, span);
                    }
                    continue;
                }
                if (BareWord(a) is var (word, _, node) && Named(word) is ParameterInfo flag && flag.IsFlag)
                {
                    TextSpan span = region.Source.ToSource(node.Span);
                    ReportDeprecated(flag.Deprecated, $"The '{word}' flag of {display}", span, span);
                    continue;
                }
                if (!seenNamed && callable.Signatures.Count == 1)
                {
                    IReadOnlyList<ParameterInfo> ps = callable.Signatures[0].Parameters;
                    if (position < ps.Count && !ps[position].Variadic && ps[position].Deprecated != null)
                    {
                        ReportDeprecated(ps[position].Deprecated, $"The '{ps[position].Name}' parameter of {display}", region.Source.ToSource(a.Span));
                    }
                }
                position++;
            }
        }

        private void ReportDeprecatedElement(MarkupElement e, ElementInfo info)
        {
            ReportDeprecated(info.Deprecated, $"<{e.Name}>", e.NameSpan, e.NameSpan);
            foreach (MarkupAttribute attr in e.Attributes)
            {
                if (info.GetAttribute(attr.Name)?.Deprecated is Deprecation d)
                {
                    ReportDeprecated(d, $"The '{attr.Name}' attribute of <{e.Name}>", attr.NameSpan,
                        d.RenameTo != null && info.GetAttribute(d.RenameTo) != null && e.GetAttribute(d.RenameTo) == null ? attr.NameSpan : null);
                }
            }
        }
    }
}
