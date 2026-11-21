using System.Text;

using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Language.Formatting
{
    /// <summary>
    /// Decides whether two parses of a <c>.kscript</c> file mean the same: the same elements, in the same order, with
    /// the same attributes, text and comments. Expressions may differ only in whitespace that can't matter: spaces
    /// next to punctuation, and how many spaces or line breaks separate two words. Text in quotes must match exactly.
    /// Blank lines are layout, not meaning.
    /// </summary>
    public static class CodeMeaning
    {
        public static bool Same(MarkupDocument a, MarkupDocument b, out string difference)
        {
            difference = null;
            if (b.Diagnostics.Any(d => d.Severity == Syntax.DiagnosticSeverity.Error) && !a.Diagnostics.Any(d => d.Severity == Syntax.DiagnosticSeverity.Error))
            {
                var d = b.Diagnostics.First(x => x.Severity == Syntax.DiagnosticSeverity.Error);
                difference = "the result has an error: " + d.Message;
                return false;
            }
            return SameNodes(a.Children, b.Children, a, ref difference);
        }

        private static List<MarkupNode> Significant(List<MarkupNode> nodes) =>
            nodes.Where(n => !(n is MarkupText t && t.IsWhitespace && !t.IsCData)).ToList();

        private static bool SameNodes(List<MarkupNode> left, List<MarkupNode> right, MarkupDocument source, ref string difference)
        {
            List<MarkupNode> a = Significant(left), b = Significant(right);
            for (int i = 0; i < Math.Max(a.Count, b.Count); i++)
            {
                if (i >= a.Count || i >= b.Count)
                {
                    difference = "a statement was added or removed near " + Where(source, (i < a.Count ? a[i] : a.LastOrDefault())?.Span.Start ?? 0);
                    return false;
                }
                if (!SameNode(a[i], b[i], source, ref difference)) return false;
            }
            return true;
        }

        private static bool SameNode(MarkupNode a, MarkupNode b, MarkupDocument source, ref string difference)
        {
            string at = Where(source, a.Span.Start);
            switch (a)
            {
                case MarkupElement ea when b is MarkupElement eb:
                    if (ea.Name != eb.Name)
                    {
                        difference = $"<{ea.Name}> became <{eb.Name}> at {at}";
                        return false;
                    }
                    if (ea.Attributes.Count != eb.Attributes.Count)
                    {
                        difference = $"the attributes of {ea.Name} changed at {at}";
                        return false;
                    }
                    for (int i = 0; i < ea.Attributes.Count; i++)
                    {
                        MarkupAttribute x = ea.Attributes[i], y = eb.Attributes[i];
                        if (x.Name != y.Name || Normalise(x.ValueText) != Normalise(y.ValueText))
                        {
                            difference = $"{ea.Name} {x.Name} changed at {at}";
                            return false;
                        }
                    }
                    return SameNodes(ea.Children, eb.Children, source, ref difference);
                case MarkupText ta when b is MarkupText tb:
                    // Quoted "text" must match exactly; unquoted text is an expression (x = 1 becomes <do>x = 1</do>).
                    bool quoted = IsQuoted(source, ta);
                    if (quoted ? Lines(ta.Value) != Lines(tb.Value) : Normalise(ta.Value) != Normalise(tb.Value))
                    {
                        difference = "text changed at " + at;
                        return false;
                    }
                    return true;
                case MarkupComment ca when b is MarkupComment cb:
                    if (Lines(ca.Text) != Lines(cb.Text))
                    {
                        difference = "a comment changed at " + at;
                        return false;
                    }
                    return true;
                default:
                    difference = "a statement changed kind at " + at;
                    return false;
            }
        }

        private static bool IsQuoted(MarkupDocument d, MarkupText t)
        {
            int start = t.Content?.SourceSpan.Start ?? t.Span.Start;
            string text = d.Source.Text;
            return (start > 0 && start <= text.Length && text[start - 1] == '"') || (t.Span.Start < text.Length && text[t.Span.Start] is '"' or '`');
        }

        private static string Where(MarkupDocument d, int offset) => "line " + (d.Source.GetPosition(Math.Min(offset, d.Source.Length)).Line + 1);

        private static string Lines(string s) => s?.Replace("\r\n", "\n");

        /// <summary>
        /// An expression with the whitespace that can't matter taken out: quoted strings are kept, whitespace next to
        /// anything but a word character is dropped, and whitespace between two words becomes one space.
        /// </summary>
        internal static string Normalise(string value)
        {
            if (value == null) return null;
            StringBuilder sb = new StringBuilder();
            int i = 0;
            bool pendingSpace = false;
            while (i < value.Length)
            {
                char c = value[i];
                if (c == '\'' || c == '"')
                {
                    if (pendingSpace && sb.Length > 0 && IsWord(sb[sb.Length - 1])) sb.Append(' ');
                    pendingSpace = false;
                    int start = i++;
                    while (i < value.Length && value[i] != c)
                    {
                        if (value[i] == '\\') i++;
                        i++;
                    }
                    i = Math.Min(value.Length, i + 1);
                    // A \ at the end of a line joins it to the next, so where a string is broken doesn't matter.
                    sb.Append(LineJoins.Join(value.Substring(start, i - start)));
                    continue;
                }
                if (c == '`' && StringBlock.Read(value, i) is StringBlock block)
                {
                    // A ``` block means its value: its lines can move as long as they keep their indentation inside it.
                    if (pendingSpace && sb.Length > 0 && IsWord(sb[sb.Length - 1])) sb.Append(' ');
                    pendingSpace = false;
                    sb.Append("```").Append(block.Tag).Append('\n').Append(block.Value).Append(block.Error != null ? "\n!" + block.Error : string.Empty).Append("```");
                    i = block.End;
                    continue;
                }
                if (char.IsWhiteSpace(c))
                {
                    pendingSpace = true;
                    i++;
                    continue;
                }
                if (pendingSpace && sb.Length > 0 && IsWord(sb[sb.Length - 1]) && IsWord(c)) sb.Append(' ');
                pendingSpace = false;
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        private static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$' || c == '@';
    }
}
