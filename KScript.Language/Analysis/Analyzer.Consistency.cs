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
        private static readonly bool MigrateQuotedCode = Environment.GetEnvironmentVariable("KSCRIPT_MIGRATE") == "1";

        private bool IsCodeFile => result.Markup.Syntax == SourceSyntax.Code;

        /// <summary>
        /// How a .kscript argument is written. Quotes are strings and everything else is code, so:
        /// KS6009 text given as a bare word (<c>title: Report</c>) goes in quotes; KS6006 a list given as one string
        /// (<c>each: "key, value"</c>) is an array; KS6011 yes/no is true/false.
        /// </summary>
        private void CheckArgumentForm(MarkupElement e, MarkupAttribute attr, AttributeInfo a)
        {
            if (!IsCodeFile || !attr.IsArgument || attr.Value == null)
            {
                return;
            }
            if (attr.FormError != null)
            {
                Report(attr.Span, DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue, attr.FormError,
                    new[] { new QuickFix("Write it in braces", new TextChange(ValueSpan(attr), "{ " + attr.ValueText.Trim() + " }")) });
                return;
            }

            if (MigrateQuotedCode && attr.Quote == '"' && CodeArguments.IsValue(a.Kind))
            {
                // MIGRATION ONLY: files written before quotes always made a string wrapped code in them.
                string code = Quotes.Decode(attr.ValueText, 1, attr.ValueText.Length - 1, '"').Trim();
                bool safe = !code.Contains('\n') && SplitNames(DecodedText.Plain(code, 0)).Count() <= 1;
                string now = a.Kind == AttributeKind.ExpressionList ? "[" + code + "]" : safe ? code : "(" + code + ")";
                Report(ValueSpan(attr), DiagnosticSeverity.Warning, DiagnosticCodes.OneSpelling, "migrate", new[] { new QuickFix("migrate", new TextChange(ValueSpan(attr), now)) });
                return;
            }

            string text = attr.ValueText.Trim();
            if (text.Length == 0)
            {
                return;
            }
            TextSpan written = ValueSpan(attr);
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
