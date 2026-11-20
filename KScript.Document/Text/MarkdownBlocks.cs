using System.Text;
using System.Text.RegularExpressions;

using KScript.Documents.Maths;

namespace KScript.Documents.Text
{
    /// <summary>
    /// Reads Markdown into document blocks: # headings become sections, and paragraphs, - and 1. lists, ``` code,
    /// &gt; quotes, --- rules, ![caption](image.png) figures, | tables |, and \[ maths \] (or $$ maths $$ in files).
    /// </summary>
    public static class MarkdownBlocks
    {
        private static readonly Regex Heading = new Regex(@"^(#{1,6})\s+(.*?)\s*(?:\{#([\w:.\-]+)\})?\s*#*\s*$");
        private static readonly Regex Bullet = new Regex(@"^(\s*)[-*+]\s+(.*)$");
        private static readonly Regex Ordered = new Regex(@"^(\s*)(\d+)[.)]\s+(.*)$");
        private static readonly Regex Image = new Regex(@"^!\[(.*)\]\(([^)\s]+)(?:\s+""[^""]*"")?\)\s*(?:\{#([\w:.\-]+)\})?$");

        /// <param name="dollarMaths">$x$ and $$ x $$ are maths too: true for .md files, false for text in scripts.</param>
        public static List<Block> Parse(string text, bool dollarMaths)
        {
            string[] lines = (text ?? string.Empty).Replace("\r\n", "\n").Split('\n');
            var root = new List<Block>();
            var stack = new List<(int Level, List<Block> Blocks)> { (0, root) };
            int i = 0;
            List<Block> Target() => stack[^1].Blocks;
            List<Inline> Inlines(string s) => InlineParser.Parse(s, keepLines: false, dollarMaths);

            while (i < lines.Length)
            {
                string line = lines[i];
                string trimmed = line.Trim();
                if (trimmed.Length == 0)
                {
                    i++;
                    continue;
                }

                Match h = Heading.Match(trimmed);
                if (h.Success)
                {
                    int level = h.Groups[1].Value.Length;
                    while (stack.Count > 1 && stack[^1].Level >= level) stack.RemoveAt(stack.Count - 1);
                    var section = new Section { Title = Inlines(h.Groups[2].Value), Id = h.Groups[3].Success ? h.Groups[3].Value : null };
                    Target().Add(section);
                    stack.Add((level, section.Blocks));
                    i++;
                    continue;
                }

                if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
                {
                    string fence = trimmed.Substring(0, 3);
                    string language = trimmed.Substring(3).Trim();
                    var code = new StringBuilder();
                    i++;
                    while (i < lines.Length && !lines[i].Trim().StartsWith(fence, StringComparison.Ordinal))
                    {
                        if (code.Length > 0) code.Append('\n');
                        code.Append(lines[i]);
                        i++;
                    }
                    i++;
                    Target().Add(new CodeBlock { Language = language.Length > 0 ? language : null, Text = code.ToString() });
                    continue;
                }

                string mathOpen = trimmed.StartsWith("\\[", StringComparison.Ordinal) ? "\\[" : dollarMaths && trimmed.StartsWith("$$", StringComparison.Ordinal) ? "$$" : null;
                if (mathOpen != null)
                {
                    string close = mathOpen == "\\[" ? "\\]" : "$$";
                    var math = new StringBuilder(trimmed.Substring(2));
                    while (math.ToString().IndexOf(close, StringComparison.Ordinal) < 0 && ++i < lines.Length) math.Append('\n').Append(lines[i]);
                    string body = math.ToString();
                    int end = body.IndexOf(close, StringComparison.Ordinal);
                    string after = end >= 0 ? body.Substring(end + 2).Trim() : string.Empty;
                    if (end >= 0) body = body.Substring(0, end);
                    Match id = Regex.Match(after, @"^\{#([\w:.\-]+)\}$");
                    Target().Add(new MathBlock { Latex = body.Trim(), Node = MathParser.Parse(body), Id = id.Success ? id.Groups[1].Value : null });
                    i++;
                    continue;
                }

                if (Regex.IsMatch(trimmed, @"^(-{3,}|\*{3,}|_{3,})$"))
                {
                    Target().Add(new Rule());
                    i++;
                    continue;
                }

                Match img = Image.Match(trimmed);
                if (img.Success)
                {
                    string caption = img.Groups[1].Value;
                    Target().Add(new Figure { Source = img.Groups[2].Value, Caption = caption.Length > 0 ? Inlines(caption) : null, Id = img.Groups[3].Success ? img.Groups[3].Value : null });
                    i++;
                    continue;
                }

                if (trimmed.StartsWith(">", StringComparison.Ordinal))
                {
                    var quoted = new StringBuilder();
                    while (i < lines.Length && lines[i].Trim().StartsWith(">", StringComparison.Ordinal))
                    {
                        string q = lines[i].Trim().Substring(1);
                        quoted.Append(q.StartsWith(" ", StringComparison.Ordinal) ? q.Substring(1) : q).Append('\n');
                        i++;
                    }
                    var quote = new Quote();
                    quote.Blocks.AddRange(Parse(quoted.ToString(), dollarMaths));
                    Target().Add(quote);
                    continue;
                }

                if (trimmed.StartsWith("|", StringComparison.Ordinal) && i + 1 < lines.Length && Regex.IsMatch(lines[i + 1].Trim(), @"^\|?\s*:?-+:?\s*(\|\s*:?-+:?\s*)*\|?$"))
                {
                    var table = new Table();
                    table.Rows.Add(Row(trimmed, header: true, Inlines));
                    foreach (string spec in Cells(lines[i + 1].Trim()))
                    {
                        string s = spec.Trim();
                        table.Align.Add(s.StartsWith(":", StringComparison.Ordinal) && s.EndsWith(":", StringComparison.Ordinal) ? ColumnAlign.Center : s.EndsWith(":", StringComparison.Ordinal) ? ColumnAlign.Right : ColumnAlign.Left);
                    }
                    i += 2;
                    while (i < lines.Length && lines[i].Trim().StartsWith("|", StringComparison.Ordinal))
                    {
                        table.Rows.Add(Row(lines[i].Trim(), header: false, Inlines));
                        i++;
                    }
                    Target().Add(table);
                    continue;
                }

                if (Bullet.IsMatch(line) || Ordered.IsMatch(line))
                {
                    Target().Add(List(lines, ref i, Indent(line), dollarMaths));
                    continue;
                }

                // A paragraph: lines up to a blank line or the start of another block.
                var para = new StringBuilder(trimmed);
                i++;
                while (i < lines.Length && lines[i].Trim().Length > 0 && !StartsBlock(lines[i], dollarMaths))
                {
                    para.Append('\n').Append(lines[i].Trim());
                    i++;
                }
                Target().Add(new Paragraph { Inlines = Inlines(para.ToString()) });
            }
            return root;
        }

        private static int Indent(string line) => line.Length - line.TrimStart().Length;

        private static bool StartsBlock(string line, bool dollarMaths)
        {
            string t = line.Trim();
            return Heading.IsMatch(t) || t.StartsWith("```", StringComparison.Ordinal) || t.StartsWith(">", StringComparison.Ordinal) || Bullet.IsMatch(line) || Ordered.IsMatch(line)
                || t.StartsWith("\\[", StringComparison.Ordinal) || (dollarMaths && t.StartsWith("$$", StringComparison.Ordinal)) || Regex.IsMatch(t, @"^(-{3,}|\*{3,})$");
        }

        private static List<string> Cells(string row)
        {
            string r = row.Trim();
            if (r.StartsWith("|", StringComparison.Ordinal)) r = r.Substring(1);
            if (r.EndsWith("|", StringComparison.Ordinal) && !r.EndsWith("\\|", StringComparison.Ordinal)) r = r.Substring(0, r.Length - 1);
            var cells = new List<string>();
            var cell = new StringBuilder();
            for (int i = 0; i < r.Length; i++)
            {
                if (r[i] == '\\' && i + 1 < r.Length && r[i + 1] == '|')
                {
                    cell.Append('|');
                    i++;
                    continue;
                }
                if (r[i] == '|')
                {
                    cells.Add(cell.ToString());
                    cell.Clear();
                    continue;
                }
                cell.Append(r[i]);
            }
            cells.Add(cell.ToString());
            return cells;
        }

        /// <summary>One table row from cells separated by |: "Ada | 36 | London".</summary>
        public static TableRow Row(string text, bool header, Func<string, List<Inline>> inlines)
        {
            var row = new TableRow { Header = header };
            foreach (string cell in Cells(text)) row.Cells.Add(new TableCell { Inlines = inlines(cell.Trim()) });
            return row;
        }

        private static ListBlock List(string[] lines, ref int i, int indent, bool dollarMaths)
        {
            Match first = Ordered.Match(lines[i]);
            var list = new ListBlock { Style = first.Success ? ListStyle.Number : ListStyle.Bullet, Start = first.Success ? int.Parse(first.Groups[2].Value) : 1 };
            while (i < lines.Length)
            {
                string line = lines[i];
                if (line.Trim().Length == 0)
                {
                    // A blank line ends the list unless the next line carries it on with the same kind of item (or a nested one).
                    if (i + 1 < lines.Length && (Indent(lines[i + 1]) > indent ? Bullet.IsMatch(lines[i + 1]) || Ordered.IsMatch(lines[i + 1])
                        : Indent(lines[i + 1]) == indent && (list.Style == ListStyle.Bullet ? Bullet.IsMatch(lines[i + 1]) : Ordered.IsMatch(lines[i + 1]))))
                    {
                        i++;
                        continue;
                    }
                    break;
                }
                int lineIndent = Indent(line);
                Match b = Bullet.Match(line), o = Ordered.Match(line);
                if ((b.Success || o.Success) && lineIndent > indent && list.Items.Count > 0)
                {
                    list.Items[^1].Blocks.Add(List(lines, ref i, lineIndent, dollarMaths));
                    continue;
                }
                if (!(b.Success || o.Success) || lineIndent < indent)
                {
                    if (lineIndent > indent && list.Items.Count > 0 && list.Items[^1].Blocks.LastOrDefault() is Paragraph p)
                    {
                        // A continuation line of the item's text.
                        p.Inlines.Add(new TextInline(" "));
                        p.Inlines.AddRange(InlineParser.Parse(line.Trim(), false, dollarMaths));
                        i++;
                        continue;
                    }
                    break;
                }
                if ((list.Style == ListStyle.Bullet) != b.Success) break; // - after 1. (or the other way round) starts a new list
                var item = new ListItem();
                item.Blocks.Add(new Paragraph { Inlines = InlineParser.Parse(b.Success ? b.Groups[2].Value : o.Groups[3].Value, false, dollarMaths) });
                list.Items.Add(item);
                i++;
            }
            return list;
        }
    }
}
