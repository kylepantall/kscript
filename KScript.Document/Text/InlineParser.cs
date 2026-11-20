using System.Text;

using KScript.Documents.Maths;

namespace KScript.Documents.Text
{
    /// <summary>
    /// Reads the text of a paragraph, heading, caption or cell into inlines. It is Markdown-like:
    /// <c>**bold**</c>, <c>*italic*</c>, <c>`code`</c>, <c>[text](url)</c>, <c>\( maths \)</c>, <c>[@key]</c> citations,
    /// <c>[#id]</c> cross-references and <c>^[a footnote]</c>. A backslash before punctuation keeps it as written.
    /// </summary>
    public static class InlineParser
    {
        /// <param name="dollarMaths">Also read $maths$, as Markdown files do. Script text uses \( \), since $ starts a variable there.</param>
        public static List<Inline> Parse(string text, bool keepLines = false, bool dollarMaths = false)
        {
            var parser = new Parser(text ?? string.Empty, keepLines, dollarMaths);
            List<Inline> result = parser.ParseUntil(null);
            return Merge(result);
        }

        /// <summary>The text of inlines without formatting, for bookmarks, alt text and sorting.</summary>
        public static string Plain(IEnumerable<Inline> inlines)
        {
            var sb = new StringBuilder();
            foreach (Inline i in inlines ?? Enumerable.Empty<Inline>())
            {
                switch (i)
                {
                    case TextInline t:
                        sb.Append(t.Text);
                        break;
                    case StrongInline s:
                        sb.Append(Plain(s.Children));
                        break;
                    case EmphasisInline e:
                        sb.Append(Plain(e.Children));
                        break;
                    case CodeInline c:
                        sb.Append(c.Text);
                        break;
                    case LinkInline l:
                        sb.Append(Plain(l.Children));
                        break;
                    case MathInline m:
                        sb.Append(MathPlain.Write(m.Node));
                        break;
                    case CiteInline ci:
                        sb.Append(ci.Text ?? string.Join("; ", ci.Keys));
                        break;
                    case RefInline r:
                        sb.Append(r.Text ?? r.Id);
                        break;
                    case LineBreakInline:
                        sb.Append(' ');
                        break;
                }
            }
            return sb.ToString();
        }

        /// <summary>Joins neighbouring text runs.</summary>
        private static List<Inline> Merge(List<Inline> inlines)
        {
            var result = new List<Inline>();
            foreach (Inline i in inlines)
            {
                if (i is TextInline t && result.Count > 0 && result[^1] is TextInline last)
                {
                    last.Text += t.Text;
                    continue;
                }
                result.Add(i);
            }
            return result;
        }

        private sealed class Parser
        {
            private readonly string text;
            private readonly bool keepLines;
            private readonly bool dollarMaths;
            private int pos;
            private readonly StringBuilder literal = new StringBuilder();

            public Parser(string text, bool keepLines, bool dollarMaths)
            {
                this.text = text.Replace("\r\n", "\n");
                this.keepLines = keepLines;
                this.dollarMaths = dollarMaths;
            }

            private char At(int i) => i >= 0 && i < text.Length ? text[i] : '\0';

            private bool Starts(string s) => string.CompareOrdinal(text, pos, s, 0, s.Length) == 0;

            private void Flush(List<Inline> into)
            {
                if (literal.Length == 0) return;
                into.Add(new TextInline(literal.ToString()));
                literal.Clear();
            }

            /// <summary>Reads inlines until <paramref name="close"/> (or the end). Returns null if close never comes.</summary>
            public List<Inline> ParseUntil(string close)
            {
                var items = new List<Inline>();
                while (pos < text.Length)
                {
                    if (close != null && Starts(close) && (close != "_" || !char.IsLetterOrDigit(At(pos + 1))) && (close != "*" || At(pos + 1) != '*' || Starts("***")))
                    {
                        Flush(items);
                        pos += close.Length;
                        return items;
                    }
                    if (!Step(items)) literal.Append(text[pos++]);
                }
                if (close != null) return null;
                Flush(items);
                return items;
            }

            /// <summary>The index of the ] matching the [ at <paramref name="open"/>, or -1.</summary>
            private int Bracket(int open)
            {
                int depth = 0;
                for (int i = open; i < text.Length; i++)
                {
                    if (text[i] == '\\')
                    {
                        i++;
                        continue;
                    }
                    if (text[i] == '[') depth++;
                    else if (text[i] == ']' && --depth == 0) return i;
                }
                return -1;
            }

            private bool Step(List<Inline> items)
            {
                char c = text[pos];
                switch (c)
                {
                    case '\\':
                        return Backslash(items);
                    case '\n':
                        Flush(items);
                        if (keepLines) items.Add(new LineBreakInline());
                        else if (literal.Length == 0 && (items.Count == 0 || !(items[^1] is TextInline t && t.Text.EndsWith(" ", StringComparison.Ordinal)))) literal.Append(' ');
                        pos++;
                        return true;
                    case '`':
                    {
                        int end = text.IndexOf('`', pos + 1);
                        if (end < 0) return false;
                        Flush(items);
                        items.Add(new CodeInline(text.Substring(pos + 1, end - pos - 1)));
                        pos = end + 1;
                        return true;
                    }
                    case '*':
                    case '_':
                        return Emphasis(items, c);
                    case '$' when dollarMaths && At(pos + 1) != ' ':
                    {
                        int end = text.IndexOf('$', pos + 1);
                        if (end < 0 || end == pos + 1) return false;
                        Flush(items);
                        string latex = text.Substring(pos + 1, end - pos - 1);
                        items.Add(new MathInline(latex.Trim(), MathParser.Parse(latex)));
                        pos = end + 1;
                        return true;
                    }
                    case '^' when At(pos + 1) == '[':
                    {
                        int end = Bracket(pos + 1);
                        if (end < 0) return false;
                        Flush(items);
                        var note = new FootnoteInline();
                        note.Children.AddRange(Merge(Parse(text.Substring(pos + 2, end - pos - 2), false, dollarMaths)));
                        items.Add(note);
                        pos = end + 1;
                        return true;
                    }
                    case '[':
                        return Bracketed(items);
                    case '-' when Starts("---"):
                        literal.Append('—');
                        pos += 3;
                        return true;
                    case '-' when Starts("--"):
                        literal.Append('–');
                        pos += 2;
                        return true;
                }
                return false;
            }

            private bool Backslash(List<Inline> items)
            {
                char next = At(pos + 1);
                if (next == '(' || next == '[')
                {
                    string close = next == '(' ? "\\)" : "\\]";
                    int end = text.IndexOf(close, pos + 2, StringComparison.Ordinal);
                    if (end < 0) throw new KScript.Runtime.KScriptException($"\\{next} starts some maths that isn't closed with {close}.", "Math");
                    string latex = text.Substring(pos + 2, end - pos - 2);
                    Flush(items);
                    items.Add(new MathInline(latex.Trim(), MathParser.Parse(latex)));
                    pos = end + 2;
                    return true;
                }
                if (next != '\0' && (char.IsPunctuation(next) || char.IsSymbol(next)))
                {
                    literal.Append(next);
                    pos += 2;
                    return true;
                }
                return false;
            }

            private bool Emphasis(List<Inline> items, char c)
            {
                bool strong = At(pos + 1) == c;
                string mark = strong ? new string(c, 2) : c.ToString();
                // _ only marks emphasis at the start of a word, so snake_case_names stay as they are.
                if (c == '_' && char.IsLetterOrDigit(At(pos - 1))) return false;
                if (char.IsWhiteSpace(At(pos + mark.Length)) || pos + mark.Length >= text.Length) return false;
                int start = pos;
                pos += mark.Length;
                var saved = new StringBuilder(literal.ToString());
                literal.Clear();
                List<Inline> inner = ParseUntil(mark);
                if (inner == null || inner.Count == 0)
                {
                    // Not closed: the mark is just a character.
                    pos = start;
                    literal.Clear();
                    literal.Append(saved);
                    return false;
                }
                literal.Append(saved);
                Flush(items);
                Inline node;
                if (strong)
                {
                    var s = new StrongInline();
                    s.Children.AddRange(Merge(inner));
                    node = s;
                }
                else
                {
                    var e = new EmphasisInline();
                    e.Children.AddRange(Merge(inner));
                    node = e;
                }
                items.Add(node);
                return true;
            }

            private bool Bracketed(List<Inline> items)
            {
                int end = Bracket(pos);
                if (end < 0) return false;
                string inside = text.Substring(pos + 1, end - pos - 1);
                string trimmed = inside.Trim();
                if (trimmed.StartsWith("@", StringComparison.Ordinal))
                {
                    var cite = new CiteInline();
                    foreach (string part in trimmed.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string key = part.Trim();
                        if (!key.StartsWith("@", StringComparison.Ordinal) || key.Length < 2) return false;
                        cite.Keys.Add(key.Substring(1));
                    }
                    Flush(items);
                    items.Add(cite);
                    pos = end + 1;
                    return true;
                }
                if (trimmed.StartsWith("#", StringComparison.Ordinal) && trimmed.Length > 1 && trimmed.Skip(1).All(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch == ':' || ch == '.'))
                {
                    Flush(items);
                    items.Add(new RefInline(trimmed.Substring(1)));
                    pos = end + 1;
                    return true;
                }
                if (At(end + 1) == '(')
                {
                    int close = text.IndexOf(')', end + 2);
                    if (close < 0) return false;
                    Flush(items);
                    var link = new LinkInline { Url = text.Substring(end + 2, close - end - 2).Trim() };
                    link.Children.AddRange(Merge(Parse(inside, false, dollarMaths)));
                    items.Add(link);
                    pos = close + 1;
                    return true;
                }
                return false;
            }
        }
    }
}
