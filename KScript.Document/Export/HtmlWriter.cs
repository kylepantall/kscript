using System.Net;
using System.Text;

using KScript.Documents.Maths;
using KScript.Documents.Text;

namespace KScript.Documents.Export
{
    /// <summary>
    /// Writes a document as one self-contained HTML page: maths as MathML, images inside the file, and print
    /// styles, so a browser's Print gives a tidy PDF too.
    /// </summary>
    public sealed class HtmlWriter
    {
        private readonly Document doc;
        private readonly Resolved resolved;
        private readonly StringBuilder sb = new StringBuilder();

        private HtmlWriter(Document doc)
        {
            this.doc = doc;
            resolved = Resolver.Resolve(doc);
        }

        public static string Write(Document doc) => new HtmlWriter(doc).Build();

        private static string H(string s) => WebUtility.HtmlEncode(s ?? string.Empty);

        private DocumentStyle S => doc.Style;

        private static string Family(FontFamily f) => f switch
        {
            FontFamily.Sans => "\"Helvetica Neue\", Helvetica, Arial, sans-serif",
            FontFamily.Mono => "\"Courier New\", Courier, monospace",
            _ => "\"Times New Roman\", Times, Georgia, serif",
        };

        private string Build()
        {
            sb.Append("<!DOCTYPE html>\n<html lang=\"").Append(H(S.Language)).Append("\">\n<head>\n<meta charset=\"utf-8\">\n");
            sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
            sb.Append("<title>").Append(H(doc.Title ?? "Document")).Append("</title>\n");
            if (doc.Authors.Count > 0) sb.Append("<meta name=\"author\" content=\"").Append(H(string.Join(", ", doc.Authors.Select(a => a.Name)))).Append("\">\n");
            if (doc.Keywords.Count > 0) sb.Append("<meta name=\"keywords\" content=\"").Append(H(string.Join(", ", doc.Keywords))).Append("\">\n");
            sb.Append("<style>\n").Append(Css()).Append("</style>\n</head>\n<body>\n<main class=\"doc ").Append(H(S.Template)).Append("\">\n");
            if (S.ShowTitle && doc.Title != null)
            {
                sb.Append("<header class=\"title-block\">\n<h1 class=\"title\">").Append(H(doc.Title)).Append("</h1>\n");
                if (doc.Subtitle != null) sb.Append("<p class=\"subtitle\">").Append(H(doc.Subtitle)).Append("</p>\n");
                if (doc.Authors.Count > 0)
                {
                    sb.Append("<div class=\"authors\">");
                    foreach (Author a in doc.Authors)
                    {
                        sb.Append("<div class=\"author\"><span class=\"name\">").Append(H(a.Name)).Append("</span>");
                        if (a.Affiliation != null) sb.Append("<span class=\"affiliation\">").Append(H(a.Affiliation)).Append("</span>");
                        if (a.Email != null) sb.Append("<a class=\"email\" href=\"mailto:").Append(H(a.Email)).Append("\">").Append(H(a.Email)).Append("</a>");
                        sb.Append("</div>");
                    }
                    sb.Append("</div>\n");
                }
                if (doc.Date != null) sb.Append("<p class=\"date\">").Append(H(doc.Date)).Append("</p>\n");
                sb.Append("</header>\n");
            }
            if (doc.Abstract.Count > 0)
            {
                sb.Append("<section class=\"abstract\">\n<h2>Abstract</h2>\n");
                Blocks(doc.Abstract);
                sb.Append("</section>\n");
            }
            if (doc.Keywords.Count > 0) sb.Append("<p class=\"keywords\"><strong>Keywords:</strong> ").Append(H(string.Join(", ", doc.Keywords))).Append("</p>\n");
            Blocks(doc.Blocks);
            if (resolved.Footnotes.Count > 0)
            {
                sb.Append("<section class=\"footnotes\">\n<hr>\n<ol>\n");
                foreach (FootnoteInline f in resolved.Footnotes)
                    sb.Append("<li id=\"fn").Append(f.Number).Append("\">").Append(Inlines(f.Children)).Append(" <a href=\"#fnref").Append(f.Number).Append("\" class=\"back\">↩</a></li>\n");
                sb.Append("</ol>\n</section>\n");
            }
            sb.Append("</main>\n</body>\n</html>\n");
            return sb.ToString();
        }

        private string Css()
        {
            string size = Units.N(S.FontSize);
            var c = new StringBuilder();
            c.Append(":root { --accent: ").Append(S.Accent).Append("; }\n");
            c.Append("body { margin: 0; background: #f4f4f4; color: #111; }\n");
            c.Append(".doc { box-sizing: border-box; max-width: ").Append(Units.N(S.PageWidth)).Append("pt; margin: 2rem auto; padding: ")
                .Append(Units.N(S.MarginTop)).Append("pt ").Append(Units.N(S.MarginRight)).Append("pt ").Append(Units.N(S.MarginBottom)).Append("pt ").Append(Units.N(S.MarginLeft))
                .Append("pt; background: #fff; box-shadow: 0 1px 4px rgba(0,0,0,.15); font-family: ").Append(Family(S.Font)).Append("; font-size: ").Append(size)
                .Append("pt; line-height: ").Append(Units.N(S.LineHeight)).Append("; text-align: ").Append(S.Align.ToString().ToLowerInvariant()).Append("; hyphens: auto; }\n");
            c.Append(".doc h1, .doc h2, .doc h3, .doc h4, .doc h5, .doc h6 { font-family: ").Append(Family(S.HeadingFont)).Append("; color: var(--accent); text-align: left; line-height: 1.2; margin: 1.4em 0 .5em; }\n");
            c.Append(".doc h2 { font-size: 1.4em; } .doc h3 { font-size: 1.2em; } .doc h4 { font-size: 1.05em; } .doc .num { margin-right: .6em; }\n");
            if (S.ParagraphSpacing == 0) c.Append(".doc p { margin: 0; text-indent: 1.5em; } .doc h2 + p, .doc h3 + p, .doc h4 + p, .doc p.noindent, .doc li p, .doc td p { text-indent: 0; }\n");
            else c.Append(".doc p { margin: 0 0 ").Append(Units.N(S.ParagraphSpacing * S.LineHeight)).Append("em; }\n");
            c.Append(".title-block { text-align: center; margin-bottom: 2em; } .title { font-size: 1.8em; margin: 0 0 .3em; color: inherit; } .subtitle { font-size: 1.2em; margin: 0; }\n");
            c.Append(".authors { display: flex; flex-wrap: wrap; justify-content: center; gap: 1.5em; margin: 1em 0 .5em; } .author span, .author a { display: block; } .affiliation, .email { font-size: .9em; } .date { margin: 0; }\n");
            c.Append(".abstract { margin: 0 2.5em 2em; font-size: .92em; } .abstract h2 { text-align: center; font-size: 1em; color: inherit; margin: 0 0 .4em; } .keywords { text-indent: 0; margin-bottom: 1.5em; }\n");
            c.Append("figure { margin: 1.5em auto; text-align: center; } figure img { max-width: 100%; } figcaption, caption { font-size: .92em; margin: .5em 0; text-align: center; }\n");
            c.Append("table { border-collapse: collapse; margin: 1.2em auto; } td, th { padding: .25em .7em; vertical-align: top; }\n");
            c.Append("table.booktabs { border-top: 1.5px solid #000; border-bottom: 1.5px solid #000; } table.booktabs thead tr:last-child th { border-bottom: 1px solid #000; } tr.rule td { border-top: 1px solid #000; }\n");
            c.Append("table.grid td, table.grid th { border: 1px solid #000; } table.full { width: 100%; }\n");
            c.Append(".equation { display: flex; align-items: center; margin: 1em 0; } .equation math { flex: 1; } .equation .eqno { margin-left: 1em; }\n");
            c.Append("pre { background: #f5f5f5; padding: .8em 1em; font-size: .9em; overflow-x: auto; text-align: left; } code { font-family: \"Courier New\", Courier, monospace; }\n");
            c.Append("blockquote { margin: 1em 2em; font-style: italic; } blockquote .by { display: block; text-align: right; font-style: normal; }\n");
            c.Append(".toc ul { list-style: none; padding-left: 1.2em; } .toc > ul { padding-left: 0; } .toc a { color: inherit; text-decoration: none; }\n");
            c.Append(".references li { margin-bottom: .4em; } .footnotes { font-size: .9em; } .footnotes .back { text-decoration: none; } a { color: var(--accent); }\n");
            c.Append(".page-break { break-after: page; } hr.rule { border: 0; border-top: 1px solid #000; }\n");
            c.Append("@media print { body { background: none; } .doc { margin: 0; box-shadow: none; max-width: none; padding: 0; } @page { size: ")
                .Append(Units.N(S.PageWidth)).Append("pt ").Append(Units.N(S.PageHeight)).Append("pt; margin: ").Append(Units.N(S.MarginTop)).Append("pt ")
                .Append(Units.N(S.MarginRight)).Append("pt ").Append(Units.N(S.MarginBottom)).Append("pt ").Append(Units.N(S.MarginLeft)).Append("pt; } h2, h3 { break-after: avoid; } figure, table { break-inside: avoid; } }\n");
            return c.ToString();
        }

        private void Blocks(IEnumerable<Block> blocks)
        {
            foreach (Block b in blocks) Block(b);
        }

        private static string IdAttr(Block b) => b.Id != null ? $" id=\"{H(b.Id)}\"" : string.Empty;

        private void Block(Block block)
        {
            switch (block)
            {
                case Section s:
                {
                    int h = Math.Min(6, s.Level + 1);
                    if (s.NewPage) sb.Append("<div class=\"page-break\"></div>\n");
                    sb.Append("<section").Append(IdAttr(s)).Append(">\n<h").Append(h).Append('>');
                    if (s.Number != null) sb.Append("<span class=\"num\">").Append(s.Number).Append("</span>");
                    sb.Append(Inlines(s.Title)).Append("</h").Append(h).Append(">\n");
                    Blocks(s.Blocks);
                    sb.Append("</section>\n");
                    break;
                }
                case Paragraph p:
                {
                    var style = new List<string>();
                    if (p.Align != null) style.Add("text-align: " + p.Align.ToString().ToLowerInvariant());
                    if (p.Bold) style.Add("font-weight: bold");
                    if (p.Scale != 1) style.Add("font-size: " + Units.N(p.Scale) + "em");
                    if (p.SpaceBefore > 0) style.Add("margin-top: " + Units.N(p.SpaceBefore) + "pt");
                    if (p.Color != null) style.Add("color: " + p.Color);
                    sb.Append("<p").Append(IdAttr(p)).Append(p.NoIndent || p.Align != null ? " class=\"noindent\"" : string.Empty)
                        .Append(style.Count > 0 ? " style=\"" + string.Join("; ", style) + "\"" : string.Empty).Append('>')
                        .Append(Inlines(p.Inlines)).Append("</p>\n");
                    break;
                }
                case ListBlock list:
                {
                    string tag = list.Ordered ? "ol" : "ul";
                    sb.Append('<').Append(tag).Append(IdAttr(list));
                    if (list.Ordered)
                    {
                        sb.Append(" type=\"").Append(list.Style switch { ListStyle.Letter => "a", ListStyle.Roman => "i", _ => "1" }).Append('"');
                        if (list.Start != 1) sb.Append(" start=\"").Append(list.Start).Append('"');
                    }
                    sb.Append(">\n");
                    foreach (ListItem item in list.Items)
                    {
                        sb.Append("<li>");
                        if (item.Blocks.Count == 1 && item.Blocks[0] is Paragraph only) sb.Append(Inlines(only.Inlines));
                        else Blocks(item.Blocks);
                        sb.Append("</li>\n");
                    }
                    sb.Append("</").Append(tag).Append(">\n");
                    break;
                }
                case Table t:
                    Table(t);
                    break;
                case Figure f:
                {
                    ImageData image = f.Image ?? ImageData.Load(Path.Combine(doc.BaseDirectory, f.Source));
                    string alt = f.Caption != null ? InlineParser.Plain(f.Caption) : Path.GetFileNameWithoutExtension(f.Source);
                    sb.Append("<figure").Append(IdAttr(f)).Append("><img src=\"data:").Append(image.MimeType).Append(";base64,").Append(Convert.ToBase64String(image.Bytes))
                        .Append("\" alt=\"").Append(H(alt)).Append('"');
                    if (f.Width > 0) sb.Append(" style=\"width: ").Append(Units.N(f.Width * 100)).Append("%\"");
                    sb.Append('>');
                    if (f.Caption != null) sb.Append("<figcaption>Figure ").Append(f.Number).Append(": ").Append(Inlines(f.Caption)).Append("</figcaption>");
                    sb.Append("</figure>\n");
                    break;
                }
                case MathBlock m:
                    sb.Append("<div class=\"equation\"").Append(IdAttr(m)).Append('>').Append(MathMl.Write(m.Node, display: true));
                    if (m.Numbered) sb.Append("<span class=\"eqno\">(").Append(m.Number).Append(")</span>");
                    sb.Append("</div>\n");
                    break;
                case CodeBlock c:
                    sb.Append("<pre").Append(IdAttr(c)).Append("><code").Append(c.Language != null ? $" class=\"language-{H(c.Language)}\"" : string.Empty).Append('>')
                        .Append(H(c.Text)).Append("</code></pre>\n");
                    break;
                case Quote q:
                    sb.Append("<blockquote").Append(IdAttr(q)).Append(">\n");
                    Blocks(q.Blocks);
                    if (q.Attribution != null) sb.Append("<span class=\"by\">— ").Append(H(q.Attribution)).Append("</span>\n");
                    sb.Append("</blockquote>\n");
                    break;
                case PageBreak:
                    sb.Append("<div class=\"page-break\"></div>\n");
                    break;
                case Spacer sp:
                    sb.Append("<div style=\"height: ").Append(Units.N(sp.Height)).Append("pt\"></div>\n");
                    break;
                case Rule:
                    sb.Append("<hr class=\"rule\">\n");
                    break;
                case Contents c:
                    Contents(c);
                    break;
                case Bibliography bib:
                    if (resolved.Cited.Count == 0) break;
                    sb.Append("<section class=\"references\"").Append(IdAttr(bib)).Append(">\n<h2>").Append(H(bib.Title)).Append("</h2>\n");
                    sb.Append(S.Citations == CitationStyle.AuthorYear ? "<ul>\n" : "<ol>\n");
                    foreach (Source s in resolved.Cited) sb.Append("<li id=\"ref-").Append(H(s.Key)).Append("\">").Append(Inlines(BibTex.Format(s, S.Citations))).Append("</li>\n");
                    sb.Append(S.Citations == CitationStyle.AuthorYear ? "</ul>\n" : "</ol>\n").Append("</section>\n");
                    break;
            }
        }

        private void Contents(Contents c)
        {
            sb.Append("<nav class=\"toc\">\n<h2>").Append(H(c.Title)).Append("</h2>\n");
            int level = 0;
            foreach (Section s in resolved.Sections.Where(x => x.Level <= c.Depth))
            {
                while (level < s.Level)
                {
                    sb.Append("<ul>");
                    level++;
                }
                while (level > s.Level)
                {
                    sb.Append("</ul>");
                    level--;
                }
                string title = (s.Number != null ? "<span class=\"num\">" + s.Number + "</span> " : string.Empty) + Inlines(s.Title);
                sb.Append("<li>").Append(s.Id != null ? $"<a href=\"#{H(s.Id)}\">{title}</a>" : title).Append("</li>\n");
            }
            while (level-- > 0) sb.Append("</ul>");
            sb.Append("\n</nav>\n");
        }

        private void Table(Table t)
        {
            string rules = t.Rules switch { TableRules.Booktabs => "booktabs", TableRules.Grid => "grid", _ => "plain" };
            sb.Append("<table class=\"").Append(rules).Append(t.Widths.Count > 0 ? " full" : string.Empty).Append('"').Append(IdAttr(t));
            if (t.Scale != 1) sb.Append(" style=\"font-size: ").Append(Units.N(t.Scale)).Append("em\"");
            sb.Append(">\n");
            if (t.Caption != null) sb.Append("<caption>Table ").Append(t.Number).Append(": ").Append(Inlines(t.Caption)).Append("</caption>\n");
            if (t.Widths.Count > 0)
            {
                double total = t.Widths.Sum();
                sb.Append("<colgroup>");
                foreach (double w in t.Widths) sb.Append("<col style=\"width: ").Append(Units.N(w / total * 100)).Append("%\">");
                sb.Append("</colgroup>\n");
            }
            var header = t.Rows.TakeWhile(r => r.Header).ToList();
            if (header.Count > 0)
            {
                sb.Append("<thead>\n");
                foreach (TableRow row in header) Row(t, row, "th");
                sb.Append("</thead>\n");
            }
            sb.Append("<tbody>\n");
            foreach (TableRow row in t.Rows.Skip(header.Count)) Row(t, row, "td");
            sb.Append("</tbody>\n</table>\n");
        }

        private void Row(Table t, TableRow row, string tag)
        {
            sb.Append("<tr").Append(row.RuleAbove ? " class=\"rule\"" : string.Empty).Append('>');
            int column = 0;
            foreach (TableCell cell in row.Cells)
            {
                int span = Math.Max(1, cell.Span);
                ColumnAlign align = t.AlignOf(column + span - 1);
                sb.Append('<').Append(tag);
                if (span > 1) sb.Append(" colspan=\"").Append(span).Append('"');
                var style = new List<string>();
                if (align != ColumnAlign.Left) style.Add("text-align: " + align.ToString().ToLowerInvariant());
                if (cell.Bold) style.Add("font-weight: bold");
                if (style.Count > 0) sb.Append(" style=\"").Append(string.Join("; ", style)).Append('"');
                sb.Append('>').Append(Inlines(cell.Inlines)).Append("</").Append(tag).Append('>');
                column += span;
            }
            sb.Append("</tr>\n");
        }

        private string Inlines(IEnumerable<Inline> inlines)
        {
            var o = new StringBuilder();
            foreach (Inline i in inlines ?? Enumerable.Empty<Inline>())
            {
                switch (i)
                {
                    case TextInline t:
                        o.Append(H(t.Text));
                        break;
                    case StrongInline s:
                        o.Append("<strong>").Append(Inlines(s.Children)).Append("</strong>");
                        break;
                    case EmphasisInline e:
                        o.Append("<em>").Append(Inlines(e.Children)).Append("</em>");
                        break;
                    case CodeInline c:
                        o.Append("<code>").Append(H(c.Text)).Append("</code>");
                        break;
                    case LinkInline l:
                        o.Append("<a href=\"").Append(H(l.Url)).Append("\">").Append(Inlines(l.Children)).Append("</a>");
                        break;
                    case MathInline m:
                        o.Append(MathMl.Write(m.Node, display: false));
                        break;
                    case CiteInline c:
                        o.Append("<a class=\"cite\" href=\"#ref-").Append(H(c.Keys[0])).Append("\">").Append(H(c.Text)).Append("</a>");
                        break;
                    case RefInline r:
                        o.Append("<a class=\"ref\" href=\"#").Append(H(r.Id)).Append("\">").Append(H(r.Text)).Append("</a>");
                        break;
                    case FootnoteInline f:
                        o.Append("<sup id=\"fnref").Append(f.Number).Append("\"><a href=\"#fn").Append(f.Number).Append("\">").Append(f.Number).Append("</a></sup>");
                        break;
                    case LineBreakInline:
                        o.Append("<br>");
                        break;
                }
            }
            return o.ToString();
        }
    }
}
