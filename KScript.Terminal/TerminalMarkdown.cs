using System.Text;

using KScript.Markdown;

using Markdig.Extensions.Mathematics;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace KScript.Terminal
{
    /// <summary>
    /// Renders Markdown for a terminal: headings in bold, lists with bullets, code dimmed, links with their address.
    /// Without colour it gives readable plain text.
    /// </summary>
    public static class TerminalMarkdown
    {
        public static string Render(string markdown, bool colour)
        {
            return Render(Markdig.Markdown.Parse(TextLayoutOf(markdown), MarkdownText.Pipeline), colour);
        }

        internal static string Render(MarkdownDocument doc, bool colour)
        {
            var output = new StringBuilder();
            var r = new Renderer(output, colour);
            r.Blocks(doc, string.Empty);
            return output.ToString().TrimEnd('\n');
        }

        private static string TextLayoutOf(string markdown) => KScript.Runtime.TextLayout.Tidy(markdown ?? string.Empty);

        private sealed class Renderer
        {
            private readonly StringBuilder _out;
            private readonly bool _colour;

            public Renderer(StringBuilder output, bool colour)
            {
                _out = output;
                _colour = colour;
            }

            private string Style(string text, string code) => _colour ? Ansi.Wrap(text, code) : text;

            private void Gap()
            {
                if (_out.Length > 0 && !_out.ToString().EndsWith("\n\n", StringComparison.Ordinal)) _out.Append('\n');
            }

            public void Blocks(ContainerBlock container, string indent)
            {
                foreach (Block block in container) Block(block, indent);
            }

            private void Block(Block block, string indent)
            {
                switch (block)
                {
                    case HeadingBlock h:
                    {
                        Gap();
                        string text = Inlines(h.Inline);
                        _out.Append(indent).Append(h.Level == 1 ? Style(text, "1;4") : Style(text, "1")).Append("\n\n");
                        break;
                    }
                    case ParagraphBlock p:
                        _out.Append(Indent(Inlines(p.Inline), indent)).Append("\n\n");
                        break;
                    case ListBlock list:
                    {
                        int n = int.TryParse(list.OrderedStart, out int start) ? start : 1;
                        foreach (Block item in list)
                        {
                            if (item is not ListItemBlock li) continue;
                            string bullet = list.IsOrdered ? $"{n++}. " : "• ";
                            var inner = new StringBuilder();
                            var sub = new Renderer(inner, _colour);
                            sub.Blocks(li, string.Empty);
                            string[] lines = inner.ToString().TrimEnd('\n').Replace("\n\n", "\n").Split('\n');
                            string pad = new string(' ', bullet.Length);
                            for (int i = 0; i < lines.Length; i++)
                                _out.Append(indent).Append(i == 0 ? bullet : pad).Append(lines[i]).Append('\n');
                        }
                        _out.Append('\n');
                        break;
                    }
                    case QuoteBlock q:
                    {
                        var inner = new StringBuilder();
                        new Renderer(inner, _colour).Blocks(q, string.Empty);
                        foreach (string line in inner.ToString().TrimEnd('\n').Split('\n'))
                            _out.Append(indent).Append(Style("│ ", "90")).Append(line).Append('\n');
                        _out.Append('\n');
                        break;
                    }
                    case FencedCodeBlock or CodeBlock:
                    {
                        var code = (LeafBlock)block;
                        foreach (var line in code.Lines.Lines.Take(code.Lines.Count))
                            _out.Append(indent).Append("    ").Append(Style(line.ToString(), "36")).Append('\n');
                        _out.Append('\n');
                        break;
                    }
                    case ThematicBreakBlock:
                        _out.Append(indent).Append(Style(new string('─', 40), "90")).Append("\n\n");
                        break;
                    case Table table:
                        RenderTable(table, indent);
                        break;
                    case HtmlBlock html:
                        _out.Append(indent).Append(string.Join("\n", html.Lines.Lines.Take(html.Lines.Count).Select(l => l.ToString()))).Append("\n\n");
                        break;
                    case ContainerBlock other:
                        Blocks(other, indent);
                        break;
                    case LeafBlock leaf when leaf.Inline != null:
                        _out.Append(Indent(Inlines(leaf.Inline), indent)).Append("\n\n");
                        break;
                }
            }

            private void RenderTable(Table table, string indent)
            {
                var rows = new List<List<string>>();
                foreach (Block rowBlock in table)
                {
                    if (rowBlock is not TableRow row) continue;
                    var cells = new List<string>();
                    foreach (Block cellBlock in row)
                    {
                        var inner = new StringBuilder();
                        new Renderer(inner, false).Blocks((TableCell)cellBlock, string.Empty);
                        cells.Add(inner.ToString().Trim().Replace("\n", " "));
                    }
                    rows.Add(cells);
                }
                if (rows.Count == 0) return;
                int columns = rows.Max(r => r.Count);
                int[] widths = Enumerable.Range(0, columns).Select(c => rows.Max(r => c < r.Count ? r[c].Length : 0)).ToArray();
                for (int i = 0; i < rows.Count; i++)
                {
                    string line = string.Join("  ", Enumerable.Range(0, columns).Select(c => (c < rows[i].Count ? rows[i][c] : string.Empty).PadRight(widths[c]))).TrimEnd();
                    _out.Append(indent).Append(i == 0 ? Style(line, "1") : line).Append('\n');
                    if (i == 0) _out.Append(indent).Append(Style(string.Join("  ", widths.Select(w => new string('─', w))), "90")).Append('\n');
                }
                _out.Append('\n');
            }

            private static string Indent(string text, string indent) =>
                indent.Length == 0 ? text : string.Join("\n", text.Split('\n').Select(l => indent + l));

            private string Inlines(ContainerInline inline)
            {
                if (inline == null) return string.Empty;
                var sb = new StringBuilder();
                foreach (Inline child in inline) Inline(child, sb);
                return sb.ToString();
            }

            private void Inline(Inline inline, StringBuilder sb)
            {
                switch (inline)
                {
                    case LiteralInline lit:
                        sb.Append(lit.Content.ToString());
                        break;
                    case CodeInline code:
                        sb.Append(Style(code.Content, "36"));
                        break;
                    case MathInline math:
                        sb.Append(Style(math.Content.ToString(), "36"));
                        break;
                    case EmphasisInline em:
                    {
                        string text = Inlines(em);
                        sb.Append(em.DelimiterChar == '~' ? Style(text, "9") : em.DelimiterCount >= 2 ? Style(text, "1") : Style(text, "3"));
                        break;
                    }
                    case LinkInline link:
                    {
                        string text = Inlines(link);
                        if (link.IsImage) sb.Append('[').Append(text.Length > 0 ? text : "image").Append(']');
                        else if (string.IsNullOrEmpty(link.Url) || text == link.Url) sb.Append(Style(text, "4"));
                        else sb.Append(Style(text, "4")).Append(' ').Append(Style($"({link.Url})", "90"));
                        break;
                    }
                    case AutolinkInline auto:
                        sb.Append(Style(auto.Url, "4"));
                        break;
                    case TaskList task:
                        sb.Append(task.Checked ? "[x]" : "[ ]");
                        break;
                    case LineBreakInline:
                        sb.Append('\n');
                        break;
                    case HtmlEntityInline entity:
                        sb.Append(entity.Transcoded.ToString());
                        break;
                    case HtmlInline:
                        break;
                    case ContainerInline container:
                        sb.Append(Inlines(container));
                        break;
                }
            }
        }
    }
}
