using System.Text.RegularExpressions;
using KScript.Language.Analysis;
using KScript.Runtime;
using KScript.Syntax;
using KScript.Syntax.Text;

namespace KScript.Language.Features
{
    public sealed partial class LanguageService
    {
        // Where a date format is being written: x: date_format = ‸, ->format(‸ and ->to_date(‸, quoted or not.
        private static readonly Regex DateFormatBefore = new Regex(
            @"(?:\bdate_format\b(?:\([^()\n]*\))?\s*=\s*|->(?:format|to_date)\(\s*(?:(?:pattern|format)\s*:\s*)?)(?<quote>['""]?)(?<typed>[^'""()\n]*)$",
            RegexOptions.Compiled);

        /// <summary>Whole formats people use most, offered next to the letters.</summary>
        private static readonly (string Format, string Shows)[] CommonDateFormats =
        {
            ("yyyy-MM-dd", "2026-03-07"),
            ("dd-MM-yyyy", "07-03-2026"),
            ("dd/MM/yyyy", "07/03/2026"),
            ("d MMMM yyyy", "7 March 2026"),
            ("dddd d MMMM yyyy", "Saturday 7 March 2026"),
            ("HH:mm", "14:05"),
            ("yyyy-MM-dd HH:mm", "2026-03-07 14:05"),
            ("yyyy-MM-ddTHH:mm:ssZ", "2026-03-07T14:05:09Z"),
        };

        private static readonly string[] DateFormatTokens =
        {
            "d", "dd", "ddd", "dddd", "M", "MM", "MMM", "MMMM", "yy", "yyyy", "H", "HH", "h", "hh", "m", "mm", "s", "ss", "fff", "tt", "zzz", "K",
        };

        /// <summary>
        /// Inside a date format (<c>x: date_format = dd-‸</c>, <c>$day-&gt;format('d MMMM ‸')</c>): the letters and what
        /// each one shows, and whole formats when nothing is typed yet. Null anywhere else.
        /// </summary>
        private static List<CompletionItem> DateFormatCompletions(DocumentAnalysis analysis, int offset)
        {
            string text = analysis.Source.Text;
            int lineStart = offset > 0 ? text.LastIndexOf('\n', offset - 1) + 1 : 0;
            Match m = DateFormatBefore.Match(text.Substring(lineStart, offset - lineStart));
            if (!m.Success)
            {
                return null;
            }
            string typed = m.Groups["typed"].Value;
            bool quoted = m.Groups["quote"].Value.Length > 0;
            if (!quoted && typed.Length == 0)
            {
                // ->format(‸: values and variables go here too, so only offer the letters once one is typed.
                return null;
            }
            // The run of one letter at the cursor is what a pick replaces: dd-M‸ replaces the M.
            int start = offset;
            while (start > offset - typed.Length && char.IsAsciiLetter(text[start - 1]) && text[start - 1] == text[offset - 1])
            {
                start--;
            }
            var replace = TextSpan.FromBounds(start, offset);
            var items = new List<CompletionItem>();
            DateTime sample = new DateTime(2026, 3, 7, 14, 5, 9, 123);
            foreach (string token in DateFormatTokens)
            {
                NativeParsers.DateLetters.TryGetValue(token[0], out var letter);
                string shows = sample.ToString(token.Length == 1 ? "%" + token : token, System.Globalization.CultureInfo.InvariantCulture);
                items.Add(new CompletionItem
                {
                    Label = token,
                    Kind = CompletionKind.Value,
                    Detail = shows,
                    Documentation = $"`{token}` in a date format: {letter.Meaning}. Saturday 7 March 2026, 14:05:09 shows `{shows}`.",
                    ReplaceSpan = replace,
                    SortText = "000" + token,
                });
            }
            if (typed.Length == 0)
            {
                foreach ((string format, string shows) in CommonDateFormats)
                {
                    items.Add(new CompletionItem
                    {
                        Label = format,
                        Kind = CompletionKind.Snippet,
                        Detail = shows,
                        Documentation = $"The date format `{format}`: Saturday 7 March 2026, 14:05:09 is `{shows}`.",
                        ReplaceSpan = replace,
                        SortText = "001" + format,
                    });
                }
            }
            return items;
        }
    }
}
