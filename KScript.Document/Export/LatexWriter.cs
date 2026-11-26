using System.Text;

using KScript.Documents.Maths;
using KScript.Documents.Text;

namespace KScript.Documents.Export
{
    /// <summary>LaTeX text escaping.</summary>
    public static class LatexText
    {
        public static string Escape(string text)
        {
            var sb = new StringBuilder(text.Length + 8);
            foreach (char c in text)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\textbackslash{}"); break;
                    case '{': sb.Append("\\{"); break;
                    case '}': sb.Append("\\}"); break;
                    case '$': sb.Append("\\$"); break;
                    case '&': sb.Append("\\&"); break;
                    case '%': sb.Append("\\%"); break;
                    case '#': sb.Append("\\#"); break;
                    case '_': sb.Append("\\_"); break;
                    case '~': sb.Append("\\textasciitilde{}"); break;
                    case '^': sb.Append("\\textasciicircum{}"); break;
                    case '<': sb.Append("\\textless{}"); break;
                    case '>': sb.Append("\\textgreater{}"); break;
                    case '\u00A0': sb.Append('~'); break;
                    default:
                        if (c >= 0x370 && MathSymbols.CommandFor.TryGetValue(c.ToString(), out string command)) sb.Append("\\ensuremath{").Append(command).Append('}');
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Writes a document as a LaTeX source file that pdflatex builds with the packages of a standard TeX install
    /// (amsmath, graphicx, booktabs, hyperref, geometry, enumitem, fancyhdr, lastpage, natbib).
    /// </summary>
    public sealed class LatexWriter
    {
        private readonly Document doc;
        private readonly Resolved resolved;
        private readonly string outputDirectory;
        private readonly StringBuilder sb = new StringBuilder();

        private LatexWriter(Document doc, string outputDirectory)
        {
            this.doc = doc;
            this.outputDirectory = outputDirectory;
            resolved = Resolver.Resolve(doc);
        }

        public static string Write(Document doc, string outputDirectory = null) => new LatexWriter(doc, outputDirectory ?? doc.BaseDirectory).Build();

        private DocumentStyle S => doc.Style;

        private string Build()
        {
            string size = S.FontSize <= 10.25 ? "10pt" : S.FontSize <= 11.25 ? "11pt" : "12pt";
            sb.Append("\\documentclass[").Append(size).Append(S.TitlePage ? ",titlepage" : string.Empty).Append("]{article}\n");
            sb.Append("% Written by KScript.Document. Build it with: pdflatex <file>.tex (twice, for the contents and references).\n");
            sb.Append("\\usepackage[T1]{fontenc}\n\\usepackage[utf8]{inputenc}\n");
            if (S.Font == FontFamily.Sans) sb.Append("\\usepackage[scaled]{helvet}\n\\renewcommand{\\familydefault}{\\sfdefault}\n");
            else if (S.Font == FontFamily.Mono) sb.Append("\\renewcommand{\\familydefault}{\\ttdefault}\n");
            else sb.Append("\\usepackage{lmodern}\n");
            sb.Append("\\usepackage[paperwidth=").Append(Units.N(S.PageWidth)).Append("pt,paperheight=").Append(Units.N(S.PageHeight))
                .Append("pt,top=").Append(Units.N(S.MarginTop)).Append("pt,bottom=").Append(Units.N(S.MarginBottom))
                .Append("pt,left=").Append(Units.N(S.MarginLeft)).Append("pt,right=").Append(Units.N(S.MarginRight)).Append("pt]{geometry}\n");
            sb.Append("\\usepackage{amsmath,amssymb}\n\\usepackage{graphicx}\n\\usepackage{array}\n\\usepackage{longtable}\n\\usepackage{booktabs}\n\\usepackage{enumitem}\n");
            sb.Append("\\usepackage[table]{xcolor}\n\\usepackage{fancyhdr}\n\\usepackage{lastpage}\n");
            if (S.Citations == CitationStyle.AuthorYear) sb.Append("\\usepackage[round]{natbib}\n");
            if (S.ParagraphSpacing > 0) sb.Append("\\usepackage{parskip}\n");
            if (S.Align != PaperAlign.Justify) sb.Append("\\usepackage{ragged2e}\n");
            sb.Append("\\usepackage[hidelinks]{hyperref}\n");
            var (r, g, b) = Units.Rgb(S.Accent);
            sb.Append("\\definecolor{accent}{rgb}{").Append(Units.N(r)).Append(',').Append(Units.N(g)).Append(',').Append(Units.N(b)).Append("}\n");
            if (!S.NumberSections) sb.Append("\\setcounter{secnumdepth}{0}\n");
            if (S.Accent != "#000000") sb.Append("\\usepackage{sectsty}\n\\allsectionsfont{\\color{accent}").Append(S.HeadingFont == FontFamily.Sans ? "\\sffamily" : string.Empty).Append("}\n");
            else if (S.HeadingFont == FontFamily.Sans && S.Font != FontFamily.Sans) sb.Append("\\usepackage{sectsty}\n\\allsectionsfont{\\sffamily}\n");
            HeaderFooter();
            if (S.Align == PaperAlign.Left) sb.Append("\\AtBeginDocument{\\RaggedRight}\n");

            sb.Append("\n\\title{").Append(Escape(doc.Title ?? string.Empty));
            if (doc.Subtitle != null) sb.Append("\\\\[0.5ex]\\large ").Append(Escape(doc.Subtitle));
            sb.Append("}\n\\author{").Append(string.Join(" \\and ", doc.Authors.Select(AuthorText))).Append("}\n");
            sb.Append("\\date{").Append(Escape(doc.Date ?? string.Empty)).Append("}\n");
            if (S.Citations == CitationStyle.AuthorYear) sb.Append("\\bibliographystyle{plainnat}\n");
            sb.Append("\n\\begin{document}\n");
            if (S.ShowTitle && doc.Title != null) sb.Append("\\maketitle\n");
            if (S.PlainFirstPage) sb.Append("\\thispagestyle{plain}\n");
            if (doc.Abstract.Count > 0)
            {
                sb.Append("\\begin{abstract}\n");
                Blocks(doc.Abstract);
                sb.Append("\\end{abstract}\n");
            }
            if (doc.Keywords.Count > 0) sb.Append("\\noindent\\textbf{Keywords:} ").Append(Escape(string.Join(", ", doc.Keywords))).Append("\n\n");
            Blocks(doc.Blocks);
            sb.Append("\\end{document}\n");
            return sb.ToString();
        }

        private string AuthorText(Author a)
        {
            string s = Escape(a.Name);
            if (a.Affiliation != null) s += "\\\\ " + Escape(a.Affiliation);
            if (a.Email != null) s += "\\\\ \\texttt{" + Escape(a.Email) + "}";
            return s;
        }

        private void HeaderFooter()
        {
            sb.Append("\\pagestyle{fancy}\n\\fancyhf{}\n\\renewcommand{\\headrulewidth}{0pt}\n");
            void Parts(string spec, string where)
            {
                string[] parts = (spec ?? string.Empty).Split('|');
                if (parts.Length == 1) parts = new[] { string.Empty, parts[0], string.Empty };
                string[] slots = { "L", "C", "R" };
                for (int i = 0; i < Math.Min(3, parts.Length); i++)
                {
                    string text = parts[i].Trim();
                    if (text.Length == 0) continue;
                    sb.Append("\\fancy").Append(where).Append('[').Append(slots[i]).Append("]{").Append(Fields(text)).Append("}\n");
                }
            }
            Parts(S.Header, "head");
            Parts(S.Footer, "foot");
            // plain: the first page's style (title page, letter): nothing in the header or footer.
            sb.Append("\\fancypagestyle{plain}{\\fancyhf{}}\n");
        }

        private string Fields(string text)
        {
            string escaped = Escape(text);
            return escaped.Replace("[page]", "\\thepage").Replace("[pages]", "\\pageref*{LastPage}")
                .Replace("[title]", Escape(doc.Title ?? string.Empty)).Replace("[author]", Escape(string.Join(", ", doc.Authors.Select(a => a.Name))))
                .Replace("[date]", Escape(doc.Date ?? string.Empty));
        }

        private static string Escape(string text) => LatexText.Escape(text);

        private void Blocks(IEnumerable<Block> blocks)
        {
            foreach (Block b in blocks) Block(b);
        }

        /// <summary>An id as a LaTeX label: the characters TeX reads specially (\ { } % # ~ ^ $ & and spaces) are written as their code.</summary>
        internal static string LabelName(string id)
        {
            var o = new StringBuilder(id.Length);
            foreach (char c in id)
            {
                if (char.IsLetterOrDigit(c) && c < 128 || c is '-' or '_' or ':' or '.') o.Append(c);
                else o.Append(".u").Append(((int)c).ToString("x", System.Globalization.CultureInfo.InvariantCulture)).Append('.');
            }
            return o.ToString();
        }

        /// <summary>A link's address for \href: the characters that would end or break the argument are percent-encoded.</summary>
        internal static string HrefTarget(string url)
        {
            var o = new StringBuilder(url.Length + 8);
            foreach (char c in url)
            {
                switch (c)
                {
                    case '%': o.Append("\\%"); break;
                    case '#': o.Append("\\#"); break;
                    case '&': o.Append("\\&"); break;
                    case '\\': o.Append("\\%5C"); break;
                    case '{': o.Append("\\%7B"); break;
                    case '}': o.Append("\\%7D"); break;
                    case ' ': o.Append("\\%20"); break;
                    default: o.Append(c); break;
                }
            }
            return o.ToString();
        }

        private void Label(Block b)
        {
            if (b.Id != null) sb.Append("\\label{").Append(LabelName(b.Id)).Append('}');
        }

        private void Block(Block block)
        {
            switch (block)
            {
                case Section s:
                {
                    if (s.NewPage) sb.Append("\\clearpage\n");
                    string command = s.Level switch { 1 => "section", 2 => "subsection", 3 => "subsubsection", 4 => "paragraph", _ => "subparagraph" };
                    sb.Append('\\').Append(command);
                    bool starred = !s.Numbered && S.NumberSections;
                    if (starred) sb.Append('*');
                    sb.Append('{').Append(Inlines(s.Title)).Append('}');
                    Label(s);
                    sb.Append('\n');
                    if (starred) sb.Append("\\addcontentsline{toc}{").Append(command).Append("}{").Append(Inlines(s.Title)).Append("}\n");
                    sb.Append('\n');
                    Blocks(s.Blocks);
                    break;
                }
                case Paragraph p:
                    Paragraph(p);
                    break;
                case ListBlock list:
                {
                    string env = list.Ordered ? "enumerate" : "itemize";
                    sb.Append("\\begin{").Append(env).Append('}');
                    if (list.Ordered)
                    {
                        string label = list.Style switch { ListStyle.Letter => "\\alph*.", ListStyle.Roman => "\\roman*.", _ => "\\arabic*." };
                        sb.Append("[label=").Append(label).Append(list.Start != 1 ? ",start=" + list.Start : string.Empty).Append(']');
                    }
                    sb.Append('\n');
                    foreach (ListItem item in list.Items)
                    {
                        sb.Append("\\item ");
                        int length = sb.Length;
                        Blocks(item.Blocks);
                        if (sb.Length == length) sb.Append('\n');
                    }
                    sb.Append("\\end{").Append(env).Append("}\n\n");
                    break;
                }
                case Table t:
                    Table(t);
                    break;
                case Figure f:
                {
                    sb.Append("\\begin{figure}[htbp]\n\\centering\n\\includegraphics[width=").Append(Units.N(f.Width > 0 ? f.Width : 0.8)).Append("\\textwidth]{")
                        .Append(ImagePath(f.Source)).Append("}\n");
                    if (f.Caption != null)
                    {
                        sb.Append("\\caption{").Append(Inlines(f.Caption)).Append('}');
                        Label(f);
                        sb.Append('\n');
                    }
                    sb.Append("\\end{figure}\n\n");
                    break;
                }
                case MathBlock m:
                {
                    // Display maths belongs to the paragraphs around it: no blank lines, so the text after isn't indented.
                    string env = m.Numbered ? "equation" : "equation*";
                    if (sb.Length > 1 && sb[^1] == '\n' && sb[^2] == '\n') sb.Length--;
                    sb.Append("\\begin{").Append(env).Append('}');
                    if (m.Numbered) Label(m);
                    sb.Append('\n').Append(MathLatex.Write(m.Node)).Append("\n\\end{").Append(env).Append("}\n");
                    break;
                }
                case CodeBlock c:
                    sb.Append("\\begin{verbatim}\n").Append(c.Text.Replace("\\end{verbatim}", "\\end {verbatim}")).Append("\n\\end{verbatim}\n\n");
                    break;
                case Quote q:
                    sb.Append("\\begin{quote}\n");
                    Blocks(q.Blocks);
                    if (q.Attribution != null) sb.Append("\\hfill--- ").Append(Escape(q.Attribution)).Append('\n');
                    sb.Append("\\end{quote}\n\n");
                    break;
                case PageBreak:
                    sb.Append("\\clearpage\n\n");
                    break;
                case Contents c:
                    sb.Append("\\renewcommand{\\contentsname}{").Append(Escape(c.Title)).Append("}\n\\setcounter{tocdepth}{").Append(c.Depth).Append("}\n\\tableofcontents\n");
                    if (S.TitlePage) sb.Append("\\clearpage\n");
                    sb.Append('\n');
                    break;
                case Bibliography bib:
                    Bibliography(bib);
                    break;
                case Spacer sp:
                    sb.Append("\\vspace{").Append(Units.N(sp.Height)).Append("pt}\n\n");
                    break;
                case Rule:
                    sb.Append("\\noindent\\rule{\\textwidth}{0.4pt}\n\n");
                    break;
            }
        }

        private void Paragraph(Paragraph p)
        {
            string env = p.Align switch { PaperAlign.Right => "flushright", PaperAlign.Center => "center", PaperAlign.Left when S.Align == PaperAlign.Justify => "flushleft", _ => null };
            if (env != null) sb.Append("\\begin{").Append(env).Append("}\n");
            if (p.SpaceBefore > 0) sb.Append("\\vspace*{").Append(Units.N(p.SpaceBefore)).Append("pt}");
            if (p.NoIndent || S.ParagraphSpacing == 0 && p.Align != null) sb.Append("\\noindent ");
            string size = p.Scale >= 1.7 ? "\\LARGE " : p.Scale >= 1.4 ? "\\Large " : p.Scale >= 1.15 ? "\\large " : p.Scale <= 0.85 ? "\\small " : null;
            string text = Inlines(p.Inlines);
            if (p.Bold) text = "\\textbf{" + text + "}";
            if (p.Color != null)
            {
                var (r, g, b) = Units.Rgb(p.Color);
                text = "\\textcolor[rgb]{" + Units.N(r) + "," + Units.N(g) + "," + Units.N(b) + "}{" + text + "}";
            }
            if (size != null) text = "{" + size + text + "\\par}";
            sb.Append(text).Append('\n');
            if (env != null) sb.Append("\\end{").Append(env).Append("}\n");
            sb.Append('\n');
        }

        private string ImagePath(string source)
        {
            string full = Path.GetFullPath(Path.Combine(doc.BaseDirectory, source));
            string relative = Path.GetRelativePath(outputDirectory, full).Replace('\\', '/');
            if (relative.Contains(' ')) relative = "\"" + relative + "\"";
            // \detokenize keeps % # & $ ~ ^ in a file name as the characters they are.
            return relative.IndexOfAny(new[] { '%', '#', '&', '$', '~', '^' }) >= 0 ? "\\detokenize{" + relative + "}" : relative;
        }

        /// <summary>Tables with more rows than this are written as a longtable, since they may not fit on a page.</summary>
        internal const int LongTableRows = 30;

        private void Table(Table t)
        {
            // A table with no rows (a query that found nothing) has nothing to show, and LaTeX won't take one.
            if (t.Rows.Count == 0) return;
            int columns = t.ColumnCount;
            bool grid = t.Rules == TableRules.Grid;
            var spec = new StringBuilder(grid ? "|" : string.Empty);
            double total = t.Widths.Count > 0 ? t.Widths.Take(columns).Sum() : 0;
            for (int c = 0; c < columns; c++)
            {
                ColumnAlign a = t.AlignOf(c);
                if (total > 0)
                {
                    double share = (c < t.Widths.Count ? t.Widths[c] : 1) / total * 0.95;
                    string ragged = a switch { ColumnAlign.Right => ">{\\raggedleft\\arraybackslash}", ColumnAlign.Center => ">{\\centering\\arraybackslash}", _ => ">{\\raggedright\\arraybackslash}" };
                    spec.Append(ragged).Append("p{").Append(Units.N(share)).Append("\\textwidth}");
                }
                else
                {
                    spec.Append(a switch { ColumnAlign.Right => 'r', ColumnAlign.Center => 'c', _ => 'l' });
                }
                if (grid) spec.Append('|');
            }
            bool floating = t.Caption != null;
            // A long table goes in a longtable, which carries on over the page (a tabular can't) and repeats its heading rows.
            bool breaks = t.Rows.Count > LongTableRows;
            int headerRows = t.Rows.TakeWhile(r => r.Header).Count();
            if (breaks)
            {
                sb.Append(t.Scale < 1 ? "{\\small\n" : string.Empty).Append("\\begin{longtable}{").Append(spec).Append("}\n");
                if (floating)
                {
                    sb.Append("\\caption{").Append(Inlines(t.Caption)).Append('}');
                    Label(t);
                    sb.Append(" \\\\\n");
                }
            }
            else
            {
                sb.Append(floating ? "\\begin{table}[htbp]\n\\centering\n" : "\\begin{center}\n");
                if (floating)
                {
                    sb.Append("\\caption{").Append(Inlines(t.Caption)).Append('}');
                    Label(t);
                    sb.Append('\n');
                }
                if (t.Scale < 1) sb.Append("\\small\n");
                sb.Append("\\begin{tabular}{").Append(spec).Append("}\n");
            }
            string top = t.Rules switch { TableRules.Booktabs => "\\toprule\n", TableRules.Grid => "\\hline\n", _ => string.Empty };
            int headStart = sb.Length;
            sb.Append(top);
            for (int i = 0; i < t.Rows.Count; i++)
            {
                TableRow row = t.Rows[i];
                if (row.RuleAbove && i > 0) sb.Append(t.Rules == TableRules.Booktabs ? "\\midrule\n" : t.Rules == TableRules.None ? "\\hline\n" : string.Empty);
                int column = 0;
                var cells = new List<string>();
                foreach (TableCell cell in row.Cells)
                {
                    string text = Inlines(cell.Inlines);
                    if (cell.Bold || row.Header) text = "\\textbf{" + text + "}";
                    int span = Math.Max(1, cell.Span);
                    if (span > 1)
                    {
                        char align = t.AlignOf(column + span - 1) switch { ColumnAlign.Right => 'r', ColumnAlign.Center => 'c', _ => 'l' };
                        text = "\\multicolumn{" + span + "}{" + (grid ? "|" : string.Empty) + align + (grid ? "|" : string.Empty) + "}{" + text + "}";
                    }
                    cells.Add(text);
                    column += span;
                }
                sb.Append(string.Join(" & ", cells)).Append(" \\\\\n");
                bool lastHeader = row.Header && (i + 1 >= t.Rows.Count || !t.Rows[i + 1].Header);
                if (grid) sb.Append("\\hline\n");
                else if (lastHeader && t.Rules == TableRules.Booktabs) sb.Append("\\midrule\n");
                if (breaks && headerRows > 0 && i == headerRows - 1)
                {
                    // The first page has the caption above the heading rows; the pages after it repeat the heading rows alone.
                    string head = sb.ToString(headStart, sb.Length - headStart);
                    sb.Append("\\endfirsthead\n").Append(head).Append("\\endhead\n");
                }
            }
            if (t.Rules == TableRules.Booktabs) sb.Append("\\bottomrule\n");
            if (breaks)
            {
                sb.Append("\\end{longtable}\n").Append(t.Scale < 1 ? "}\n\n" : "\n");
                return;
            }
            sb.Append("\\end{tabular}\n");
            sb.Append(floating ? "\\end{table}\n\n" : "\\end{center}\n\n");
        }

        private void Bibliography(Bibliography bib)
        {
            if (resolved.Cited.Count == 0) return;
            sb.Append("\\renewcommand{\\refname}{").Append(Escape(bib.Title)).Append("}\n");
            sb.Append("\\begin{thebibliography}{").Append(resolved.Cited.Count >= 10 ? "99" : "9").Append("}\n");
            foreach (Source s in resolved.Cited)
            {
                sb.Append("\\bibitem");
                if (S.Citations == CitationStyle.AuthorYear)
                {
                    string label = BibTex.AuthorYear(s);
                    int comma = label.LastIndexOf(", ", StringComparison.Ordinal);
                    sb.Append('[').Append(Escape(label.Substring(0, comma))).Append('(').Append(Escape(label.Substring(comma + 2))).Append(")]");
                }
                sb.Append('{').Append(s.Key).Append("} ").Append(Inlines(BibTex.Format(s, S.Citations))).Append('\n');
            }
            sb.Append("\\end{thebibliography}\n\n");
        }

        private string Inlines(IEnumerable<Inline> inlines)
        {
            var o = new StringBuilder();
            foreach (Inline i in inlines ?? Enumerable.Empty<Inline>())
            {
                switch (i)
                {
                    case TextInline t:
                        o.Append(Escape(t.Text));
                        break;
                    case StrongInline s:
                        o.Append("\\textbf{").Append(Inlines(s.Children)).Append('}');
                        break;
                    case EmphasisInline e:
                        o.Append("\\emph{").Append(Inlines(e.Children)).Append('}');
                        break;
                    case CodeInline c:
                        o.Append("\\texttt{").Append(Escape(c.Text)).Append('}');
                        break;
                    case LinkInline l:
                        o.Append("\\href{").Append(HrefTarget(l.Url)).Append("}{").Append(Inlines(l.Children)).Append('}');
                        break;
                    case MathInline m:
                        o.Append('$').Append(MathLatex.Write(m.Node)).Append('$');
                        break;
                    case CiteInline c:
                        o.Append(S.Citations == CitationStyle.AuthorYear ? "\\citep{" : "\\cite{").Append(string.Join(",", c.Keys)).Append('}');
                        break;
                    case RefInline r:
                        o.Append(resolved.Targets[r.Id] is MathBlock ? "\\eqref{" : "\\ref{").Append(LabelName(r.Id)).Append('}');
                        break;
                    case FootnoteInline f:
                        o.Append("\\footnote{").Append(Inlines(f.Children)).Append('}');
                        break;
                    case LineBreakInline:
                        o.Append("\\\\\n");
                        break;
                }
            }
            return o.ToString();
        }
    }
}
