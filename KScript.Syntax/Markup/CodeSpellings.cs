using System.Text;

using KScript.Syntax.Text;

namespace KScript.Syntax.Markup
{
    /// <summary>
    /// Writes expressions the way a <c>.kscript</c> file spells them. XML and <c>.kscript</c> share one expression
    /// language, but XML has spellings of its own: the comparison words <c>lt le gt ge eq ne</c> (a raw <c>&lt;</c>
    /// isn't allowed in an attribute), which are the symbols <c>&lt; &lt;= &gt; &gt;= == !=</c> in a .kscript file.
    /// A lambda's parameters are declared bare in both (<c>fn(n) =&gt; $n * 2</c>), so an old <c>fn($n)</c> is tidied
    /// on the way. <see cref="ScriptConverter.ToCode"/> runs its result through this, so converted code is never
    /// reported by the spelling rules (KS6010, KS6012).
    /// </summary>
    public static class CodeSpellings
    {
        /// <summary>The comparison words, and the symbol a .kscript file writes instead.</summary>
        public static readonly IReadOnlyDictionary<string, string> ComparisonSymbols = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["lt"] = "<",
            ["le"] = "<=",
            ["gt"] = ">",
            ["ge"] = ">=",
            ["eq"] = "==",
            ["ne"] = "!=",
        };

        /// <summary>The .kscript text with comparison words written as symbols and lambda parameters declared bare.</summary>
        public static string Normalize(string code)
        {
            if (string.IsNullOrEmpty(code) || !MayNeedIt(code))
            {
                return code;
            }
            var changes = new List<TextChange>();
            Collect(code, 0, code.Length, changes, depth: 0);
            if (changes.Count == 0)
            {
                return code;
            }
            StringBuilder sb = new StringBuilder(code);
            foreach (TextChange c in changes.OrderByDescending(c => c.Span.Start))
            {
                sb.Remove(c.Span.Start, c.Span.Length);
                sb.Insert(c.Span.Start, c.NewText);
            }
            return sb.ToString();
        }

        private static bool MayNeedIt(string code) =>
            code.Contains("fn(", StringComparison.Ordinal) || ComparisonSymbols.Keys.Any(w => code.Contains(" " + w + " ", StringComparison.Ordinal));

        private static void Collect(string code, int start, int end, List<TextChange> changes, int depth)
        {
            List<Token> tokens = Lexer.Tokenize(code, start, end);
            for (int i = 0; i < tokens.Count; i++)
            {
                Token t = tokens[i];
                if (t.Kind == TokenKind.Identifier && ComparisonSymbols.TryGetValue(t.Text, out string symbol)
                    && i > 0 && i + 1 < tokens.Count && EndsValue(tokens[i - 1]) && StartsValue(tokens[i + 1])
                    && code.IndexOf('\n', tokens[i - 1].Span.End, t.Span.Start - tokens[i - 1].Span.End) < 0)
                {
                    changes.Add(new TextChange(t.Span, symbol));
                }
                else if (t.IsWord("fn") && i + 1 < tokens.Count && tokens[i + 1].Is("("))
                {
                    // fn($a, $b) => ...: the parameters, up to the ) that => follows.
                    int p = i + 2;
                    var sigils = new List<TextChange>();
                    while (p < tokens.Count && tokens[p].Kind is TokenKind.Variable or TokenKind.Identifier)
                    {
                        if (tokens[p].Kind == TokenKind.Variable)
                        {
                            sigils.Add(new TextChange(new TextSpan(tokens[p].Span.Start, 1), string.Empty));
                        }
                        p++;
                        if (p < tokens.Count && tokens[p].Is(","))
                        {
                            p++;
                        }
                    }
                    if (p + 1 < tokens.Count && tokens[p].Is(")") && tokens[p + 1].Is("=>"))
                    {
                        changes.AddRange(sigils);
                    }
                }
                else if (t.Kind == TokenKind.String && depth < 4 && t.Span.Length >= 2 && code[t.Span.Start] == '"' && code[t.Span.End - 1] == '"'
                    && TemplateParser.ParseQuoted(code, t.Span.Start + 1, t.Span.End - 1) is Template template)
                {
                    // "Total: { $n gt 3 ? 'many' : 'few' }": the code inside the braces.
                    foreach (TemplateSegment segment in template.Expressions.Where(s => s.Kind == TemplateSegmentKind.Interpolation))
                    {
                        Collect(code, segment.Span.Start, segment.Span.End, changes, depth + 1);
                    }
                }
            }
        }

        private static bool EndsValue(Token t) => t.Kind switch
        {
            TokenKind.Number or TokenKind.String or TokenKind.Variable or TokenKind.Regex or TokenKind.Email or TokenKind.Uuid or TokenKind.Url => true,
            TokenKind.Identifier => !Lexer.Keywords.Contains(t.Text) || t.Text is "true" or "false" or "null",
            TokenKind.Punctuation => t.Text is ")" or "]" or "}",
            _ => false,
        };

        private static bool StartsValue(Token t) => t.Kind switch
        {
            TokenKind.Number or TokenKind.String or TokenKind.Variable or TokenKind.Command or TokenKind.Regex or TokenKind.Email or TokenKind.Uuid or TokenKind.Url => true,
            TokenKind.Identifier => !Lexer.Keywords.Contains(t.Text) || t.Text is "true" or "false" or "null" or "not" or "fn" or "match",
            TokenKind.Punctuation => t.Text is "(" or "[" or "{" or "-" or "+",
            _ => false,
        };
    }
}
