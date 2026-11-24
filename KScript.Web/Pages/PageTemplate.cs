using KScript.Syntax.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KScript.Pages
{
    internal abstract class PageNode
    {
        public int Line { get; set; }
    }

    internal sealed class PageText : PageNode
    {
        public string Text { get; set; }

        /// <summary>Raw text (inside script/style, doctypes, comments) is copied as-is, with no { } interpolation.</summary>
        public bool Raw { get; set; }
    }

    internal sealed class PageAttribute
    {
        /// <summary>The name in lower case, for matching.</summary>
        public string Name { get; set; }

        /// <summary>The name as written, which is what is output: SVG needs viewBox, not viewbox.</summary>
        public string SourceName { get; set; }

        /// <summary>The attribute value, or null for a bare attribute like <c>disabled</c>.</summary>
        public string Value { get; set; }

        public char Quote { get; set; }

        /// <summary>For on-* attributes in a &lt;live&gt; block: the handler's number.</summary>
        public int HandlerIndex { get; set; } = -1;
    }

    internal sealed class PageElement : PageNode
    {
        /// <summary>The tag name in lower case, for matching.</summary>
        public string Name { get; set; }

        /// <summary>The tag name as written, which is what is output: SVG needs linearGradient, not lineargradient.</summary>
        public string SourceName { get; set; }
        public List<PageAttribute> Attributes { get; } = new List<PageAttribute>();
        public List<PageNode> Children { get; } = new List<PageNode>();
        public int Offset { get; set; }
        public bool SelfClosing { get; set; }
        public bool Void { get; set; }

        public string Attr(string name) => Attributes.FirstOrDefault(a => a.Name == name)?.Value;
        public bool HasAttr(string name) => Attributes.Any(a => a.Name == name);
    }

    /// <summary>
    /// A forgiving HTML reader for page templates. It understands void elements (br, img, meta…), self-closing
    /// tags (&lt;let … /&gt;), raw text in script and style, comments and doctypes, and closes anything left open.
    /// Unlike a browser's HTML5 parser it never moves elements around, so KScript elements such as
    /// &lt;for&gt; inside &lt;ul&gt; or &lt;table&gt; stay exactly where they were written.
    /// </summary>
    internal static class PageTemplate
    {
        public static readonly HashSet<string> VoidElements = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr",
        };

        private static readonly HashSet<string> RawTextElements = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "script", "style", "textarea", "pre",
        };

        /// <summary>KScript's control elements. Everything else is HTML (or a component) and is output.</summary>
        public static readonly HashSet<string> ControlElements = new HashSet<string>(StringComparer.Ordinal)
        {
            "let", "set", "do", "if", "elif", "else", "for", "while", "match", "case", "default",
            "layout", "prop", "slot", "markdown", "respond", "redirect", "include", "live", "state",
        };

        public static List<PageNode> Parse(string text, int firstLine = 1, bool markdown = false)
        {
            var root = new PageElement { Name = "#root" };
            var stack = new Stack<PageElement>();
            stack.Push(root);

            int i = 0, line = firstLine;
            var textBuf = new StringBuilder();
            int textLine = line;

            void FlushText()
            {
                if (textBuf.Length > 0)
                {
                    stack.Peek().Children.Add(new PageText { Text = textBuf.ToString(), Line = textLine });
                    textBuf.Clear();
                }
            }

            void Advance(int to)
            {
                for (int k = i; k < to && k < text.Length; k++) if (text[k] == '\n') line++;
                i = to;
            }

            while (i < text.Length)
            {
                char c = text[i];
                if (markdown && KScript.Syntax.Markup.MarkdownCode.EndAt(text, i) is int codeEnd && codeEnd > i)
                {
                    FlushText();
                    stack.Peek().Children.Add(new PageText { Text = text.Substring(i, codeEnd - i), Raw = true, Line = line });
                    Advance(codeEnd);
                    continue;
                }

                // A { expression } in text is kept whole, so '<' inside it (in a string, say) isn't read as a tag.
                if (c == '{' && i + 1 < text.Length && text[i + 1] != '{')
                {
                    int close = FindExpressionEnd(text, i + 1);
                    if (close > 0)
                    {
                        if (textBuf.Length == 0) textLine = line;
                        string chunk = text.Substring(i, close - i + 1);
                        textBuf.Append(chunk);
                        line += chunk.Count(ch => ch == '\n');
                        i = close + 1;
                        continue;
                    }
                }
                if (c == '{' && i + 1 < text.Length && text[i + 1] == '{')
                {
                    if (textBuf.Length == 0) textLine = line;
                    textBuf.Append("{{");
                    i += 2;
                    continue;
                }

                if (c != '<')
                {
                    if (textBuf.Length == 0) textLine = line;
                    textBuf.Append(c);
                    if (c == '\n') line++;
                    i++;
                    continue;
                }

                // Comments
                if (string.CompareOrdinal(text, i, "<!--", 0, 4) == 0)
                {
                    FlushText();
                    int end = text.IndexOf("-->", i + 4, StringComparison.Ordinal);
                    end = end < 0 ? text.Length : end + 3;
                    stack.Peek().Children.Add(new PageText { Text = text.Substring(i, end - i), Raw = true, Line = line });
                    Advance(end);
                    continue;
                }

                // Doctype, CDATA and processing instructions are copied through.
                if (i + 1 < text.Length && (text[i + 1] == '!' || text[i + 1] == '?'))
                {
                    FlushText();
                    int end = text.IndexOf('>', i);
                    end = end < 0 ? text.Length : end + 1;
                    stack.Peek().Children.Add(new PageText { Text = text.Substring(i, end - i), Raw = true, Line = line });
                    Advance(end);
                    continue;
                }

                // Closing tag
                if (i + 1 < text.Length && text[i + 1] == '/' && i + 2 < text.Length && IsNameStart(text[i + 2]))
                {
                    int j = i + 2;
                    while (j < text.Length && IsNameChar(text[j])) j++;
                    string name = text.Substring(i + 2, j - i - 2).ToLowerInvariant();
                    int gt = text.IndexOf('>', j);
                    if (gt < 0) gt = text.Length - 1;
                    FlushText();
                    Advance(gt + 1);

                    if (stack.Any(e => e.Name == name))
                    {
                        while (stack.Peek().Name != name) stack.Pop();
                        stack.Pop();
                    }
                    // A stray closing tag with nothing to close is dropped.
                    continue;
                }

                // Opening tag
                if (i + 1 < text.Length && IsNameStart(text[i + 1]))
                {
                    int startLine = line;
                    int j = i + 1;
                    while (j < text.Length && IsNameChar(text[j])) j++;
                    string tagName = text.Substring(i + 1, j - i - 1);
                    var el = new PageElement { Name = tagName.ToLowerInvariant(), SourceName = tagName, Line = startLine, Offset = i };

                    // Attributes, or a shorthand head: <if $ready> means <if test="$ready">.
                    bool closed = false;
                    string shorthand = KScript.Syntax.Markup.ShorthandCondition.AttributeFor(el.Name);
                    KScript.Syntax.Markup.ShorthandCondition.Head head = default;
                    bool scanned = shorthand != null && KScript.Syntax.Markup.ShorthandCondition.Scan(text, j, out head);
                    if (scanned)
                    {
                        el.Attributes.Add(new PageAttribute { Name = shorthand, SourceName = shorthand, Value = text.Substring(head.Start, head.End - head.Start), Quote = '"' });
                        el.SelfClosing = head.SelfClosing;
                        closed = head.Closed;
                        j = head.TagEnd;
                    }
                    while (!scanned && j < text.Length)
                    {
                        while (j < text.Length && char.IsWhiteSpace(text[j])) j++;
                        if (j >= text.Length) break;
                        if (text[j] == '>') { j++; closed = true; break; }
                        if (text[j] == '/' && j + 1 < text.Length && text[j + 1] == '>') { el.SelfClosing = true; j += 2; closed = true; break; }

                        int ns = j;
                        while (j < text.Length && !char.IsWhiteSpace(text[j]) && text[j] != '=' && text[j] != '>' && !(text[j] == '/' && j + 1 < text.Length && text[j + 1] == '>')) j++;
                        if (j == ns) { j++; continue; }
                        string attrName = text.Substring(ns, j - ns);
                        var attr = new PageAttribute { Name = attrName.ToLowerInvariant(), SourceName = attrName };
                        while (j < text.Length && char.IsWhiteSpace(text[j])) j++;
                        if (j < text.Length && text[j] == '=')
                        {
                            j++;
                            while (j < text.Length && char.IsWhiteSpace(text[j])) j++;
                            if (j < text.Length && (text[j] == '"' || text[j] == '\''))
                            {
                                char q = text[j];
                                int close = text.IndexOf(q, j + 1);
                                if (close < 0) throw new PageException($"Line {startLine}: unclosed {q} in attribute '{attr.Name}' of <{el.Name}>.");
                                attr.Value = text.Substring(j + 1, close - j - 1);
                                attr.Quote = q;
                                j = close + 1;
                            }
                            else if (j < text.Length && text[j] == '{' && (j + 1 == text.Length || text[j + 1] != '{'))
                            {
                                int from = j + 1;
                                if (from < text.Length && text[from] == '!') from++;
                                KScript.Syntax.ExpressionParser.ParseEmbedded(text, from, out int end);
                                if (end < 0) throw new PageException($"Line {startLine}: '{attr.Name}' is missing its closing brace.");
                                attr.Value = text.Substring(j, end - j + 1);
                                j = end + 1;
                            }
                            else
                            {
                                int vs = j;
                                while (j < text.Length && !char.IsWhiteSpace(text[j]) && text[j] != '>') j++;
                                attr.Value = text.Substring(vs, j - vs);
                            }
                        }
                        el.Attributes.Add(attr);
                    }

                    if (!closed)
                    {
                        // Not really a tag (e.g. "a <b" at the end of text): keep it as text.
                        if (textBuf.Length == 0) textLine = line;
                        textBuf.Append(c);
                        i++;
                        continue;
                    }

                    FlushText();
                    Advance(j);
                    CloseImpliedEnd(stack, el.Name);
                    stack.Peek().Children.Add(el);

                    if (VoidElements.Contains(el.Name) && !ControlElements.Contains(el.Name))
                    {
                        el.Void = true;
                        continue;
                    }
                    if (el.SelfClosing) continue;

                    if (RawTextElements.Contains(el.Name))
                    {
                        string closeTag = "</" + el.Name;
                        int end = text.IndexOf(closeTag, i, StringComparison.OrdinalIgnoreCase);
                        if (end < 0) end = text.Length;
                        if (end > i)
                        {
                            // <pre> keeps { } interpolation; script, style and textarea content are left alone.
                            el.Children.Add(new PageText { Text = text.Substring(i, end - i), Raw = el.Name != "pre", Line = line });
                        }
                        Advance(end);
                        int gt = text.IndexOf('>', i);
                        Advance(gt < 0 ? text.Length : gt + 1);
                        continue;
                    }

                    stack.Push(el);
                    continue;
                }

                // A '<' that doesn't start a tag, e.g. "a < b".
                if (textBuf.Length == 0) textLine = line;
                textBuf.Append(c);
                i++;
            }

            FlushText();
            DedentControlBodies(root.Children, null);
            TidyWhitespace(root.Children);
            return root.Children;
        }

        private static readonly HashSet<string> ClosesParagraph = new HashSet<string>(StringComparer.Ordinal)
        {
            "address", "article", "aside", "blockquote", "details", "dialog", "div", "dl", "fieldset", "figcaption", "figure",
            "footer", "form", "h1", "h2", "h3", "h4", "h5", "h6", "header", "hgroup", "hr", "main", "menu", "nav", "ol", "p",
            "pre", "section", "table", "ul",
        };

        /// <summary>
        /// HTML lets some end tags be left out: a new &lt;li&gt; ends the previous one, a &lt;div&gt; ends an open
        /// &lt;p&gt;, and so on. Only the element directly open is closed, so nothing is ever moved, and KScript
        /// elements (e.g. &lt;for&gt; around &lt;li&gt;) keep their place.
        /// </summary>
        private static void CloseImpliedEnd(Stack<PageElement> stack, string opening)
        {
            string open = stack.Peek().Name;
            bool close = opening switch
            {
                "li" => open == "li",
                "dt" or "dd" => open == "dt" || open == "dd",
                "option" => open == "option",
                "optgroup" => open == "option" || open == "optgroup",
                "td" or "th" => open == "td" || open == "th",
                "tr" => open == "td" || open == "th" || open == "tr",
                "thead" or "tbody" or "tfoot" => open is "td" or "th" or "tr" or "thead" or "tbody",
                _ => open == "p" && ClosesParagraph.Contains(opening),
            };
            if (!close) return;
            stack.Pop();
            // </tr> is implied too when a new row or section starts inside a cell.
            if ((opening == "tr" || opening is "thead" or "tbody" or "tfoot") && stack.Peek().Name == "tr") stack.Pop();
            // So is </optgroup> when the next group starts inside an option.
            if (opening == "optgroup" && open == "option" && stack.Peek().Name == "optgroup") stack.Pop();
            if (opening is "thead" or "tbody" or "tfoot" && stack.Peek().Name is "thead" or "tbody") stack.Pop();
        }

        /// <summary>
        /// The body of a control element written on its own lines is indented one level deeper than the
        /// element itself; remove that extra level so the output lines up with the surrounding HTML.
        /// </summary>
        private static void DedentControlBodies(List<PageNode> nodes, string _)
        {
            for (int k = 0; k < nodes.Count; k++)
            {
                if (nodes[k] is not PageElement el) continue;
                DedentControlBodies(el.Children, null);
                if (!ControlElements.Contains(el.Name) || el.Name == "slot" || el.Name == "live" || el.Name == "let" || el.Name == "set" || el.Name == "do") continue;
                if (k == 0 || nodes[k - 1] is not PageText before || before.Raw) continue;

                int lastNl = before.Text.LastIndexOf('\n');
                string tagIndent = before.Text.Substring(lastNl + 1);
                if (tagIndent.Trim().Length != 0 || lastNl < 0) continue;

                // Body must start on a new line for this to apply.
                if (el.Children.Count == 0 || el.Children[0] is not PageText first || first.Raw) continue;
                int firstNl = first.Text.IndexOf('\n');
                if (firstNl < 0 || first.Text.Substring(0, firstNl).Trim().Length != 0) continue;

                int bodyIndent = MinLineIndent(el.Children);
                int delta = bodyIndent - tagIndent.Length;
                if (delta > 0) RemoveIndent(el.Children, delta);
            }
        }

        private static IEnumerable<(PageText Text, bool BeforeElement)> Texts(List<PageNode> nodes)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                if (n is PageText t && !t.Raw) yield return (t, i + 1 < nodes.Count && nodes[i + 1] is PageElement);
                else if (n is PageElement e && !RawTextElements.Contains(e.Name))
                    foreach (var inner in Texts(e.Children)) yield return inner;
            }
        }

        private static int MinLineIndent(List<PageNode> nodes)
        {
            int min = int.MaxValue;
            foreach (var (t, beforeElement) in Texts(nodes))
            {
                var lines = t.Text.Split('\n');
                for (int i = 1; i < lines.Length; i++)
                {
                    string line = lines[i];
                    // A blank line doesn't count, except the indentation in front of an element.
                    if (line.Trim().Length == 0 && !(i == lines.Length - 1 && beforeElement)) continue;
                    min = Math.Min(min, line.Length - line.TrimStart(' ', '\t').Length);
                }
            }
            return min == int.MaxValue ? 0 : min;
        }

        private static void RemoveIndent(List<PageNode> nodes, int delta)
        {
            foreach (var (t, _) in Texts(nodes))
            {
                var lines = t.Text.Split('\n');
                for (int i = 1; i < lines.Length; i++)
                {
                    int remove = 0;
                    while (remove < delta && remove < lines[i].Length && (lines[i][remove] == ' ' || lines[i][remove] == '\t')) remove++;
                    lines[i] = lines[i].Substring(remove);
                }
                t.Text = string.Join("\n", lines);
            }
        }

        private static int FindExpressionEnd(string text, int from)
        {
            int depth = 0;
            char quote = '\0';
            for (int i = from; i < text.Length; i++)
            {
                char c = text[i];
                if (quote != '\0')
                {
                    if (Quotes.IsEscape(text, i, text.Length, quote)) i++;
                    else if (c == quote) quote = '\0';
                    else if (c == '\n') return -1;
                    continue;
                }
                if (c == '\'' || c == '"') quote = c;
                else if (c == '{') depth++;
                else if (c == '}')
                {
                    if (depth == 0) return i;
                    depth--;
                }
                else if (c == '<' && i + 1 < text.Length && (text[i + 1] == '/' || char.IsLetter(text[i + 1])) && depth == 0 && quote == '\0')
                {
                    // A tag before the closing brace: this '{' is not an expression.
                    return -1;
                }
            }
            return -1;
        }

        private static bool IsNameStart(char c) => char.IsLetter(c);
        private static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == ':' || c == '.';

        /// <summary>
        /// Control elements that sit on their own line leave no blank lines behind: the indentation before
        /// the tag and the newline after it are removed, inside and outside the element.
        /// </summary>
        private static void TidyWhitespace(List<PageNode> nodes)
        {
            // Decide which control elements start on their own line before changing any text.
            var ownLine = new bool[nodes.Count];
            for (int k = 0; k < nodes.Count; k++)
            {
                if (nodes[k] is not PageElement e || !ControlElements.Contains(e.Name) || e.Name == "slot" || e.Name == "live") continue;
                if (k == 0) { ownLine[k] = true; continue; }
                if (nodes[k - 1] is PageText prev && !prev.Raw)
                {
                    int lastNl = prev.Text.LastIndexOf('\n');
                    bool blankTail = prev.Text.Substring(lastNl + 1).Trim().Length == 0;
                    ownLine[k] = blankTail && (lastNl >= 0 || k == 1);
                }
                else if (nodes[k - 1] is PageText raw && raw.Raw)
                {
                    ownLine[k] = false;
                }
            }

            // Blank lines between control elements (a run of <let>s, say) produce no output.
            bool IsControl(int k) => k >= 0 && k < nodes.Count && nodes[k] is PageElement ce && ControlElements.Contains(ce.Name) && ce.Name != "slot" && ce.Name != "live";
            for (int k = 0; k < nodes.Count; k++)
            {
                if (nodes[k] is PageText ws && !ws.Raw && ws.Text.Trim().Length == 0 && ws.Text.Contains('\n')
                    && IsControl(k - 1) && IsControl(k + 1))
                {
                    ws.Text = string.Empty;
                    if (IsControl(k + 1)) ownLine[k + 1] = true;
                }
            }

            for (int k = 0; k < nodes.Count; k++)
            {
                if (nodes[k] is not PageElement el) continue;
                TidyWhitespace(el.Children);
                if (!ControlElements.Contains(el.Name) || el.Name == "slot" || el.Name == "live") continue;

                if (el.Children.Count > 0 && el.Children[0] is PageText first && !first.Raw)
                {
                    int nl = first.Text.IndexOf('\n');
                    if (nl >= 0 && first.Text.Substring(0, nl).Trim().Length == 0)
                        first.Text = first.Text.Substring(nl + 1);
                }
                if (el.Children.Count > 0 && el.Children[^1] is PageText last && !last.Raw)
                {
                    int nl = last.Text.LastIndexOf('\n');
                    if (nl >= 0 && last.Text.Substring(nl + 1).Trim().Length == 0)
                        last.Text = last.Text.Substring(0, nl + 1);
                }

                if (!ownLine[k]) continue;

                if (k > 0 && nodes[k - 1] is PageText before && !before.Raw)
                {
                    int lastNl = before.Text.LastIndexOf('\n');
                    before.Text = before.Text.Substring(0, lastNl + 1);
                }
                if (k + 1 < nodes.Count && nodes[k + 1] is PageText after && !after.Raw)
                {
                    string t = after.Text;
                    int nl = t.IndexOf('\n');
                    if (nl >= 0 && t.Substring(0, nl).Trim().Length == 0)
                    {
                        after.Text = t.Substring(nl + 1);
                        // The next control element on the following line is on its own line too.
                        if (k + 2 < nodes.Count && after.Text.Trim().Length == 0 && nodes[k + 2] is PageElement) ownLine[k + 2] = ownLine[k + 2] || ControlElements.Contains(((PageElement)nodes[k + 2]).Name);
                    }
                }
            }
        }
    }
}
