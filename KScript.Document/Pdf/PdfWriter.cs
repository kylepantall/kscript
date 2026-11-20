using KScript.Documents.Maths;
using KScript.Documents.Text;

namespace KScript.Documents.Pdf
{
    /// <summary>
    /// Lays a document out on pages and writes it as a PDF, without TeX or any other program: line breaking and
    /// justification, numbered headings, lists, tables that continue over pages, figures, equations, footnotes at
    /// the foot of their page, a contents page with page numbers, links, bookmarks, headers and footers.
    /// </summary>
    public sealed class PdfWriter
    {
        private readonly Document doc;
        private readonly Resolved resolved;
        private readonly DocumentStyle S;
        private readonly Dictionary<Section, int> knownPages;

        private readonly List<PdfPage> pages = new List<PdfPage>();
        private readonly Dictionary<PdfPage, List<FootnoteInline>> pageNotes = new Dictionary<PdfPage, List<FootnoteInline>>();
        private readonly Dictionary<PdfPage, double> noteHeights = new Dictionary<PdfPage, double>();
        private readonly Dictionary<string, (int Page, double Y)> destinations = new Dictionary<string, (int, double)>(StringComparer.Ordinal);
        private readonly List<Bookmark> bookmarks = new List<Bookmark>();
        private readonly Dictionary<Section, int> sectionPages = new Dictionary<Section, int>();
        private PdfPage page;
        private double y;
        private bool pageEmpty = true;
        private bool indentNext;

        private PdfWriter(Document doc, Resolved resolved, Dictionary<Section, int> knownPages)
        {
            this.doc = doc;
            this.resolved = resolved;
            S = doc.Style;
            this.knownPages = knownPages;
        }

        public static byte[] Write(Document doc)
        {
            Resolved resolved = Resolver.Resolve(doc);
            foreach (Figure f in doc.AllBlocks().OfType<Figure>()) f.Image ??= ImageData.Load(Path.GetFullPath(Path.Combine(doc.BaseDirectory, f.Source)));
            // Twice: the contents page shows page numbers that are only known once everything is laid out.
            var first = new PdfWriter(doc, resolved, null);
            first.Run();
            PdfWriter writer = first;
            if (doc.AllBlocks().Any(b => b is Contents))
            {
                writer = new PdfWriter(doc, resolved, first.sectionPages);
                writer.Run();
            }
            return writer.Finish();
        }

        /// <summary>How many pages a document takes.</summary>
        public static int CountPages(Document doc)
        {
            Resolved resolved = Resolver.Resolve(doc);
            foreach (Figure f in doc.AllBlocks().OfType<Figure>()) f.Image ??= ImageData.Load(Path.GetFullPath(Path.Combine(doc.BaseDirectory, f.Source)));
            var writer = new PdfWriter(doc, resolved, null);
            writer.Run();
            return writer.pages.Count;
        }

        private double Top => S.MarginTop;
        private double Bottom => S.PageHeight - S.MarginBottom;
        private double Lead => S.FontSize * S.LineHeight;
        private double ParagraphGap => S.ParagraphSpacing * Lead;

        private RunStyle Body(double scale = 1, bool italic = false) => new RunStyle { Face = new Face(S.Font, false, italic), Size = S.FontSize * scale };

        private readonly record struct Frame(double X, double Width, bool Italic = false, double Scale = 1);

        private void Run()
        {
            NewPage();
            Frame frame = new Frame(S.MarginLeft, S.TextWidth);
            if (S.ShowTitle && (doc.Title != null || doc.Authors.Count > 0)) TitleBlock(frame);
            else if (doc.Abstract.Count > 0) Abstract(frame);
            Blocks(doc.Blocks, frame);
        }

        private void NewPage()
        {
            page = new PdfPage();
            pages.Add(page);
            pageNotes[page] = new List<FootnoteInline>();
            noteHeights[page] = 0;
            y = Top;
            pageEmpty = true;
        }

        /// <summary>The lowest y content may reach on this page, above its footnotes.</summary>
        private double Limit => Bottom - noteHeights[page];

        /// <summary>Moves to a new page unless <paramref name="height"/> fits on this one.</summary>
        private void Need(double height)
        {
            if (!pageEmpty && y + height > Limit) NewPage();
        }

        private void Gap(double height)
        {
            if (!pageEmpty) y += height;
        }

        private int PageIndex => pages.Count - 1;

        private void Mark(string id)
        {
            if (id != null) destinations[id] = (PageIndex, Math.Max(0, y - S.FontSize));
        }

        // ---- text --------------------------------------------------------------------------------------------------

        private (double Height, double Baseline) Metrics(Line line, double size)
        {
            double ascent = Math.Max(line.Ascent, size * 0.72), descent = Math.Max(line.Descent, size * 0.22);
            double height = Math.Max(size * S.LineHeight, ascent + descent + size * 0.2);
            return (height, (height - (ascent + descent)) / 2 + ascent);
        }

        private double NotesHeight(IEnumerable<FootnoteInline> notes, bool first)
        {
            double size = S.FontSize * 0.82;
            double h = 0;
            foreach (FootnoteInline n in notes)
            {
                var flow = new TextFlow(n.Children, Body(0.82), S.Font);
                h += flow.Break(S.TextWidth - size * 1.6).Count * size * 1.25;
            }
            return h + (first && h > 0 ? S.FontSize * 1.2 : 0);
        }

        /// <summary>Places lines on the page, starting new pages as needed. Returns the baseline of the first line.</summary>
        private double Lines(List<Line> lines, Frame f, PaperAlign align, double size, Action<double> onFirstBaseline = null, int keepFirst = 2)
        {
            double firstBaseline = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                Line line = lines[i];
                var (height, baseline) = Metrics(line, size);
                var notes = line.Notes.ToList();
                double notesHeight = notes.Count > 0 ? NotesHeight(notes, pageNotes[page].Count == 0) : 0;
                double needed = height;
                if (i == 0 && lines.Count > 1 && keepFirst > 1) needed += Metrics(lines[1], size).Height;
                if (!pageEmpty && y + needed + notesHeight > Limit)
                {
                    NewPage();
                    notesHeight = notes.Count > 0 ? NotesHeight(notes, true) : 0;
                }
                if (notes.Count > 0)
                {
                    pageNotes[page].AddRange(notes);
                    noteHeights[page] += notesHeight;
                }
                TextFlow.Draw(line, f.X, y + baseline, f.Width, align, page);
                if (i == 0)
                {
                    firstBaseline = y + baseline;
                    onFirstBaseline?.Invoke(firstBaseline);
                }
                y += height;
                pageEmpty = false;
            }
            return firstBaseline;
        }

        private void Text(List<Inline> inlines, Frame f, RunStyle style, PaperAlign align, double firstIndent = 0, Action<double> onFirstBaseline = null)
        {
            var flow = new TextFlow(inlines, style, S.Font);
            Lines(flow.Break(f.Width, firstIndent), f, align, style.Size, onFirstBaseline);
        }

        // ---- blocks ------------------------------------------------------------------------------------------------

        private void Blocks(IEnumerable<Block> blocks, Frame f, Action<double> marker = null)
        {
            foreach (Block b in blocks)
            {
                Block(b, f, marker);
                marker = null;
            }
        }

        private void Block(Block block, Frame f, Action<double> marker)
        {
            switch (block)
            {
                case Section s:
                    Heading(s, f);
                    Blocks(s.Blocks, f);
                    break;
                case Paragraph p:
                    Paragraph(p, f, marker);
                    break;
                case ListBlock list:
                    List(list, f, marker);
                    break;
                case Table t:
                    Table(t, f);
                    break;
                case Figure fig:
                    Figure(fig, f);
                    break;
                case MathBlock m:
                    Equation(m, f);
                    break;
                case CodeBlock c:
                    Code(c, f);
                    break;
                case Quote q:
                {
                    Gap(Lead * 0.3);
                    double inset = S.FontSize * 1.5;
                    var inner = new Frame(f.X + inset, f.Width - inset * 2, Italic: true, Scale: f.Scale);
                    int startPage = PageIndex;
                    double startY = y;
                    Blocks(q.Blocks, inner);
                    if (q.Attribution != null) Text(new List<Inline> { new TextInline("— " + q.Attribution) }, inner, Body(f.Scale), PaperAlign.Right);
                    if (PageIndex == startPage) page.Ops.Add(new LineOp { X1 = f.X + inset * 0.4, Y1 = startY, X2 = f.X + inset * 0.4, Y2 = y - ParagraphGap, Width = 1.2, Colour = "#bbbbbb" });
                    Gap(Lead * 0.3);
                    indentNext = false;
                    break;
                }
                case PageBreak:
                    if (!pageEmpty) NewPage();
                    break;
                case Spacer sp:
                    y += sp.Height;
                    if (y > Limit) NewPage();
                    break;
                case Rule:
                    Gap(Lead * 0.3);
                    Need(Lead * 0.6);
                    page.Ops.Add(new LineOp { X1 = f.X, Y1 = y + Lead * 0.3, X2 = f.X + f.Width, Y2 = y + Lead * 0.3, Width = 0.5, Colour = "#000000" });
                    y += Lead * 0.6;
                    pageEmpty = false;
                    indentNext = false;
                    break;
                case Contents c:
                    Contents(c, f);
                    break;
                case Bibliography bib:
                    Bibliography(bib, f);
                    break;
            }
        }

        private static readonly double[] HeadingScales = { 1.45, 1.2, 1.05, 1, 1, 1 };

        private void Heading(Section s, Frame f)
        {
            if (s.NewPage && !pageEmpty) NewPage();
            double size = S.FontSize * HeadingScales[Math.Min(5, s.Level - 1)];
            var style = new RunStyle { Face = new Face(S.HeadingFont, true, s.Level >= 4 && S.HeadingFont == FontFamily.Serif), Size = size, Colour = S.Accent };
            var inlines = new List<Inline>();
            if (s.Number != null) inlines.Add(new TextInline(s.Number + "  "));
            inlines.AddRange(s.Title);
            var lines = new TextFlow(inlines, style, S.HeadingFont).Break(f.Width);
            double height = lines.Sum(l => Metrics(l, size).Height);
            Gap(size * (s.Level == 1 ? 1.1 : 0.8));
            // Keep the heading with the first lines after it.
            Need(height + size * 0.4 + Lead * 2.2);
            Mark(s.Id);
            bookmarks.Add(new Bookmark { Title = (s.Number != null ? s.Number + " " : string.Empty) + InlineParser.Plain(s.Title), Level = s.Level, Page = PageIndex, Y = Math.Max(0, y - size) });
            sectionPages[s] = PageIndex + 1;
            Lines(lines, f, PaperAlign.Left, size, keepFirst: 1);
            y += size * 0.4;
            indentNext = false;
        }

        private void Paragraph(Paragraph p, Frame f, Action<double> marker)
        {
            double scale = f.Scale * p.Scale;
            RunStyle style = Body(scale, f.Italic);
            if (p.Bold) style.Face = style.Face.WithBold();
            if (p.Color != null) style.Colour = Units.Colour(p.Color);
            if (p.SpaceBefore > 0) y += p.SpaceBefore;
            PaperAlign align = p.Align ?? S.Align;
            bool indent = S.ParagraphSpacing == 0 && indentNext && !p.NoIndent && p.Align == null && marker == null;
            var flow = new TextFlow(p.Inlines, style, S.Font);
            var lines = flow.Break(f.Width, indent ? S.FontSize * 1.5 : 0);
            Mark(p.Id);
            Lines(lines, f, align, style.Size, marker);
            y += marker != null ? 0 : ParagraphGap;
            indentNext = true;
        }

        private void List(ListBlock list, Frame f, Action<double> outer)
        {
            double indent = S.FontSize * (list.Ordered ? 2.0 : 1.6);
            if (outer == null) Gap(ParagraphGap > 0 ? 0 : Lead * 0.25);
            for (int i = 0; i < list.Items.Count; i++)
            {
                ListItem item = list.Items[i];
                var inner = new Frame(f.X + indent, f.Width - indent, f.Italic, f.Scale);
                RunStyle markerStyle = Body(f.Scale, f.Italic && list.Ordered);
                string text = list.Marker(i);
                double right = f.X + indent - S.FontSize * 0.5;
                // A list that starts an item shares the item's first line, so it draws the outer marker too.
                Action<double> outerMarker = i == 0 ? outer : null;
                void Marker(double baseline)
                {
                    outerMarker?.Invoke(baseline);
                    DrawText(text, markerStyle, right - StandardFonts.Measure(text, markerStyle.Face, markerStyle.Size), baseline);
                }
                if (item.Blocks.Count == 0) item.Blocks.Add(new Paragraph());
                Blocks(item.Blocks, inner, Marker);
                if (i < list.Items.Count - 1) y += Lead * 0.15;
            }
            y += ParagraphGap > 0 ? ParagraphGap : Lead * 0.25;
            indentNext = false;
        }

        private void DrawText(string text, RunStyle style, double x, double baseline)
        {
            foreach (var (font, bytes, w) in StandardFonts.Runs(text, style.Face, style.Size))
            {
                page.Ops.Add(new TextOp { Font = font, Size = style.Size, X = x, Y = baseline, Bytes = bytes, Colour = style.Colour });
                x += w;
            }
        }

        private void Equation(MathBlock m, Frame f)
        {
            double size = S.FontSize * f.Scale;
            string number = m.Numbered ? $"({m.Number})" : null;
            RunStyle numberStyle = Body(f.Scale);
            double numberWidth = number != null ? StandardFonts.Measure(number, numberStyle.Face, numberStyle.Size) + size : 0;
            MathBox box = MathLayout.Layout(m.Node, size, display: true, "#000000", S.Font);
            double room = f.Width - 2 * numberWidth;
            if (box.Width > room && box.Width > 0) box = MathLayout.Layout(m.Node, size * Math.Max(0.5, room / box.Width), display: true, "#000000", S.Font);
            double above = size * 0.7, below = size * 0.7;
            Gap(above);
            double height = box.Ascent + box.Descent;
            Need(height + below);
            Mark(m.Id);
            double baseline = y + box.Ascent;
            double x = f.X + (f.Width - box.Width) / 2;
            foreach (Op op in box.Ops) page.Ops.Add(op.Moved(x, baseline));
            if (number != null)
            {
                double nx = f.X + f.Width - StandardFonts.Measure(number, numberStyle.Face, numberStyle.Size);
                foreach (var (font, bytes, w) in StandardFonts.Runs(number, numberStyle.Face, numberStyle.Size))
                {
                    page.Ops.Add(new TextOp { Font = font, Size = numberStyle.Size, X = nx, Y = baseline, Bytes = bytes, Colour = "#000000" });
                    nx += w;
                }
            }
            y += height + below;
            pageEmpty = false;
            indentNext = false;
        }

        private void Code(CodeBlock c, Frame f)
        {
            double size = S.FontSize * 0.86 * f.Scale;
            var face = new Face(FontFamily.Mono, false, false);
            double lead = size * 1.25;
            double pad = size * 0.6;
            double charWidth = StandardFonts.Measure("m", face, size);
            int perLine = Math.Max(10, (int)((f.Width - 2 * pad) / charWidth));
            var lines = new List<string>();
            foreach (string raw in c.Text.Replace("\t", "    ").Split('\n'))
            {
                string rest = raw;
                do
                {
                    lines.Add(rest.Length > perLine ? rest.Substring(0, perLine) : rest);
                    rest = rest.Length > perLine ? rest.Substring(perLine) : string.Empty;
                }
                while (rest.Length > 0);
            }
            Gap(Lead * 0.3);
            Need(pad * 2 + lead * Math.Min(3, lines.Count));
            Mark(c.Id);
            int start = 0;
            while (start < lines.Count)
            {
                int fit = Math.Max(1, (int)((Limit - y - 2 * pad) / lead));
                if (!pageEmpty && fit < 1) NewPage();
                int count = Math.Min(fit, lines.Count - start);
                double top = y;
                page.Ops.Add(new RectOp { X = f.X, Y = top, W = f.Width, H = count * lead + 2 * pad, Fill = "#f4f4f4" });
                for (int i = 0; i < count; i++)
                {
                    string text = lines[start + i];
                    if (text.Length == 0) continue;
                    double x = f.X + pad;
                    foreach (var (font, bytes, w) in StandardFonts.Runs(text, face, size))
                    {
                        page.Ops.Add(new TextOp { Font = font, Size = size, X = x, Y = top + pad + i * lead + size * 0.85, Bytes = bytes, Colour = "#222222" });
                        x += w;
                    }
                }
                y = top + count * lead + 2 * pad;
                pageEmpty = false;
                start += count;
                if (start < lines.Count) NewPage();
            }
            y += Math.Max(ParagraphGap, Lead * 0.4);
            indentNext = false;
        }

        private void Figure(Figure fig, Frame f)
        {
            ImageData image = fig.Image;
            double width = fig.Width > 0 ? f.Width * Math.Min(1, fig.Width) : Math.Min(f.Width, image.Width * 0.75);
            double height = width * image.Height / Math.Max(1, image.Width);
            List<Line> caption = null;
            double captionHeight = 0;
            RunStyle captionStyle = Body(0.92 * f.Scale);
            if (fig.Caption != null)
            {
                var inlines = new List<Inline> { Bold($"Figure {fig.Number}: ") };
                inlines.AddRange(fig.Caption);
                caption = new TextFlow(inlines, captionStyle, S.Font).Break(f.Width * 0.9);
                captionHeight = caption.Sum(l => Metrics(l, captionStyle.Size).Height) + S.FontSize * 0.5;
            }
            double room = Bottom - Top - captionHeight - S.FontSize * 2;
            if (height > room)
            {
                width *= room / height;
                height = room;
            }
            Gap(Lead * 0.5);
            Need(height + captionHeight);
            Mark(fig.Id);
            page.Ops.Add(new ImageOp { Image = image, X = f.X + (f.Width - width) / 2, Y = y, W = width, H = height });
            y += height + S.FontSize * 0.5;
            pageEmpty = false;
            if (caption != null) Lines(caption, new Frame(f.X + f.Width * 0.05, f.Width * 0.9), caption.Count == 1 ? PaperAlign.Center : PaperAlign.Justify, captionStyle.Size);
            y += Lead * 0.5;
            indentNext = false;
        }

        private static StrongInline Bold(string text)
        {
            var s = new StrongInline();
            s.Children.Add(new TextInline(text));
            return s;
        }

        private void Table(Table t, Frame f)
        {
            int columns = t.ColumnCount;
            if (columns == 0) return;
            double size = S.FontSize * t.Scale * f.Scale;
            double padX = size * 0.5, padY = size * 0.25;
            RunStyle CellStyle(TableRow row, TableCell cell)
            {
                RunStyle s = Body(t.Scale * f.Scale, f.Italic);
                if (row.Header || cell.Bold) s.Face = s.Face.WithBold();
                return s;
            }
            var flows = t.Rows.Select(r => r.Cells.Select(c => new TextFlow(c.Inlines, CellStyle(r, c), S.Font)).ToList()).ToList();

            // Column widths: relative widths across the text, or sized to the content.
            var widths = new double[columns];
            if (t.Widths.Count > 0)
            {
                double total = Enumerable.Range(0, columns).Sum(c => c < t.Widths.Count ? t.Widths[c] : 1);
                for (int c = 0; c < columns; c++) widths[c] = f.Width * (c < t.Widths.Count ? t.Widths[c] : 1) / total;
            }
            else
            {
                var natural = new double[columns];
                var min = new double[columns];
                for (int r = 0; r < t.Rows.Count; r++)
                {
                    int c = 0;
                    for (int k = 0; k < t.Rows[r].Cells.Count; k++)
                    {
                        int span = Math.Max(1, t.Rows[r].Cells[k].Span);
                        if (span == 1 && c < columns)
                        {
                            natural[c] = Math.Max(natural[c], flows[r][k].NaturalWidth + 2 * padX);
                            min[c] = Math.Max(min[c], flows[r][k].MinWidth + 2 * padX);
                        }
                        c += span;
                    }
                }
                double sum = natural.Sum();
                if (sum <= f.Width)
                {
                    natural.CopyTo(widths, 0);
                }
                else
                {
                    double minSum = min.Sum();
                    double spare = Math.Max(0, f.Width - minSum);
                    double flexible = natural.Zip(min, (n, m) => n - m).Sum();
                    for (int c = 0; c < columns; c++) widths[c] = min[c] + (flexible > 0 ? spare * (natural[c] - min[c]) / flexible : spare / columns);
                }
            }
            double tableWidth = widths.Sum();
            double x0 = f.X + Math.Max(0, (f.Width - tableWidth) / 2);

            // Lay out every cell's lines now, so row heights are known.
            var cellLines = new List<List<(List<Line> Lines, double X, double Width, ColumnAlign Align)>>();
            var rowHeights = new List<double>();
            for (int r = 0; r < t.Rows.Count; r++)
            {
                var row = new List<(List<Line>, double, double, ColumnAlign)>();
                double h = 0;
                int c = 0;
                for (int k = 0; k < t.Rows[r].Cells.Count && c < columns; k++)
                {
                    int span = Math.Min(Math.Max(1, t.Rows[r].Cells[k].Span), columns - c);
                    double cx = x0 + widths.Take(c).Sum();
                    double cw = widths.Skip(c).Take(span).Sum();
                    List<Line> lines = flows[r][k].Break(Math.Max(1, cw - 2 * padX));
                    h = Math.Max(h, lines.Sum(l => Metrics(l, size).Height));
                    row.Add((lines, cx, cw, t.AlignOf(c + span - 1)));
                    c += span;
                }
                cellLines.Add(row);
                rowHeights.Add(h + 2 * padY);
            }

            List<Line> caption = null;
            RunStyle captionStyle = Body(0.92 * f.Scale);
            double captionHeight = 0;
            if (t.Caption != null)
            {
                var inlines = new List<Inline> { Bold($"Table {t.Number}: ") };
                inlines.AddRange(t.Caption);
                caption = new TextFlow(inlines, captionStyle, S.Font).Break(Math.Max(tableWidth, f.Width * 0.6));
                captionHeight = caption.Sum(l => Metrics(l, captionStyle.Size).Height) + size * 0.3;
            }
            Gap(Lead * 0.5);
            int headerRows = t.Rows.TakeWhile(r => r.Header).Count();
            double headerHeight = rowHeights.Take(headerRows).Sum();
            Need(captionHeight + headerHeight + (rowHeights.Count > headerRows ? rowHeights[headerRows] : 0));
            Mark(t.Id);
            if (caption != null)
            {
                double cw = Math.Max(tableWidth, f.Width * 0.6);
                Lines(caption, new Frame(f.X + (f.Width - cw) / 2, cw), caption.Count == 1 ? PaperAlign.Center : PaperAlign.Justify, captionStyle.Size);
                y += size * 0.3;
            }
            double thick = 0.8, thin = 0.4;
            void HLine(double at, double width) => page.Ops.Add(new LineOp { X1 = x0, Y1 = at, X2 = x0 + tableWidth, Y2 = at, Width = width, Colour = "#000000" });
            void DrawRow(int r)
            {
                double top = y;
                if (t.Rules == TableRules.Grid)
                {
                    HLine(top, thin);
                    double vx = x0;
                    page.Ops.Add(new LineOp { X1 = vx, Y1 = top, X2 = vx, Y2 = top + rowHeights[r], Width = thin, Colour = "#000000" });
                    foreach (var cell in cellLines[r])
                    {
                        vx = cell.X + cell.Width;
                        page.Ops.Add(new LineOp { X1 = vx, Y1 = top, X2 = vx, Y2 = top + rowHeights[r], Width = thin, Colour = "#000000" });
                    }
                }
                else if (t.Rows[r].RuleAbove && r > 0)
                {
                    HLine(top, thin);
                }
                foreach (var (lines, cx, cw, align) in cellLines[r])
                {
                    double ly = top + padY;
                    foreach (Line line in lines)
                    {
                        var (h, b) = Metrics(line, size);
                        TextFlow.Draw(line, cx + padX, ly + b, cw - 2 * padX, align switch { ColumnAlign.Right => PaperAlign.Right, ColumnAlign.Center => PaperAlign.Center, _ => PaperAlign.Left }, page);
                        ly += h;
                    }
                }
                y += rowHeights[r];
                pageEmpty = false;
            }
            void Header(bool continued)
            {
                if (t.Rules == TableRules.Booktabs) HLine(y, thick);
                for (int r = 0; r < headerRows; r++) DrawRow(r);
                if (headerRows > 0 && t.Rules == TableRules.Booktabs) HLine(y, thin);
            }
            Header(false);
            for (int r = headerRows; r < t.Rows.Count; r++)
            {
                if (!pageEmpty && y + rowHeights[r] > Limit)
                {
                    if (t.Rules == TableRules.Booktabs) HLine(y, thick);
                    else if (t.Rules == TableRules.Grid) HLine(y, thin);
                    NewPage();
                    Header(true);
                }
                DrawRow(r);
            }
            if (t.Rules == TableRules.Booktabs) HLine(y, thick);
            else if (t.Rules == TableRules.Grid) HLine(y, thin);
            y += Lead * 0.6;
            indentNext = false;
        }

        // ---- title, contents and references --------------------------------------------------------------------

        private void TitleBlock(Frame f)
        {
            bool sans = S.HeadingFont == FontFamily.Sans;
            if (S.TitlePage) y = S.PageHeight * 0.28;
            if (doc.Title != null)
            {
                var style = new RunStyle { Face = new Face(S.HeadingFont, sans || S.TitlePage, false), Size = S.FontSize * (S.TitlePage ? 2.2 : 1.75), Colour = S.TitlePage ? S.Accent : "#000000" };
                Text(new List<Inline> { new TextInline(doc.Title) }, f, style, S.TitlePage ? PaperAlign.Left : PaperAlign.Center);
            }
            if (doc.Subtitle != null)
            {
                y += S.FontSize * 0.2;
                Text(new List<Inline> { new TextInline(doc.Subtitle) }, f, new RunStyle { Face = new Face(S.HeadingFont, false, false), Size = S.FontSize * 1.25, Colour = "#333333" }, S.TitlePage ? PaperAlign.Left : PaperAlign.Center);
            }
            if (S.TitlePage)
            {
                y += S.FontSize;
                page.Ops.Add(new LineOp { X1 = f.X, Y1 = y, X2 = f.X + f.Width * 0.35, Y2 = y, Width = 2, Colour = S.Accent });
                y += S.FontSize * 1.5;
            }
            else
            {
                y += S.FontSize * 0.8;
            }
            if (doc.Authors.Count > 0)
            {
                // Authors side by side, up to three a row.
                int perRow = S.TitlePage ? 1 : Math.Min(3, doc.Authors.Count);
                for (int start = 0; start < doc.Authors.Count; start += perRow)
                {
                    var row = doc.Authors.Skip(start).Take(perRow).ToList();
                    double top = y, bottom = y;
                    double cw = f.Width / row.Count;
                    for (int i = 0; i < row.Count; i++)
                    {
                        y = top;
                        var cell = new Frame(f.X + i * cw, cw);
                        PaperAlign align = S.TitlePage ? PaperAlign.Left : PaperAlign.Center;
                        Text(new List<Inline> { new TextInline(row[i].Name) }, cell, Body(S.TitlePage ? 1.2 : 1.1), align);
                        if (row[i].Affiliation != null) Text(new List<Inline> { new TextInline(row[i].Affiliation) }, cell, Body(0.92), align);
                        if (row[i].Email != null)
                        {
                            RunStyle mail = Body(0.88);
                            mail.Face = new Face(FontFamily.Mono, false, false);
                            mail.Url = "mailto:" + row[i].Email;
                            Text(new List<Inline> { new TextInline(row[i].Email) }, cell, mail, align);
                        }
                        bottom = Math.Max(bottom, y);
                    }
                    y = bottom + S.FontSize * 0.4;
                }
            }
            if (doc.Date != null) Text(new List<Inline> { new TextInline(doc.Date) }, f, Body(1.05), S.TitlePage ? PaperAlign.Left : PaperAlign.Center);
            y += S.FontSize * 1.2;
            if (doc.Abstract.Count > 0) Abstract(f);
            if (doc.Keywords.Count > 0)
            {
                var inlines = new List<Inline> { Bold("Keywords: "), new TextInline(string.Join(", ", doc.Keywords)) };
                Text(inlines, Inset(f), Body(0.92), PaperAlign.Left);
                y += Lead;
            }
            if (S.TitlePage) NewPage();
            indentNext = false;
        }

        private Frame Inset(Frame f) => S.TitlePage ? f : new Frame(f.X + f.Width * 0.08, f.Width * 0.84);

        private void Abstract(Frame f)
        {
            Frame inner = Inset(f);
            Text(new List<Inline> { new TextInline("Abstract") }, inner, Body(0.92).With(s => s.Face = s.Face.WithBold()), S.TitlePage ? PaperAlign.Left : PaperAlign.Center);
            y += S.FontSize * 0.3;
            indentNext = false;
            Blocks(doc.Abstract, new Frame(inner.X, inner.Width, Scale: 0.92));
            y += Lead * 0.6;
        }

        private void Contents(Contents c, Frame f)
        {
            var title = new Section { Numbered = false, Level = 1 };
            title.Title.Add(new TextInline(c.Title));
            double size = S.FontSize * HeadingScales[0];
            Gap(size);
            Text(title.Title, f, new RunStyle { Face = new Face(S.HeadingFont, true, false), Size = size, Colour = S.Accent }, PaperAlign.Left);
            y += size * 0.5;
            foreach (Section s in resolved.Sections.Where(x => x.Level <= c.Depth))
            {
                double indent = (s.Level - 1) * S.FontSize * 1.5;
                RunStyle style = Body();
                if (s.Level == 1) style.Face = style.Face.WithBold();
                style.Target = s.Id ?? SectionTarget(s);
                string pageNumber = knownPages != null && knownPages.TryGetValue(s, out int n) ? n.ToString() : "0";
                double numberWidth = StandardFonts.Measure(pageNumber, style.Face, style.Size);
                var inlines = new List<Inline>();
                if (s.Number != null) inlines.Add(new TextInline(s.Number + "  "));
                inlines.AddRange(s.Title);
                var frame = new Frame(f.X + indent, f.Width - indent - S.FontSize * 2.5);
                double before = y;
                if (s.Level == 1) Gap(S.FontSize * 0.35);
                var lines = new TextFlow(inlines, style, S.Font).Break(frame.Width);
                double lastBaseline = 0;
                Lines(lines, frame, PaperAlign.Left, style.Size, b => lastBaseline = b, keepFirst: 1);
                Line last = lines[^1];
                lastBaseline = y - Metrics(last, style.Size).Height + Metrics(last, style.Size).Baseline;
                // Dot leaders from the end of the title to the page number.
                double from = frame.X + last.Indent + last.Natural + S.FontSize * 0.5;
                double to = f.X + f.Width - numberWidth - S.FontSize * 0.5;
                RunStyle dots = Body();
                double dot = StandardFonts.Measure(". ", dots.Face, dots.Size);
                for (double dx = to - dot; dx >= from; dx -= dot)
                    page.Ops.Add(new TextOp { Font = dots.Face.BaseFont, Size = dots.Size, X = dx, Y = lastBaseline, Bytes = new[] { (byte)'.' }, Colour = "#777777" });
                double nx = f.X + f.Width - numberWidth;
                page.Ops.Add(new TextOp { Font = style.Face.BaseFont, Size = style.Size, X = nx, Y = lastBaseline, Bytes = System.Text.Encoding.ASCII.GetBytes(pageNumber), Colour = "#000000" });
                page.Links.Add(new LinkArea { X = f.X + indent, Y = lastBaseline - style.Size, W = f.Width - indent, H = style.Size * 1.3, Target = style.Target });
            }
            y += Lead;
            indentNext = false;
        }

        /// <summary>Sections without an id still get a jump target, for the contents page.</summary>
        private string SectionTarget(Section s) => "section-" + resolved.Sections.IndexOf(s);

        private void Bibliography(Bibliography bib, Frame f)
        {
            if (resolved.Cited.Count == 0) return;
            var heading = new Section { Numbered = false, Level = 1, Id = bib.Id };
            heading.Title.Add(new TextInline(bib.Title));
            Heading(heading, f);
            bool numeric = S.Citations == CitationStyle.Numeric;
            RunStyle style = Body(0.95 * f.Scale);
            double label = numeric ? StandardFonts.Measure($"[{resolved.Cited.Count}]", style.Face, style.Size) + S.FontSize * 0.8 : S.FontSize * 1.5;
            foreach (Source s in resolved.Cited)
            {
                Mark("ref-" + s.Key);
                var flow = new TextFlow(BibTex.Format(s, S.Citations), style, S.Font);
                var inner = new Frame(f.X + label, f.Width - label);
                if (numeric)
                {
                    Lines(flow.Break(inner.Width), inner, PaperAlign.Left, style.Size, baseline =>
                    {
                        string text = $"[{s.Number}]";
                        page.Ops.Add(new TextOp { Font = style.Face.BaseFont, Size = style.Size, X = f.X, Y = baseline, Bytes = System.Text.Encoding.ASCII.GetBytes(text), Colour = "#000000" });
                    });
                }
                else
                {
                    // A hanging indent: the first line starts at the margin.
                    var lines = flow.Break(inner.Width, -label);
                    Lines(lines, inner, PaperAlign.Left, style.Size);
                }
                y += S.FontSize * 0.3;
            }
            indentNext = false;
        }

        // ---- finishing -----------------------------------------------------------------------------------------

        private byte[] Finish()
        {
            for (int i = 0; i < pages.Count; i++)
            {
                PdfPage p = pages[i];
                Footnotes(p);
                if (i == 0 && S.PlainFirstPage) continue;
                HeaderFooter(p, S.Header, S.MarginTop * 0.55, i + 1);
                HeaderFooter(p, S.Footer, S.PageHeight - S.MarginBottom * 0.45, i + 1);
            }
            foreach (Section s in resolved.Sections.Where(s => s.Id == null && sectionPages.ContainsKey(s)))
            {
                Bookmark b = bookmarks.FirstOrDefault(x => x.Page == sectionPages[s] - 1 && x.Title.EndsWith(InlineParser.Plain(s.Title), StringComparison.Ordinal));
                if (b != null) destinations[SectionTarget(s)] = (b.Page, b.Y);
            }
            var info = new Dictionary<string, string>
            {
                ["Title"] = doc.Title,
                ["Author"] = string.Join(", ", doc.Authors.Select(a => a.Name)),
                ["Subject"] = doc.Subtitle,
                ["Keywords"] = string.Join(", ", doc.Keywords),
            };
            return PdfFile.Write(pages, S.PageWidth, S.PageHeight, destinations, bookmarks, info);
        }

        private void Footnotes(PdfPage p)
        {
            List<FootnoteInline> notes = pageNotes[p];
            if (notes.Count == 0) return;
            double size = S.FontSize * 0.82;
            double top = Bottom - noteHeights[p];
            double ny = top + S.FontSize * 0.6;
            p.Ops.Add(new LineOp { X1 = S.MarginLeft, Y1 = ny, X2 = S.MarginLeft + S.TextWidth * 0.3, Y2 = ny, Width = 0.5, Colour = "#000000" });
            ny += S.FontSize * 0.6;
            double label = size * 1.6;
            foreach (FootnoteInline n in notes)
            {
                destinations["fn" + n.Number] = (pages.IndexOf(p), ny - size);
                var lines = new TextFlow(n.Children, Body(0.82), S.Font).Break(S.TextWidth - label);
                for (int i = 0; i < lines.Count; i++)
                {
                    double baseline = ny + size;
                    if (i == 0)
                    {
                        string mark = n.Number.ToString();
                        p.Ops.Add(new TextOp { Font = new Face(S.Font, false, false).BaseFont, Size = size * 0.75, X = S.MarginLeft + size * 0.2, Y = baseline - size * 0.32, Bytes = System.Text.Encoding.ASCII.GetBytes(mark), Colour = "#000000" });
                    }
                    TextFlow.Draw(lines[i], S.MarginLeft + label, baseline, S.TextWidth - label, S.Align, p);
                    ny += size * 1.25;
                }
            }
        }

        private void HeaderFooter(PdfPage p, string spec, double baseline, int number)
        {
            if (string.IsNullOrWhiteSpace(spec)) return;
            string[] parts = spec.Split('|');
            if (parts.Length == 1) parts = new[] { string.Empty, parts[0], string.Empty };
            RunStyle style = Body(0.85);
            style.Colour = "#444444";
            for (int i = 0; i < Math.Min(3, parts.Length); i++)
            {
                string text = parts[i].Trim()
                    .Replace("[pages]", pages.Count.ToString()).Replace("[page]", number.ToString())
                    .Replace("[title]", doc.Title ?? string.Empty).Replace("[author]", string.Join(", ", doc.Authors.Select(a => a.Name)))
                    .Replace("[date]", doc.Date ?? string.Empty);
                if (text.Length == 0) continue;
                double width = StandardFonts.Measure(text, style.Face, style.Size);
                double x = i switch { 0 => S.MarginLeft, 1 => S.MarginLeft + (S.TextWidth - width) / 2, _ => S.MarginLeft + S.TextWidth - width };
                foreach (var (font, bytes, w) in StandardFonts.Runs(text, style.Face, style.Size))
                {
                    p.Ops.Add(new TextOp { Font = font, Size = style.Size, X = x, Y = baseline, Bytes = bytes, Colour = style.Colour });
                    x += w;
                }
            }
        }
    }
}
