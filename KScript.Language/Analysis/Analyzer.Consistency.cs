using KScript.Metadata;
using KScript.Syntax;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Language.Analysis
{
    // How a .kscript argument is written. The old forms are errors, found by OldSpellings for this checker and for
    // the interpreter alike, and kscript check --fix rewrites them. A length of time without its unit (KS6018) is
    // style: a plain number is seconds, so it still runs.
    public sealed partial class Analyzer
    {
        private bool IsCodeFile => result.Markup.Syntax == SourceSyntax.Code;

        /// <summary>
        /// How a .kscript argument is written. Quotes are strings and everything else is code, so:
        /// KS6009 text given as a bare word (<c>title: Report</c>) goes in quotes; KS6006 a list given as one string
        /// (<c>each: "key, value"</c>) is an array; KS6011 yes/no is true/false.
        /// </summary>
        private void CheckArgumentForm(MarkupElement e, MarkupAttribute attr, AttributeInfo a)
        {
            if (!IsCodeFile)
            {
                CheckXmlDuration(attr, a);
                return;
            }
            if (!attr.IsArgument || attr.Value == null)
            {
                return;
            }
            if (attr.FormError != null)
            {
                Report(attr.Span, DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue, attr.FormError,
                    new[] { new QuickFix(Fixer.CodeInBracesTitle, new TextChange(ValueSpan(attr), "{ " + attr.ValueText.Trim() + " }")) });
                return;
            }

            string text = attr.ValueText.Trim();
            if (text.Length == 0)
            {
                return;
            }
            TextSpan written = ValueSpan(attr);
            if (a.Type == DurationType && DurationAsWritten(attr.Quote == '\0' ? Unquoted(text) ?? text : Unquoted(source.Text.Substring(written.Start, written.Length))) is string duration
                && (attr.Quote != '\0' || duration != text))
            {
                ReportDurationUnit(written, attr.Name, duration);
                return;
            }
            foreach (OldSpelling found in OldSpellings.InArgument(e, attr, a, source.Text))
            {
                Report(found.Span, DiagnosticSeverity.Error, found.Code, found.Message, found.Fix == null ? null : new[] { found.Fix });
            }
        }

        private const string DurationType = "duration";

        private static readonly System.Text.RegularExpressions.Regex DurationText =
            new System.Text.RegularExpressions.Regex(@"^(?:\d+(?:\.\d+)?(?:ms|min|w|d|h|m|s))+$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        private static readonly System.Text.RegularExpressions.Regex PlainNumber =
            new System.Text.RegularExpressions.Regex(@"^\d+(?:\.\d+)?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        /// <summary>
        /// KS6018. How a length of time given as a plain number (<c>30</c>, which is seconds) or as text
        /// (<c>'10m'</c>) is written as a duration: <c>30s</c>, <c>10m</c>. Null for anything else, and for 0.
        /// </summary>
        private static string DurationAsWritten(string text)
        {
            text = text?.Trim();
            if (string.IsNullOrEmpty(text)) return null;
            if (DurationText.IsMatch(text)) return text;
            return PlainNumber.IsMatch(text) && text.Trim('0', '.').Length > 0 ? text + "s" : null;
        }

        /// <summary>Text without the quotes around it, or null when it isn't one plain quoted string.</summary>
        private static string Unquoted(string written)
        {
            written = written.Trim();
            return written.Length >= 2 && written[0] is '\'' or '"' && written[^1] == written[0] && written.IndexOf(written[0], 1) == written.Length - 1
                ? written.Substring(1, written.Length - 2)
                : null;
        }

        private void ReportDurationUnit(TextSpan span, string name, string duration, bool attribute = false) =>
            Report(span, DiagnosticSeverity.Warning, DiagnosticCodes.DurationUnit,
                $"Write the length of time with its unit and no quotes: {(attribute ? name + " as " + duration : name + ": " + duration)}. A plain number is seconds.",
                new[] { new QuickFix($"Write {duration}", new TextChange(span, duration)) });

        /// <summary>KS6018 in XML: <c>timeout="30"</c> and <c>ttl="'10m'"</c> are written <c>timeout="30s"</c> and <c>ttl="10m"</c>.</summary>
        private void CheckXmlDuration(MarkupAttribute attr, AttributeInfo a)
        {
            if (a.Type != DurationType || attr.Value == null || result.Markup.Syntax == SourceSyntax.Code)
            {
                return;
            }
            string text = attr.ValueText.Trim();
            if (text.Length > 0 && DurationAsWritten(Unquoted(text) ?? text) is string duration && duration != text)
            {
                ReportDurationUnit(TrimmedSpan(attr.Value), attr.Name, duration, attribute: true);
            }
        }

        /// <summary>The value of an argument as written, quotes included.</summary>
        private TextSpan ValueSpan(MarkupAttribute attr) => OldSpellings.ValueSpan(attr, source.Text);
    }
}
