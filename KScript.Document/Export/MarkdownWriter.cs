using System.Text;

using KScript.Documents.Maths;
using KScript.Documents.Text;

namespace KScript.Documents.Export
{
    /// <summary>Writes a document as Markdown: GitHub tables, $maths$, footnotes and a reference list.</summary>
    public sealed class MarkdownWriter
    {
        private readonly Document doc;
        private readonly Resolved resolved;
        private readonly string outputDirectory;
        private readonly StringBuilder sb = new StringBuilder();

        private MarkdownWriter(Document doc, string outputDirectory)
        {
            this.doc = doc;
            this.outputDirectory = outputDirectory;
            resolved = Resolver.Resolve(doc);
        }

        public static string Write(Document doc, string outputDirectory = null) => new MarkdownWriter(doc, outputDirectory ?? doc.BaseDirectory).Build();

        private string Build()
        {
            if (doc.Style.ShowTitle && doc.Title != null)
            {
                sb.Append("# ").Append(doc.Title).Append("\n\n");
                if (doc.Subtitle != null) sb.Append("*").Append(doc.Subtitle).Append("*\n\n");
                if (doc.Authors.Count > 0) sb.Append(string.Join(", ", doc.Authors.Select(a => a.Affiliation != null ? $"{a.Name} ({a.Affiliation})" : a.Name))).Append("\n\n");
                if (doc.Date != null) sb.Append(doc.Date).Append("\n\n");
            }
            if (doc.Abstract.Count > 0)
            {
                sb.Append("**Abstract.** ");
                Blocks(doc.Abstract, string.Empty);
            }
            if (doc.Keywords.Count > 0) sb.Append("**Keywords:** ").Append(string.Join(", ", doc.Keywords)).Append("\n\n");
            Blocks(doc.Blocks, string.Empty);
            if (resolved.Footnotes.Count > 0)
            {
                foreach (FootnoteInline f in resolved.Footnotes) sb.Append("[^").Append(f.Number).Append("]: ").Append(Inlines(f.Children)).Append('\n');
                sb.Append('\n');
            }
            return sb.ToString().TrimEnd() + "\n";
        }

        private int HeadingOffset => doc.Style.ShowTitle && doc.Title != null ? 1 : 0;

        private void Blocks(IEnumerable<Block> blocks, string indent)
        {
            foreach (Block b in blocks) Block(b, indent);
        }

        private void Line(string indent, string text) => sb.Append(indent).Append(text).Append('\n');

        private void Block(Block block, string indent)
        {
            switch (block)
            {
                case Section s:
                    sb.Append(new string('#', Math.Min(6, s.Level + HeadingOffset))).Append(' ');
                    if (s.Number != null) sb.Append(s.Number).Append(' ');
                    sb.Append(Inlines(s.Title));
                    if (s.Id != null) sb.Append(" {#").Append(s.Id).Append('}');
                    sb.Append("\n\n");
                    Blocks(s.Blocks, indent);
                    break;
                case Paragraph p:
                {
                    string text = Inlines(p.Inlines);
                    if (p.Bold) text = "**" + text + "**";
                    foreach (string line in text.Split('\n')) Line(indent, line);
                    sb.Append(indent.TrimEnd()).Append('\n');
                    break;
                }
                case ListBlock list:
                    for (int i = 0; i < list.Items.Count; i++)
                    {
                        string marker = list.Ordered ? (list.Start + i) + ". " : "- ";
                        int start = sb.Length;
                        Blocks(list.Items[i].Blocks, indent + new string(' ', marker.Length));
                        string item = sb.ToString(start, sb.Length - start).TrimEnd();
                        sb.Length = start;
                        // The first line gets the marker in place of its indent.
                        string body = item.Length >= indent.Length + marker.Length ? item.Substring(indent.Length + marker.Length) : item.TrimStart();
                        sb.Append(indent).Append(marker).Append(body).Append('\n');
                    }
                    sb.Append('\n');
                    break;
                case Table t:
                {
                    if (t.Caption != null) Line(indent, $"**Table {t.Number}:** {Inlines(t.Caption)}\n");
                    int columns = t.ColumnCount;
                    List<TableRow> rows = t.Rows;
                    bool header = rows.Count > 0 && rows[0].Header;
                    string Cells(TableRow row)
                    {
                        var cells = new List<string>();
                        foreach (TableCell c in row.Cells)
                        {
                            string text = Inlines(c.Inlines).Replace("|", "\\|").Replace("\n", " ");
                            if (c.Bold) text = "**" + text + "**";
                            cells.Add(text);
                            for (int s = 1; s < c.Span; s++) cells.Add(string.Empty);
                        }
                        while (cells.Count < columns) cells.Add(string.Empty);
                        return "| " + string.Join(" | ", cells) + " |";
                    }
                    Line(indent, header ? Cells(rows[0]) : "| " + string.Join(" | ", Enumerable.Repeat(" ", columns)) + " |");
                    Line(indent, "|" + string.Join("|", Enumerable.Range(0, columns).Select(c => t.AlignOf(c) switch { ColumnAlign.Right => " ---: ", ColumnAlign.Center => " :---: ", _ => " --- " })) + "|");
                    foreach (TableRow row in rows.Skip(header ? 1 : 0)) Line(indent, Cells(row));
                    sb.Append('\n');
                    break;
                }
                case Figure f:
                {
                    string path = Path.GetRelativePath(outputDirectory, Path.GetFullPath(Path.Combine(doc.BaseDirectory, f.Source))).Replace('\\', '/');
                    string alt = f.Caption != null ? InlineParser.Plain(f.Caption) : Path.GetFileNameWithoutExtension(f.Source);
                    Line(indent, $"![{alt}]({path.Replace(" ", "%20")})");
                    sb.Append('\n');
                    if (f.Caption != null) Line(indent, $"*Figure {f.Number}: {Inlines(f.Caption)}*\n");
                    break;
                }
                case MathBlock m:
                    Line(indent, "$$");
                    Line(indent, MathLatex.Write(m.Node) + (m.Numbered ? $" \\tag{{{m.Number}}}" : string.Empty));
                    Line(indent, "$$");
                    sb.Append('\n');
                    break;
                case CodeBlock c:
                    Line(indent, "```" + (c.Language ?? string.Empty));
                    foreach (string line in c.Text.Split('\n')) Line(indent, line);
                    Line(indent, "```");
                    sb.Append('\n');
                    break;
                case Quote q:
                    Blocks(q.Blocks, indent + "> ");
                    if (q.Attribution != null) Line(indent + "> ", "\u2014 " + q.Attribution);
                    sb.Append('\n');
                    break;
                case PageBreak:
                case Spacer:
                    break;
                case Rule:
                    Line(indent, "---");
                    sb.Append('\n');
                    break;
                case Contents c:
                    sb.Append("**").Append(c.Title).Append("**\n\n");
                    foreach (Section s in resolved.Sections.Where(x => x.Level <= c.Depth))
                    {
                        sb.Append(new string(' ', (s.Level - 1) * 2)).Append("- ");
                        string title = (s.Number != null ? s.Number + " " : string.Empty) + InlineParser.Plain(s.Title);
                        sb.Append(s.Id != null ? $"[{title}](#{s.Id})" : title).Append('\n');
                    }
                    sb.Append('\n');
                    break;
                case Bibliography bib:
                    if (resolved.Cited.Count == 0) break;
                    sb.Append(new string('#', 1 + HeadingOffset)).Append(' ').Append(bib.Title).Append("\n\n");
                    foreach (Source s in resolved.Cited)
                    {
                        string entry = Inlines(BibTex.Format(s, doc.Style.Citations));
                        sb.Append(doc.Style.Citations == CitationStyle.AuthorYear ? "- " : $"{s.Number}. ").Append(entry).Append('\n');
                    }
                    sb.Append('\n');
                    break;
            }
        }

        private static string EscapeText(string text)
        {
            var o = new StringBuilder();
            foreach (char c in text)
            {
                if ("\\`*_[]<>|$".IndexOf(c) >= 0) o.Append('\\');
                o.Append(c);
            }
            return o.ToString();
        }

        private string Inlines(IEnumerable<Inline> inlines)
        {
            var o = new StringBuilder();
            foreach (Inline i in inlines ?? Enumerable.Empty<Inline>())
            {
                switch (i)
                {
                    case TextInline t:
                        o.Append(EscapeText(t.Text));
                        break;
                    case StrongInline s:
                        o.Append("**").Append(Inlines(s.Children)).Append("**");
                        break;
                    case EmphasisInline e:
                        o.Append('*').Append(Inlines(e.Children)).Append('*');
                        break;
                    case CodeInline c:
                        o.Append('`').Append(c.Text).Append('`');
                        break;
                    case LinkInline l:
                        o.Append('[').Append(Inlines(l.Children)).Append("](").Append(l.Url).Append(')');
                        break;
                    case MathInline m:
                        o.Append('$').Append(MathLatex.Write(m.Node)).Append('$');
                        break;
                    case CiteInline c:
                        o.Append(EscapeText(c.Text));
                        break;
                    case RefInline r:
                        o.Append(resolved.Targets[r.Id] is Section ? $"[{r.Text}](#{r.Id})" : EscapeText(r.Text));
                        break;
                    case FootnoteInline f:
                        o.Append("[^").Append(f.Number).Append(']');
                        break;
                    case LineBreakInline:
                        o.Append("  \n");
                        break;
                }
            }
            return o.ToString();
        }
    }
}
