using KScript.Metadata;
using KScript.Syntax;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Language.Analysis
{
    // Consistency rules: where the parser still reads a second spelling, these say which one is the language's, and
    // offer the fix. They are style rules (KS6xxx), so kscript:recommended warns and kscript:strict fails, and
    // kscript check --fix rewrites them.
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
                    new[] { new QuickFix("Write it in braces", new TextChange(ValueSpan(attr), "{ " + attr.ValueText.Trim() + " }")) });
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
            if (attr.Quote == '\0' && CodeArguments.TakesFunctions(ElementInfo.BareName(e.Name), a))
            {
                // KS6012: a function is @name wherever one is named, as it is in an expression: fn: @send, tools: [@lookup].
                foreach ((string name, int at) in NameList.Split(attr.ValueText))
                {
                    if (!Lexer.IsIdentStart(name[0]) || !name.All(c => Lexer.IsIdentChar(c) || c == '.'))
                    {
                        continue;
                    }
                    TextSpan span = new TextSpan(attr.Value.ToSource(at), name.Length);
                    Report(span, DiagnosticSeverity.Warning, DiagnosticCodes.OneSpelling,
                        $"Write @{name}: a function is referred to as @name wherever one is named.",
                        new[] { new QuickFix($"Write @{name}", new TextChange(new TextSpan(span.Start, 0), "@")) });
                }
                return;
            }
            if (attr.Quote == '\0' && a.IsNameList)
            {
                // Text in a list is quoted like any other text: to: ['paper.pdf', 'paper.docx']. Names stay bare.
                if (a.Kind is AttributeKind.Template or AttributeKind.Path && text[0] != '$')
                {
                    foreach ((string name, int at) in NameList.Split(attr.ValueText))
                    {
                        int offset = attr.Value.ToSource(at);
                        if (offset > 0 && source.Text[offset - 1] is '\'' or '"') continue;
                        TextSpan span = new TextSpan(offset, name.Length);
                        string quotedName = "'" + name + "'";
                        Report(span, DiagnosticSeverity.Warning, DiagnosticCodes.QuoteText,
                            $"Write text in quotes: {quotedName}. A bare word names a variable, a function or a type.",
                            new[] { new QuickFix($"Write {quotedName}", new TextChange(span, quotedName)) });
                    }
                }
                return;
            }
            if (attr.Quote == '\0' && CodeArguments.IsText(a.Kind) && !a.IsName && !attr.CodeValue && !attr.QuotedValue && !IsPlainValue(text))
            {
                string quoted = ScriptConverter.Quote(text);
                Report(written, DiagnosticSeverity.Warning, DiagnosticCodes.QuoteText,
                    $"Write text in quotes: {attr.Name}: {quoted}. A bare word names a variable, a function or a type.",
                    new[] { new QuickFix($"Write {quoted}", new TextChange(written, quoted)) });
                return;
            }
            if (attr.Quote == '\0' && a.Kind == AttributeKind.Bool && text is "yes" or "no" or "on" or "off" or "y" or "n" or "1" or "0")
            {
                string word = KScript.Runtime.Values.ParseBool(text) == true ? "true" : "false";
                Report(written, DiagnosticSeverity.Warning, DiagnosticCodes.BoolWords,
                    $"Write {word}: booleans are true and false everywhere in .kscript.",
                    new[] { new QuickFix($"Write {word}", new TextChange(written, word)) });
                return;
            }
            if (attr.Quote == '"' && !a.IsNameList && a.Kind != AttributeKind.ExpressionList && CodeArguments.IsList(a.Kind, a) && e.Name != "import")
            {
                string array = "[" + text + "]";
                Report(written, DiagnosticSeverity.Warning, DiagnosticCodes.PreferArrayList,
                    $"Write the list as an array: {attr.Name}: {array}.",
                    new[] { new QuickFix($"Write {array}", new TextChange(written, array)) });
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

        /// <summary>A number, a list, a map, true, false or null: a value as it stands, never mistaken for a name.</summary>
        private static bool IsPlainValue(string text) =>
            text is "true" or "false" or "null" || text[0] is '[' or '{' or '(' or '$' or '@' or '\'' or '`'
            || double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _);

        /// <summary>The value of an argument as written, quotes included.</summary>
        private TextSpan ValueSpan(MarkupAttribute attr)
        {
            int start = attr.NameSpan.End;
            string text = source.Text;
            while (start < attr.Span.End && (char.IsWhiteSpace(text[start]) || text[start] is ':' or '='))
            {
                // Only the first : or = separates the name from the value.
                bool separator = text[start] is ':' or '=';
                start++;
                if (separator)
                {
                    while (start < attr.Span.End && char.IsWhiteSpace(text[start])) start++;
                    break;
                }
            }
            return TextSpan.FromBounds(start, Math.Max(start, attr.Span.End));
        }
    }
}
