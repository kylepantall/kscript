using System.Text;

using KScript.Language.Catalog;
using KScript.Syntax;
using KScript.Metadata;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Language.Formatting
{
    /// <summary>The outcome of formatting one document.</summary>
    public sealed class FormatResult
    {
        /// <summary>The formatted text, or null when the document could not be formatted.</summary>
        public string Text { get; init; }

        /// <summary>Why the document was left alone (a syntax error, or a Markdown page), when <see cref="Text"/> is null.</summary>
        public string Error { get; init; }

        /// <summary>Where the syntax error is, when there is one.</summary>
        public TextSpan? ErrorSpan { get; init; }

        public bool Succeeded => Text != null;
    }

    /// <summary>
    /// Tidies KScript scripts (<c>.ks.xml</c>, <c>.ks</c>, <c>.xml</c>) and <c>kscript serve</c> HTML pages.
    /// <para>
    /// The formatter only changes whitespace that cannot change what a file does: it re-indents elements by
    /// nesting, puts each statement on its own line, normalises start tags (<c>&lt;x a="1"/&gt;</c> becomes
    /// <c>&lt;x a="1" /&gt;</c>, long ones get one attribute per line), trims trailing spaces, collapses runs of blank
    /// lines and ends the file with one newline.
    /// </para>
    /// <para>
    /// Text is never rewrapped. Multi-line text keeps its own layout and is only moved as a block to the new indent,
    /// which is safe because KScript removes shared indentation from element text. Attribute values, comments'
    /// contents, CDATA sections and the content of &lt;pre&gt;, &lt;textarea&gt;, &lt;script&gt; and &lt;style&gt; (plus any
    /// element in <see cref="FormatOptions.Preserve"/>) are kept exactly. In pages, a line break is only added where
    /// there was already whitespace or next to a block-level element, so the rendered page doesn't change.
    /// Documents with markup errors are not formatted, and Markdown pages are left alone.
    /// </para>
    /// </summary>
    public sealed class Formatter
    {
        private static readonly HashSet<string> RawElements = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pre", "textarea", "script", "style",
        };

        private static readonly HashSet<string> VoidElements = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "track", "wbr",
        };

        /// <summary>HTML elements around which whitespace never shows, so a line break can always be added.</summary>
        private static readonly HashSet<string> BlockElements = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "html", "head", "body", "title", "meta", "link", "base", "script", "style", "noscript", "template",
            "address", "article", "aside", "blockquote", "details", "dialog", "dd", "div", "dl", "dt", "fieldset",
            "figcaption", "figure", "footer", "form", "h1", "h2", "h3", "h4", "h5", "h6", "header", "hgroup", "hr",
            "li", "main", "nav", "ol", "p", "pre", "section", "summary", "table", "caption", "colgroup", "col",
            "thead", "tbody", "tfoot", "tr", "td", "th", "ul", "menu", "option", "optgroup", "select", "datalist",
            "picture", "source", "video", "audio", "track", "canvas", "svg", "iframe", "object", "map", "area",
            // KScript page structure: these produce no markup of their own at a block boundary.
            "layout", "slot", "import", "live",
        };

        private readonly string text;
        private readonly MarkupDocument document;
        private readonly FormatOptions options;
        private readonly bool html;
        private readonly string eol;
        private readonly StringBuilder sb = new StringBuilder();

        /// <summary>Spans whose lines are copied exactly (raw elements, CDATA, attribute values).</summary>
        private readonly List<TextSpan> protectedSpans = new List<TextSpan>();

        /// <summary>Output before this point was copied exactly (trailing spaces included) and is never trimmed.</summary>
        private int keptUpTo;

        /// <summary>The indent (in columns) of the output line being written.</summary>
        private int lineIndent;

        private Formatter(MarkupDocument document, FormatOptions options)
        {
            this.document = document;
            this.options = options;
            text = document.Source.Text;
            html = document.Kind != DocumentKind.Script;
            eol = options.EndOfLine switch
            {
                EndOfLine.Lf => "\n",
                EndOfLine.CrLf => "\r\n",
                _ => DominantEndOfLine(text),
            };
        }

        /// <summary>Formats a document, choosing how to parse it from its path (as the analyzer does).</summary>
        public static FormatResult Format(string text, string path, FormatOptions options = null) =>
            Format(text, MarkupParser.KindFromPath(path), options);

        public static FormatResult Format(string text, DocumentKind kind, FormatOptions options = null)
        {
            options ??= FormatOptions.Default;
            if (kind == DocumentKind.Markdown)
            {
                return new FormatResult { Error = "Markdown pages are not reformatted." };
            }

            text ??= string.Empty;
            // A byte order mark is kept as it was and not seen by the parser.
            string bom = text.Length > 0 && text[0] == '\uFEFF' ? "\uFEFF" : string.Empty;
            MarkupDocument document = MarkupParser.Parse(text.Substring(bom.Length), kind);
            Diagnostic error = document.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
            if (error != null)
            {
                LinePosition at = document.Source.GetPosition(error.Span.Start);
                return new FormatResult
                {
                    Error = $"not formatted: line {at.Line + 1}: {error.Message}",
                    ErrorSpan = new TextSpan(error.Span.Start + bom.Length, error.Span.Length),
                };
            }

            Formatter f = new Formatter(document, options);
            f.Run();
            return new FormatResult { Text = bom + f.sb.ToString() };
        }

        #region Driver

        private void Run()
        {
            CollectProtectedSpans();

            List<Item> items = Items(document.Children, 0, text.Length);
            if (items.Count == 0)
            {
                return;
            }

            bool textMode = document.Children.OfType<MarkupText>().Any(t => !t.IsWhitespace || t.IsCData);
            if (textMode)
            {
                // Text at the top level (rare: most pages start with tags): keep it, tidy only the whitespace.
                int start = items[0].Start;
                int end = items[items.Count - 1].End;
                lineIndent = 0;
                AppendShifted(start, end, old => old, keepTrailing: false);
            }
            else
            {
                for (int i = 0; i < items.Count; i++)
                {
                    if (i > 0)
                    {
                        string sep = text.Substring(items[i - 1].End, items[i].Start - items[i - 1].End);
                        if (!CanBreak(false, sep, IsBlock(items[i - 1]), IsBlock(items[i])))
                        {
                            EmitItem(items[i], 0);
                            continue;
                        }
                        NewLineAt(IndentAt(0, items[i].Start, 0), BlankLines(sep));
                    }
                    EmitItem(items[i], 0);
                }
            }

            TrimTrailingWhitespace();
            if (options.FinalNewline)
            {
                sb.Append(eol);
            }
            else
            {
                // Keep the file's own ending: the whitespace after the last node, with blank lines tidied like any others.
                int last = items[items.Count - 1].End;
                int newlines = text.Substring(last).Count(c => c == '\n');
                for (int i = 0; i < Math.Min(newlines, MaxBlank + 1); i++) sb.Append(eol);
            }
        }

        #endregion

        #region Items

        /// <summary>A child worth laying out: an element, a comment, text with content, or source the parser skipped (a declaration).</summary>
        private sealed class Item
        {
            public MarkupNode Node;
            public int Start;
            public int End;

            /// <summary>Copied exactly: a region between kscript-fmt: off and on, or the node after kscript-fmt-ignore.</summary>
            public bool Verbatim;
        }

        /// <summary>The comments that switch formatting off for part of a file.</summary>
        public const string OffDirective = "kscript-fmt: off";
        public const string OnDirective = "kscript-fmt: on";
        public const string IgnoreDirective = "kscript-fmt-ignore";

        private string Directive(Item item)
        {
            if (!(item.Node is MarkupComment c)) return null;
            string body = text.Substring(c.Span.Start, c.Span.Length);
            if (body.StartsWith("<!--", StringComparison.Ordinal)) body = body.Substring(4);
            if (body.EndsWith("-->", StringComparison.Ordinal)) body = body.Substring(0, body.Length - 3);
            body = string.Join(" ", body.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
            if (string.Equals(body, OffDirective, StringComparison.OrdinalIgnoreCase) || string.Equals(body, "kscript-fmt:off", StringComparison.OrdinalIgnoreCase)) return OffDirective;
            if (string.Equals(body, OnDirective, StringComparison.OrdinalIgnoreCase) || string.Equals(body, "kscript-fmt:on", StringComparison.OrdinalIgnoreCase)) return OnDirective;
            if (body.StartsWith(IgnoreDirective, StringComparison.OrdinalIgnoreCase)) return IgnoreDirective;
            return null;
        }

        /// <summary>
        /// Applies the ignore comments among a container's children: everything from &lt;!-- kscript-fmt: off --&gt; to
        /// &lt;!-- kscript-fmt: on --&gt; (or the end of the container) becomes one item copied exactly, and so does the
        /// node right after &lt;!-- kscript-fmt-ignore --&gt;.
        /// </summary>
        private List<Item> ApplyDirectives(List<Item> items)
        {
            List<Item> result = new List<Item>();
            for (int i = 0; i < items.Count; i++)
            {
                string directive = Directive(items[i]);
                if (directive == OffDirective)
                {
                    int j = i + 1;
                    while (j < items.Count && Directive(items[j]) != OnDirective) j++;
                    int last = Math.Min(j, items.Count - 1);
                    Item region = new Item { Node = items[i].Node, Start = items[i].Start, End = items[last].End, Verbatim = true };
                    protectedSpans.Add(TextSpan.FromBounds(region.Start, region.End));
                    result.Add(region);
                    i = last;
                    continue;
                }
                result.Add(items[i]);
                if (directive == IgnoreDirective && i + 1 < items.Count)
                {
                    Item next = items[++i];
                    next.Verbatim = true;
                    protectedSpans.Add(TextSpan.FromBounds(next.Start, next.End));
                    result.Add(next);
                }
            }
            return result;
        }

        /// <summary>The children of a container, with whitespace-only text dropped and skipped source (such as &lt;!doctype&gt;) kept.</summary>
        private List<Item> Items(List<MarkupNode> children, int contentStart, int contentEnd)
        {
            List<Item> items = new List<Item>();
            int pos = contentStart;
            foreach (MarkupNode child in children)
            {
                AddGap(items, pos, child.Span.Start);
                if (!(child is MarkupText t && t.IsWhitespace && !t.IsCData))
                {
                    items.Add(new Item { Node = child, Start = child.Span.Start, End = EffectiveEnd(child) });
                }
                pos = child.Span.End;
            }
            AddGap(items, pos, contentEnd);
            return ApplyDirectives(items);
        }

        private void AddGap(List<Item> items, int start, int end)
        {
            if (end <= start)
            {
                return;
            }
            // Skipped source is a declaration, a processing instruction or a stray end tag, surrounded by whitespace.
            int s = start, e = end;
            while (s < e && char.IsWhiteSpace(text[s])) s++;
            while (e > s && char.IsWhiteSpace(text[e - 1])) e--;
            if (e > s)
            {
                items.Add(new Item { Node = null, Start = s, End = e });
            }
        }

        /// <summary>Where a node really ends: an element closed implicitly (an HTML &lt;li&gt; without &lt;/li&gt;) doesn't own the whitespace after its content.</summary>
        private int EffectiveEnd(MarkupNode node)
        {
            int end = node.Span.End;
            if (node is MarkupElement e && !HasEndTag(e) && !e.SelfClosing && e.StartTagClosed && !IsVoid(e))
            {
                while (end > e.StartTagSpan.End && char.IsWhiteSpace(text[end - 1])) end--;
            }
            return end;
        }

        private static bool HasEndTag(MarkupElement e) => e.EndNameSpan != null;

        private bool IsVoid(MarkupElement e) => html && VoidElements.Contains(e.Name);

        /// <summary>In a page: a block-level element (or a declaration). In a script every element is a statement, so this is always true.</summary>
        private bool IsBlock(Item item) => !html || (item.Node is MarkupElement e ? BlockElements.Contains(e.Name) : item.Node == null);

        /// <summary>
        /// True when the element's content is flowing text (an HTML inline element, or a KScript element whose content
        /// is a template or markup, like &lt;echo&gt;), where whitespace between children can show in the output.
        /// </summary>
        private bool IsFlow(MarkupElement e)
        {
            if (html)
            {
                return !BlockElements.Contains(e.Name) && !IsKScriptScript(e);
            }
            ContentKind? content = Catalog.GetElement(e.Name)?.Content;
            return content == ContentKind.Template || content == ContentKind.Markup;
        }

        /// <summary>
        /// Whether a line break may go between two neighbours. In flowing content only existing whitespace may become a
        /// line break; elsewhere a break is fine when there was whitespace or one side is a block (whitespace next to a
        /// block never shows).
        /// </summary>
        private bool CanBreak(bool flow, string separator, bool before, bool after)
        {
            if (separator.Length > 0) return true;
            if (flow || !options.StatementPerLine) return false;
            return before || after;
        }

        private LanguageCatalog Catalog => LanguageCatalog.Default;

        private bool IsRaw(MarkupElement e) =>
            options.Preserve.Contains(e.Name)
            || (html && RawElements.Contains(e.Name) && !IsKScriptScript(e));

        private static bool IsKScriptScript(MarkupElement e) =>
            string.Equals(e.Name, "script", StringComparison.OrdinalIgnoreCase)
            && string.Equals(e.GetAttributeValue("type")?.Trim(), "text/kscript", StringComparison.OrdinalIgnoreCase);

        private void CollectProtectedSpans()
        {
            foreach (MarkupNode node in document.DescendantNodes())
            {
                switch (node)
                {
                    case MarkupElement e when IsRaw(e):
                        protectedSpans.Add(e.Span);
                        break;
                    case MarkupText t when t.IsCData:
                        protectedSpans.Add(t.Span);
                        break;
                }
                if (node is MarkupElement el)
                {
                    foreach (MarkupAttribute a in el.Attributes)
                    {
                        if (a.Value != null)
                        {
                            protectedSpans.Add(a.Value.SourceSpan);
                        }
                    }
                }
            }
        }

        /// <summary>True when a line starting at this offset begins inside text that must be copied exactly.</summary>
        private bool IsProtected(int offset) => protectedSpans.Any(s => offset > s.Start && offset < s.End);

        #endregion

        #region Emitting

        private void EmitItem(Item item, int depth)
        {
            if (item.Verbatim)
            {
                AppendShifted(item.Start, item.End, old => old, keepTrailing: true);
                return;
            }
            switch (item.Node)
            {
                case MarkupElement e:
                    EmitElement(e, depth);
                    break;
                default:
                    // Comments, top-level text and declarations: copied, with their later lines moved along with the first.
                    int delta = lineIndent - OldLineIndent(item.Start);
                    AppendShifted(item.Start, item.End, old => old + delta, keepTrailing: false);
                    break;
            }
        }

        private void EmitElement(MarkupElement e, int depth)
        {
            if (IsRaw(e))
            {
                // Kept exactly; only the line it starts on moves.
                AppendShifted(e.Span.Start, EffectiveEnd(e), old => old, keepTrailing: true);
                return;
            }

            int own = lineIndent;
            AppendStartTag(e, depth);
            if (e.SelfClosing || !e.StartTagClosed || IsVoid(e))
            {
                return;
            }

            bool endTag = HasEndTag(e);
            int contentStart = e.ContentSpan.Start;
            int contentEnd = endTag ? e.ContentSpan.End : EffectiveEnd(e);
            List<Item> items = Items(e.Children, contentStart, contentEnd);

            bool textMode = e.Children.OfType<MarkupText>().Any(t => !t.IsWhitespace || t.IsCData);
            if (textMode)
            {
                EmitText(e, depth, contentStart, contentEnd);
            }
            else if (html && items.All(i => !IsBlock(i)) && text.IndexOf('\n', contentStart, contentEnd - contentStart) < 0)
            {
                // Inline elements written on one line (a row of links, say) stay as they are.
                sb.Append(text, contentStart, contentEnd - contentStart);
            }
            else if (items.Count > 0)
            {
                bool flow = IsFlow(e);
                for (int i = 0; i < items.Count; i++)
                {
                    int prevEnd = i == 0 ? contentStart : items[i - 1].End;
                    string sep = text.Substring(prevEnd, items[i].Start - prevEnd);
                    bool sideBlock = i == 0 ? !flow : IsBlock(items[i - 1]);
                    if (!CanBreak(flow, sep, sideBlock, IsBlock(items[i])))
                    {
                        EmitItem(items[i], depth + 1);
                        continue;
                    }
                    NewLineAt(IndentAt(depth + 1, items[i].Start, own + options.IndentSize), i == 0 && options.CollapseBlankLines ? 0 : BlankLines(sep));
                    EmitItem(items[i], depth + 1);
                }

                if (endTag)
                {
                    Item last = items[items.Count - 1];
                    string sep = text.Substring(last.End, contentEnd - last.End);
                    if (CanBreak(flow, sep, !flow, IsBlock(last)))
                    {
                        NewLineAt(IndentAt(depth, contentEnd, own), BlankLines(sep, closing: true));
                    }
                }
            }

            if (endTag)
            {
                sb.Append("</").Append(e.Name).Append('>');
            }
        }

        /// <summary>
        /// Element content with text in it. One-line content stays as written. Multi-line content keeps its layout:
        /// when it starts on its own line it is re-indented one level in (the end tag goes on its own line), otherwise
        /// its later lines move by as much as the element's line did.
        /// </summary>
        private void EmitText(MarkupElement e, int depth, int start, int end)
        {
            string content = text.Substring(start, end - start);
            bool keepTrailing = string.Equals(e.Name, "markdown", StringComparison.OrdinalIgnoreCase);
            if (content.IndexOf('\n') < 0)
            {
                sb.Append(content);
                return;
            }

            int firstBreak = content.IndexOf('\n');
            bool startsOnOwnLine = content.Substring(0, firstBreak).Trim().Length == 0;
            int lastBreak = content.LastIndexOf('\n');
            bool endsOnOwnLine = content.Substring(lastBreak + 1).Trim().Length == 0;

            if (startsOnOwnLine)
            {
                // The smallest indent of the lines that can move becomes one level deeper than the element.
                int min = MinIndent(start + firstBreak + 1, end);
                int target = options.Indent ? (depth + 1) * options.IndentSize : min;
                int delta = min == int.MaxValue ? 0 : target - min;

                int bodyEnd = endsOnOwnLine ? start + lastBreak : end;
                if (bodyEnd > start + firstBreak)
                {
                    lineIndent = min == int.MaxValue ? 0 : target;
                    AppendShifted(start + firstBreak, bodyEnd, old => old + delta, keepTrailing, dropFirstLine: true);
                }
                if (endsOnOwnLine)
                {
                    NewLineAt(IndentAt(depth, end, depth * options.IndentSize), 0);
                }
            }
            else
            {
                // Never move a line left of the margin: that would change the lines' relative indentation.
                int delta = Math.Max(lineIndent - OldLineIndent(e.Span.Start), -MinIndent(start + firstBreak + 1, end));
                int bodyEnd = endsOnOwnLine ? start + lastBreak : end;
                AppendShifted(start, bodyEnd, old => old + delta, keepTrailing);
                if (endsOnOwnLine)
                {
                    // The end tag moves with the rest of the element.
                    NewLineAt(Math.Max(0, Columns(LineStartOf(end), end) + delta), 0);
                }
            }
        }

        /// <summary>The smallest indent of the lines from <paramref name="from"/> that the formatter may move (int.MaxValue if none).</summary>
        private int MinIndent(int from, int end)
        {
            int min = int.MaxValue;
            foreach (int lineStart in LineStarts(from, end))
            {
                int lineEnd = LineEnd(lineStart, end);
                // Blank lines, and the end tag's own line (whitespace only), don't count.
                if (IsProtected(lineStart) || IsBlank(lineStart, lineEnd))
                {
                    continue;
                }
                min = Math.Min(min, Columns(lineStart, lineEnd));
            }
            return min;
        }

        private void AppendStartTag(MarkupElement e, int depth)
        {
            List<string> attributes = e.Attributes.Select(a => Normalise(text.Substring(a.Span.Start, a.Span.Length))).ToList();
            string close = e.SelfClosing ? " />" : e.StartTagClosed ? ">" : string.Empty;

            string oneLine = "<" + e.Name + string.Concat(attributes.Select(a => " " + a)) + close;
            int firstLine = oneLine.IndexOf('\n');
            bool tooLong = CurrentColumn() + (firstLine < 0 ? oneLine.Length : firstLine) > options.PrintWidth;
            if (!options.TidyTags || (!options.WrapAttributes && WasWrapped(e)))
            {
                // Kept exactly as written.
                sb.Append(Normalise(text.Substring(e.StartTagSpan.Start, e.StartTagSpan.Length)).Replace("\n", eol));
                return;
            }
            if (attributes.Count < 2 || !options.WrapAttributes || !(tooLong || WasWrapped(e)))
            {
                sb.Append(oneLine.Replace("\n", eol));
                return;
            }

            // One attribute per line, one level in; the tag closes on the last attribute's line.
            int outer = lineIndent;
            sb.Append('<').Append(e.Name);
            foreach (string a in attributes)
            {
                NewLineAt(outer + options.IndentSize, 0);
                sb.Append(a.Replace("\n", eol));
            }
            sb.Append(close);
            lineIndent = outer;
        }

        /// <summary>True when the author put line breaks between the attributes (not just inside a value).</summary>
        private bool WasWrapped(MarkupElement e)
        {
            int pos = e.NameSpan.End;
            foreach (MarkupAttribute a in e.Attributes)
            {
                if (text.IndexOf('\n', pos, a.Span.Start - pos) >= 0) return true;
                pos = a.Span.End;
            }
            return text.IndexOf('\n', pos, e.StartTagSpan.End - pos) >= 0;
        }

        /// <summary>Attribute text as written, with CRLF reduced to LF so it can be re-joined with the chosen line ending.</summary>
        private static string Normalise(string s) => s.Replace("\r\n", "\n");

        #endregion

        #region Lines

        /// <summary>
        /// The indent for something at a nesting depth. With the indent rule off, a line keeps the indentation it had
        /// (when it started a line in the source).
        /// </summary>
        private int IndentAt(int depth, int sourceOffset, int relative)
        {
            if (options.Indent)
            {
                return depth * options.IndentSize;
            }
            // A line that already existed keeps its indent; a new one goes one level in from its parent's line.
            return StartsLine(sourceOffset) ? OldLineIndent(sourceOffset) : relative;
        }

        private bool StartsLine(int offset)
        {
            int i = offset;
            while (i > 0 && (text[i - 1] == ' ' || text[i - 1] == '\t')) i--;
            return i == 0 || text[i - 1] == '\n';
        }

        private void NewLineAt(int columns, int blankLines)
        {
            TrimTrailingWhitespace();
            sb.Append(eol);
            for (int i = 0; i < blankLines; i++)
            {
                sb.Append(eol);
            }
            sb.Append(Indent(columns));
            lineIndent = columns;
        }

        private int BlankLines(string separator, bool closing = false)
        {
            int newlines = separator.Count(c => c == '\n');
            if (!options.CollapseBlankLines) return Math.Max(0, newlines - 1);
            return closing ? 0 : Math.Max(0, Math.Min(newlines - 1, options.MaxBlankLines));
        }

        private int MaxBlank => options.CollapseBlankLines ? options.MaxBlankLines : int.MaxValue;

        /// <summary>
        /// Copies source text, re-indenting every line after the first with <paramref name="indentFor"/> (old columns to
        /// new columns), except lines that start inside protected text. Blank lines lose their spaces and runs of them
        /// shrink to <see cref="FormatOptions.MaxBlankLines"/>.
        /// </summary>
        private void AppendShifted(int start, int end, Func<int, int> indentFor, bool keepTrailing, bool dropFirstLine = false)
        {
            int lineStart = start;
            bool first = true;
            bool wroteLine = !dropFirstLine;
            int pendingBlanks = 0;
            while (true)
            {
                int lineEnd = LineEnd(lineStart, end);
                int contentEnd = lineEnd > lineStart && text[lineEnd - 1] == '\r' ? lineEnd - 1 : lineEnd;

                if (first)
                {
                    if (!dropFirstLine)
                    {
                        string line = text.Substring(lineStart, contentEnd - lineStart);
                        AppendLine(line, keepTrailing || ProtectedEnd(contentEnd));
                    }
                    first = false;
                }
                else if (IsProtected(lineStart))
                {
                    FlushBlanks(ref pendingBlanks);
                    sb.Append(eol);
                    AppendLine(text.Substring(lineStart, contentEnd - lineStart), true);
                    wroteLine = true;
                }
                else if (IsBlank(lineStart, contentEnd))
                {
                    // Blank lines are written only once a line follows, so none are left before an end tag.
                    if (wroteLine) pendingBlanks++;
                }
                else
                {
                    FlushBlanks(ref pendingBlanks);
                    int columns = Math.Max(0, indentFor(Columns(lineStart, contentEnd)));
                    int s = lineStart;
                    while (s < contentEnd && (text[s] == ' ' || text[s] == '\t')) s++;
                    string line = text.Substring(s, contentEnd - s);
                    TrimTrailingWhitespace();
                    sb.Append(eol).Append(Indent(columns));
                    AppendLine(line, keepTrailing || ProtectedEnd(contentEnd));
                    lineIndent = columns;
                    wroteLine = true;
                }

                if (lineEnd >= end)
                {
                    break;
                }
                lineStart = lineEnd + 1;
            }
        }

        private void AppendLine(string line, bool exact)
        {
            exact |= !options.TrimTrailingWhitespace;
            sb.Append(exact ? line : line.TrimEnd());
            if (exact) keptUpTo = sb.Length;
        }

        private void FlushBlanks(ref int pending)
        {
            int n = Math.Min(pending, MaxBlank);
            if (n > 0) TrimTrailingWhitespace();
            for (int i = 0; i < n; i++) sb.Append(eol);
            pending = 0;
        }

        /// <summary>True when the end of a line is inside protected text, so its trailing spaces are content.</summary>
        private bool ProtectedEnd(int offset) => protectedSpans.Any(s => offset > s.Start && offset < s.End);

        private void TrimTrailingWhitespace()
        {
            if (!options.TrimTrailingWhitespace) return;
            int i = sb.Length;
            while (i > keptUpTo && (sb[i - 1] == ' ' || sb[i - 1] == '\t')) i--;
            sb.Length = i;
        }

        /// <summary>The offset of the '\n' ending the line, or <paramref name="end"/>.</summary>
        private int LineEnd(int lineStart, int end)
        {
            int i = text.IndexOf('\n', lineStart, Math.Max(0, end - lineStart));
            return i < 0 ? end : i;
        }

        private IEnumerable<int> LineStarts(int from, int end)
        {
            for (int i = from; i <= end;)
            {
                yield return i;
                int next = text.IndexOf('\n', i, Math.Max(0, end - i));
                if (next < 0) yield break;
                i = next + 1;
            }
        }

        private int LineStartOf(int offset)
        {
            int i = offset;
            while (i > 0 && text[i - 1] != '\n') i--;
            return i;
        }

        private bool IsBlank(int start, int end)
        {
            for (int i = start; i < end; i++)
            {
                if (!char.IsWhiteSpace(text[i])) return false;
            }
            return true;
        }

        /// <summary>The width of the indentation at the start of a line, with tabs counted as one indent level.</summary>
        private int Columns(int lineStart, int end)
        {
            int columns = 0;
            for (int i = lineStart; i < end; i++)
            {
                if (text[i] == ' ') columns++;
                else if (text[i] == '\t') columns += options.IndentSize - (columns % options.IndentSize);
                else break;
            }
            return columns;
        }

        /// <summary>The indent of the source line the offset is on.</summary>
        private int OldLineIndent(int offset)
        {
            int lineStart = LineStartOf(offset);
            return Columns(lineStart, offset);
        }

        /// <summary>The column the output has reached on the current line.</summary>
        private int CurrentColumn()
        {
            int i = sb.Length;
            while (i > 0 && sb[i - 1] != '\n') i--;
            int columns = 0;
            for (int j = i; j < sb.Length; j++)
            {
                columns = sb[j] == '\t' ? columns + options.IndentSize - (columns % options.IndentSize) : columns + 1;
            }
            return columns;
        }

        private string Indent(int columns)
        {
            if (columns <= 0) return string.Empty;
            if (!options.UseTabs) return new string(' ', columns);
            return new string('\t', columns / options.IndentSize) + new string(' ', columns % options.IndentSize);
        }

        private static string DominantEndOfLine(string text)
        {
            int crlf = 0, lf = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    if (i > 0 && text[i - 1] == '\r') crlf++;
                    else lf++;
                }
            }
            return crlf > lf ? "\r\n" : "\n";
        }

        #endregion
    }
}
