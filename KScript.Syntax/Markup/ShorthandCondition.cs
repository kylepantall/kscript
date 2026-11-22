namespace KScript.Syntax.Markup
{
    /// <summary>
    /// The shorthand start tag for elements that take one expression: <c>&lt;if $user.is_logged_in&gt;</c> means
    /// <c>&lt;if test="$user.is_logged_in"&gt;</c>. The expression runs from after the tag name to the '&gt;' that ends
    /// the tag, so it can use <c>&gt;</c>, <c>&lt;</c> and quotes without escaping.
    /// </summary>
    /// <remarks>
    /// A '&gt;' ends the tag unless it is part of <c>-&gt;</c>, <c>=&gt;</c> or <c>&gt;=</c>, is inside brackets or a
    /// string, or has a space on both sides with more of the expression after it on the same line (<c>$n &gt; 3</c>).
    /// If that reading runs into the next tag, the first such spaced '&gt;' ends the tag after all. Both the script
    /// and page parsers use this, so they always agree on where the tag ends.
    /// </remarks>
    public static class ShorthandCondition
    {
        /// <summary>The attribute a shorthand start tag fills in for an element, or null if it has no shorthand.</summary>
        public static string AttributeFor(string element) => element switch
        {
            "if" or "elif" or "while" => "test",
            "match" => "value",
            _ => null,
        };

        /// <summary>The elements that have a shorthand start tag.</summary>
        public static IReadOnlyList<string> Elements { get; } = new[] { "if", "elif", "while", "match" };

        /// <summary>The result of <see cref="Scan"/>.</summary>
        public readonly struct Head
        {
            public Head(int start, int end, int tagEnd, bool closed, bool selfClosing)
            {
                Start = start;
                End = end;
                TagEnd = tagEnd;
                Closed = closed;
                SelfClosing = selfClosing;
            }

            /// <summary>Where the expression starts (after the spaces that follow the tag name).</summary>
            public int Start { get; }

            /// <summary>Where the expression ends, with trailing spaces left out.</summary>
            public int End { get; }

            /// <summary>Just after the tag's '&gt;' (or '/&gt;'), or where reading stopped if the tag isn't closed.</summary>
            public int TagEnd { get; }

            public bool Closed { get; }

            public bool SelfClosing { get; }
        }

        /// <summary>
        /// Reads a shorthand head starting just after the tag name. Returns false when the tag is written with
        /// attributes (<c>test="..."</c>) or has nothing in it, so the caller reads it as usual.
        /// </summary>
        public static bool Scan(string text, int afterName, out Head head)
        {
            head = default;
            int start = afterName;
            while (start < text.Length && char.IsWhiteSpace(text[start]))
            {
                start++;
            }
            if (start == afterName || start >= text.Length || text[start] == '>' || (text[start] == '/' && At(text, start + 1) == '>')
                || LooksLikeAttribute(text, start))
            {
                return false;
            }

            int depth = 0;
            int spaced = -1;
            int i = start;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '\'' || c == '"')
                {
                    int close = text.IndexOf(c, i + 1);
                    i = close < 0 ? text.Length : close + 1;
                    continue;
                }
                if (c == '(' || c == '[' || c == '{')
                {
                    depth++;
                }
                else if ((c == ')' || c == ']' || c == '}') && depth > 0)
                {
                    depth--;
                }
                else if (depth == 0 && c == '>')
                {
                    char before = text[i - 1];
                    char after = At(text, i + 1);
                    if (before == '-' || before == '=' || after == '=')
                    {
                        i++;
                        continue;
                    }
                    if (before == '/')
                    {
                        head = Make(text, start, i - 1, i + 1, closed: true, selfClosing: true);
                        return true;
                    }
                    if (IsSpace(before) && IsSpace(after) && OperandFollows(text, i + 1))
                    {
                        spaced = spaced < 0 ? i : spaced;
                        i++;
                        continue;
                    }
                    head = Make(text, start, i, i + 1, closed: true, selfClosing: false);
                    return true;
                }
                else if (depth == 0 && c == '<' && StartsTag(text, i))
                {
                    break;
                }
                i++;
            }

            head = spaced >= 0
                ? Make(text, start, spaced, spaced + 1, closed: true, selfClosing: false)
                : Make(text, start, i, i, closed: false, selfClosing: false);
            return true;
        }

        /// <summary>
        /// True when <paramref name="expression"/> can be written as a shorthand head and read back unchanged, which is
        /// what <c>kscript convert</c> checks before writing <c>&lt;if expression&gt;</c> instead of <c>test="..."</c>.
        /// </summary>
        public static bool CanWrite(string expression)
        {
            string e = expression?.Trim();
            if (string.IsNullOrEmpty(e) || e.Contains('&') || e.Contains('\n') || e.Contains('\r'))
            {
                return false;
            }
            string tag = "<if " + e + ">";
            return Scan(tag, 3, out Head head) && head.Closed && !head.SelfClosing && head.TagEnd == tag.Length
                && head.End - head.Start == e.Length;
        }

        private static Head Make(string text, int start, int end, int tagEnd, bool closed, bool selfClosing)
        {
            while (end > start && char.IsWhiteSpace(text[end - 1]))
            {
                end--;
            }
            return new Head(start, end, tagEnd, closed, selfClosing);
        }

        /// <summary>True for <c>name=</c> (an attribute), but not <c>name ==</c> or <c>name =&gt;</c>.</summary>
        private static bool LooksLikeAttribute(string text, int i)
        {
            if (!(char.IsLetter(text[i]) || text[i] == '_' || text[i] == ':'))
            {
                return false;
            }
            while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_' || text[i] == ':' || text[i] == '-' || text[i] == '.'))
            {
                i++;
            }
            while (i < text.Length && char.IsWhiteSpace(text[i]))
            {
                i++;
            }
            return At(text, i) == '=' && At(text, i + 1) != '=' && At(text, i + 1) != '>';
        }

        /// <summary>True when the rest of the line after a '&gt;' holds more expression rather than the next tag.</summary>
        private static bool OperandFollows(string text, int i)
        {
            while (i < text.Length && IsSpace(text[i]))
            {
                i++;
            }
            return i < text.Length && text[i] != '\n' && text[i] != '\r' && text[i] != '<';
        }

        private static bool StartsTag(string text, int i)
        {
            char next = At(text, i + 1);
            return char.IsLetter(next) || next == '_' || next == '/' || next == '!';
        }

        private static bool IsSpace(char c) => c == ' ' || c == '\t';

        private static char At(string text, int i) => i < text.Length ? text[i] : '\0';
    }
}
