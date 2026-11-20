using KScript.Documents.Maths;

namespace KScript.Documents.Pdf
{
    /// <summary>Laid-out maths: drawing operations around a baseline at y = 0 (up is negative), with its size.</summary>
    internal sealed class MathBox
    {
        public double Width;
        public double Ascent;
        public double Descent;
        public List<Op> Ops = new List<Op>();

        public void Add(MathBox other, double x, double y)
        {
            foreach (Op op in other.Ops) Ops.Add(op.Moved(x, y));
        }
    }

    /// <summary>
    /// Lays out maths in the TeX manner, simplified: italic letters, upright digits and operators, spacing by atom
    /// kind, smaller scripts, fractions, roots, large operators with limits, growing delimiters and matrices.
    /// </summary>
    internal sealed class MathLayout
    {
        private readonly string colour;
        private readonly FontFamily family;

        private MathLayout(string colour, FontFamily family)
        {
            this.colour = colour;
            this.family = family == FontFamily.Mono ? FontFamily.Serif : family;
        }

        /// <param name="display">Displayed maths (an equation on its own line) rather than maths in a sentence.</param>
        public static MathBox Layout(MathNode node, double size, bool display, string colour = "#000000", FontFamily family = FontFamily.Serif) =>
            new MathLayout(colour, family).Node(node, size, display ? 0 : 1);

        // Styles: 0 display, 1 text, 2 script, 3 script-script.
        private static double ScriptSize(double size, int style) => style >= 2 ? size * 0.72 : size * 0.7;

        private static int Smaller(int style) => style < 2 ? 2 : 3;

        private static double Axis(double size) => size * 0.25;

        private Face FaceFor(MathFont font, bool letter) => font switch
        {
            MathFont.Bold => new Face(family, true, false),
            MathFont.Italic => new Face(family, false, true),
            MathFont.Sans => new Face(FontFamily.Sans, false, false),
            MathFont.Mono => new Face(FontFamily.Mono, false, false),
            MathFont.Roman => new Face(family, false, false),
            _ => new Face(family, false, letter),
        };

        private MathBox Glyphs(string text, Face face, double size)
        {
            var box = new MathBox { Ascent = size * 0.72, Descent = size * 0.22 };
            double x = 0;
            foreach (var (font, bytes, width) in StandardFonts.Runs(text, face, size))
            {
                box.Ops.Add(new TextOp { Font = font, Size = size, X = x, Y = 0, Bytes = bytes, Colour = colour });
                x += width;
            }
            box.Width = x;
            return box;
        }

        private MathBox Node(MathNode node, double size, int style)
        {
            switch (node)
            {
                case MathRow row:
                    return Row(row.Items, size, style);
                case MathAtom a:
                    return Glyphs(a.Text, FaceFor(a.Font, a.Kind == AtomKind.Ord && a.Text.All(c => c < 128 && char.IsLetter(c))), size);
                case MathText t:
                    return Glyphs(t.Text, t.Font switch { MathFont.Bold => new Face(family, true, false), MathFont.Italic => new Face(family, false, true), MathFont.Mono => new Face(FontFamily.Mono, false, false), MathFont.Sans => new Face(FontFamily.Sans, false, false), _ => new Face(family, false, false) }, size);
                case MathStyled s:
                    return Node(s.Body, size, style);
                case MathFrac f:
                    return Frac(f, size, style);
                case MathSqrt sq:
                    return Sqrt(sq, size, style);
                case MathScripts sc:
                    return Scripts(sc, size, style);
                case MathBigOp op:
                    return BigOp(op, size, style);
                case MathFenced fe:
                    return Fenced(Node(fe.Body, size, style), fe.Left, fe.Right, size);
                case MathMatrix m:
                    return Matrix(m, size, style);
                case MathAccent ac:
                    return Accent(ac, size, style);
                case MathSpace sp:
                    return new MathBox { Width = sp.Em * size };
            }
            return new MathBox();
        }

        private static AtomKind KindOf(MathNode n) => n switch
        {
            MathAtom a => a.Kind,
            MathBigOp => AtomKind.Op,
            MathScripts s => KindOf(s.Base),
            MathStyled st => KindOf(st.Body),
            MathFenced => AtomKind.Ord,
            _ => AtomKind.Ord,
        };

        private MathBox Row(List<MathNode> items, double size, int style)
        {
            var box = new MathBox();
            double x = 0;
            AtomKind? previous = null;
            for (int i = 0; i < items.Count; i++)
            {
                MathNode item = items[i];
                AtomKind kind = KindOf(item);
                // A + or - with nothing before it to add to is a sign: -x, (+1).
                if (kind == AtomKind.Bin && (previous == null || previous == AtomKind.Bin || previous == AtomKind.Rel || previous == AtomKind.Open || previous == AtomKind.Punct || previous == AtomKind.Op))
                    kind = AtomKind.Ord;
                if (kind == AtomKind.Bin && (i == items.Count - 1 || KindOf(items[i + 1]) == AtomKind.Rel || KindOf(items[i + 1]) == AtomKind.Close || KindOf(items[i + 1]) == AtomKind.Punct))
                    kind = AtomKind.Ord;
                if (previous != null) x += Space(previous.Value, kind, style) * size;
                MathBox b = Node(item, size, style);
                box.Add(b, x, 0);
                x += b.Width;
                box.Ascent = Math.Max(box.Ascent, b.Ascent);
                box.Descent = Math.Max(box.Descent, b.Descent);
                previous = kind;
            }
            box.Width = x;
            if (items.Count == 0)
            {
                box.Ascent = size * 0.72;
                box.Descent = size * 0.22;
            }
            return box;
        }

        /// <summary>Space between two atoms, in ems: TeX's thin (3/18), medium (4/18) and thick (5/18) spaces.</summary>
        private static double Space(AtomKind left, AtomKind right, int style)
        {
            bool tight = style >= 2;
            const double thin = 3 / 18.0, medium = 4 / 18.0, thick = 5 / 18.0;
            if (left == AtomKind.Bin || right == AtomKind.Bin) return tight ? 0 : medium;
            if (left == AtomKind.Rel || right == AtomKind.Rel) return left == AtomKind.Rel && right == AtomKind.Rel ? 0 : tight ? 0 : thick;
            if (left == AtomKind.Punct) return tight ? 0 : thin;
            if (left == AtomKind.Op && (right == AtomKind.Ord || right == AtomKind.Op)) return thin;
            if (right == AtomKind.Op && left == AtomKind.Ord) return thin;
            return 0;
        }

        private MathBox Frac(MathFrac f, double size, int style)
        {
            int inner = style == 0 ? 1 : Smaller(style);
            double innerSize = style == 0 ? size : ScriptSize(size, style);
            if (f.Command == "dfrac" || f.Command == "dbinom" || f.Command == "cfrac")
            {
                inner = 1;
                innerSize = size;
            }
            MathBox num = Node(f.Numerator, innerSize, inner);
            MathBox den = Node(f.Denominator, innerSize, inner);
            double rule = f.Bar ? Math.Max(0.4, size * 0.045) : 0;
            double gap = size * (style == 0 ? 0.16 : 0.1);
            double axis = Axis(size);
            double pad = size * 0.1;
            double width = Math.Max(num.Width, den.Width) + 2 * pad;
            var box = new MathBox { Width = width };
            double numBase = -(axis + rule / 2 + gap + num.Descent);
            double denBase = -axis + rule / 2 + gap + den.Ascent;
            box.Add(num, (width - num.Width) / 2, numBase);
            box.Add(den, (width - den.Width) / 2, denBase);
            if (f.Bar) box.Ops.Add(new LineOp { X1 = pad / 2, Y1 = -axis, X2 = width - pad / 2, Y2 = -axis, Width = rule, Colour = colour });
            box.Ascent = -numBase + num.Ascent;
            box.Descent = denBase + den.Descent;
            if (!f.Bar) return Fenced(box, "(", ")", size);
            return box;
        }

        private MathBox Sqrt(MathSqrt s, double size, int style)
        {
            MathBox body = Node(s.Body, size, style);
            double rule = Math.Max(0.4, size * 0.045);
            double gap = size * 0.12;
            double top = body.Ascent + gap + rule / 2;
            double bottom = body.Descent;
            double height = top + bottom;
            double signWidth = size * 0.45 + height * 0.12;
            double indexShift = 0;
            MathBox index = null;
            if (s.Index != null)
            {
                index = Node(s.Index, size * 0.5, 3);
                indexShift = Math.Max(0, index.Width - signWidth * 0.55);
            }
            var box = new MathBox { Width = indexShift + signWidth + body.Width + size * 0.1 };
            double x0 = indexShift;
            var path = new PathOp { Width = rule, Colour = colour };
            double tickY = bottom - height * 0.45;
            path.Parts.Add(('M', new[] { x0, tickY + rule }));
            path.Parts.Add(('L', new[] { x0 + signWidth * 0.22, tickY - size * 0.04 }));
            path.Parts.Add(('L', new[] { x0 + signWidth * 0.5, bottom }));
            path.Parts.Add(('L', new[] { x0 + signWidth, -top }));
            path.Parts.Add(('L', new[] { box.Width, -top }));
            box.Ops.Add(path);
            // The down stroke is drawn thicker, as in a printed radical sign.
            box.Ops.Add(new LineOp { X1 = x0 + signWidth * 0.22, Y1 = tickY - size * 0.04, X2 = x0 + signWidth * 0.5, Y2 = bottom, Width = rule * 2.2, Colour = colour });
            box.Add(body, x0 + signWidth + size * 0.05, 0);
            box.Ascent = top + rule;
            box.Descent = bottom + rule;
            if (index != null)
            {
                double indexBase = tickY - size * 0.12 - index.Descent;
                box.Add(index, Math.Max(0, x0 + signWidth * 0.55 - index.Width), indexBase);
                box.Ascent = Math.Max(box.Ascent, -indexBase + index.Ascent);
            }
            return box;
        }

        private MathBox Scripts(MathScripts sc, double size, int style)
        {
            if (sc.Base is MathBigOp op && op.Limits && style == 0) return Limits(op, sc, size, style);
            MathBox b = Node(sc.Base, size, style);
            double scriptSize = ScriptSize(size, style);
            int scriptStyle = Smaller(style);
            var box = new MathBox { Ascent = b.Ascent, Descent = b.Descent };
            box.Add(b, 0, 0);
            double x = b.Width;
            bool italic = sc.Base is MathAtom a && a.Kind == AtomKind.Ord && a.Font == MathFont.Normal && a.Text.All(char.IsLetter);
            MathBox sup = sc.Sup != null ? Node(sc.Sup, scriptSize, scriptStyle) : null;
            MathBox sub = sc.Sub != null ? Node(sc.Sub, scriptSize, scriptStyle) : null;
            bool big = sc.Base is MathBigOp bo && bo.Large;
            double u = sup == null ? 0 : Math.Max(size * 0.38, b.Ascent - scriptSize * (big ? 0.6 : 0.4));
            double v = sub == null ? 0 : Math.Max(size * 0.18, b.Descent - scriptSize * (big ? 0.2 : 0.3));
            if (sup != null && sub != null)
            {
                double clearance = (u - sup.Descent) - (sub.Ascent - v);
                if (clearance < size * 0.12)
                {
                    double push = (size * 0.12 - clearance) / 2;
                    u += push;
                    v += push;
                }
            }
            double width = 0;
            if (sup != null)
            {
                box.Add(sup, x + (italic ? size * 0.04 : 0), -u);
                box.Ascent = Math.Max(box.Ascent, u + sup.Ascent);
                width = sup.Width + (italic ? size * 0.04 : 0);
            }
            if (sub != null)
            {
                box.Add(sub, x - (big ? size * 0.15 : 0), v);
                box.Descent = Math.Max(box.Descent, v + sub.Descent);
                width = Math.Max(width, sub.Width - (big ? size * 0.15 : 0));
            }
            box.Width = x + width + size * 0.03;
            return box;
        }

        private MathBox Limits(MathBigOp op, MathScripts sc, double size, int style)
        {
            MathBox b = BigOp(op, size, style);
            double scriptSize = ScriptSize(size, style);
            MathBox sup = sc.Sup != null ? Node(sc.Sup, scriptSize, 2) : null;
            MathBox sub = sc.Sub != null ? Node(sc.Sub, scriptSize, 2) : null;
            double width = Math.Max(b.Width, Math.Max(sup?.Width ?? 0, sub?.Width ?? 0));
            var box = new MathBox { Width = width, Ascent = b.Ascent, Descent = b.Descent };
            box.Add(b, (width - b.Width) / 2, 0);
            double gap = size * 0.12;
            if (sup != null)
            {
                double baseY = -(b.Ascent + gap + sup.Descent);
                box.Add(sup, (width - sup.Width) / 2, baseY);
                box.Ascent = -baseY + sup.Ascent;
            }
            if (sub != null)
            {
                double baseY = b.Descent + gap + sub.Ascent;
                box.Add(sub, (width - sub.Width) / 2, baseY);
                box.Descent = baseY + sub.Descent;
            }
            return box;
        }

        private MathBox BigOp(MathBigOp op, double size, int style)
        {
            if (op.Named) return Glyphs(op.Text, new Face(family, false, false), size);
            bool integral = op.Text[0] >= '∫' && op.Text[0] <= '∮';
            double glyphSize = style == 0 ? size * (integral ? 1.9 : 1.55) : size * (integral ? 1.3 : 1.15);
            MathBox g = Glyphs(op.Text, new Face(family, false, false), glyphSize);
            // Symbol's ∑ reaches from -0.01 to 0.71 em and ∫ from -0.18 to 0.77 em: centre it on the maths axis.
            double low = integral ? -0.18 : -0.01, high = integral ? 0.77 : 0.71;
            double shift = -Axis(size) + (high + low) / 2 * glyphSize;
            var box = new MathBox { Width = g.Width };
            box.Add(g, 0, shift);
            box.Ascent = high * glyphSize - shift;
            box.Descent = -low * glyphSize + shift;
            if (op.Text[0] == '∮')
            {
                // ∮: a small circle on the integral.
                double r = glyphSize * 0.09;
                box.Ops.Add(Circle(g.Width / 2, -Axis(size), r, fill: false, width: Math.Max(0.4, size * 0.04)));
            }
            return box;
        }

        private PathOp Circle(double cx, double cy, double r, bool fill, double width = 0.5)
        {
            const double k = 0.5523;
            var p = new PathOp { Fill = fill, Width = width, Colour = colour };
            p.Parts.Add(('M', new[] { cx + r, cy }));
            p.Parts.Add(('C', new[] { cx + r, cy - k * r, cx + k * r, cy - r, cx, cy - r }));
            p.Parts.Add(('C', new[] { cx - k * r, cy - r, cx - r, cy - k * r, cx - r, cy }));
            p.Parts.Add(('C', new[] { cx - r, cy + k * r, cx - k * r, cy + r, cx, cy + r }));
            p.Parts.Add(('C', new[] { cx + k * r, cy + r, cx + r, cy + k * r, cx + r, cy }));
            return p;
        }

        /// <summary>Delimiters around a box, stretched to cover it evenly about the maths axis.</summary>
        private MathBox Fenced(MathBox body, string left, string right, double size)
        {
            double axis = Axis(size);
            double half = Math.Max(body.Ascent - axis, body.Descent + axis) * 1.08;
            double height = Math.Max(size * 0.95, half * 2);
            MathBox l = Delimiter(left, height, size);
            MathBox r = Delimiter(right, height, size);
            double pad = size * 0.05;
            var box = new MathBox { Width = l.Width + pad + body.Width + pad + r.Width };
            box.Add(l, 0, 0);
            box.Add(body, l.Width + pad, 0);
            box.Add(r, l.Width + pad + body.Width + pad, 0);
            box.Ascent = Math.Max(body.Ascent, Math.Max(l.Ascent, r.Ascent));
            box.Descent = Math.Max(body.Descent, Math.Max(l.Descent, r.Descent));
            return box;
        }

        /// <summary>One delimiter of a given height, centred on the axis: a glyph stretched upwards, or lines for floors and ceilings.</summary>
        private MathBox Delimiter(string d, double height, double size)
        {
            double axis = Axis(size);
            var box = new MathBox { Ascent = height / 2 + axis, Descent = height / 2 - axis };
            if (string.IsNullOrEmpty(d)) return box;
            double w = Math.Max(0.4, size * 0.05);
            if (d == "⌊" || d == "⌋" || d == "⌈" || d == "⌉")
            {
                double width = size * 0.32;
                bool leftSide = d == "⌊" || d == "⌈";
                bool floor = d == "⌊" || d == "⌋";
                double x = leftSide ? size * 0.08 : width - size * 0.08;
                double top = -box.Ascent, bottom = box.Descent;
                box.Ops.Add(new LineOp { X1 = x, Y1 = top, X2 = x, Y2 = bottom, Width = w, Colour = colour });
                double arm = leftSide ? x + size * 0.18 : x - size * 0.18;
                double y = floor ? bottom : top;
                box.Ops.Add(new LineOp { X1 = x, Y1 = y, X2 = arm, Y2 = y, Width = w, Colour = colour });
                box.Width = width;
                return box;
            }
            if (d == "‖")
            {
                box.Ops.Add(new LineOp { X1 = size * 0.1, Y1 = -box.Ascent, X2 = size * 0.1, Y2 = box.Descent, Width = w, Colour = colour });
                box.Ops.Add(new LineOp { X1 = size * 0.22, Y1 = -box.Ascent, X2 = size * 0.22, Y2 = box.Descent, Width = w, Colour = colour });
                box.Width = size * 0.32;
                return box;
            }
            string glyph = d switch { "⟨" => "〈", "⟩" => "〉", _ => d };
            // A glyph's ink reaches from about -0.18 to 0.68 em (Times parentheses); stretch that to the height.
            const double low = -0.18, high = 0.68;
            double stretch = Math.Max(1, height / ((high - low) * size));
            MathBox g = Glyphs(glyph, new Face(family, false, false), size);
            double baseline = box.Descent + low * size * stretch;
            foreach (Op op in g.Ops)
            {
                var t = (TextOp)op.Moved(0, baseline);
                t.StretchY = stretch;
                box.Ops.Add(t);
            }
            box.Width = g.Width;
            return box;
        }

        private MathBox Matrix(MathMatrix m, double size, int style)
        {
            int columns = m.ColumnCount;
            int cellStyle = style == 0 && (m.Environment == "aligned" || m.Environment == "align" || m.Environment == "align*" || m.Environment == "split" || m.Environment == "gathered") ? 0 : 1;
            var cells = m.Rows.Select(r => r.Select(c => Node(c, size, cellStyle)).ToList()).ToList();
            var widths = new double[columns];
            foreach (var row in cells)
                for (int c = 0; c < row.Count; c++) widths[c] = Math.Max(widths[c], row[c].Width);
            bool aligned = m.AlignOf(0) == 'r' && m.AlignOf(1) == 'l' && m.Columns == null;
            double Gap(int c) => aligned ? (c % 2 == 0 ? 0 : size * 1.5) : m.Environment == "cases" ? size * 1.0 : size * 0.8;
            double rowGap = size * (m.Environment == "cases" || aligned ? 0.35 : 0.3);
            var ascents = cells.Select(r => r.Count == 0 ? size * 0.72 : Math.Max(size * 0.72, r.Max(b => b.Ascent))).ToList();
            var descents = cells.Select(r => r.Count == 0 ? size * 0.22 : Math.Max(size * 0.22, r.Max(b => b.Descent))).ToList();
            double total = ascents.Sum() + descents.Sum() + rowGap * Math.Max(0, cells.Count - 1);
            var table = new MathBox { Width = widths.Sum() + Enumerable.Range(0, Math.Max(0, columns - 1)).Sum(Gap) };
            double axis = Axis(size);
            double y = -(total / 2 + axis);
            for (int r = 0; r < cells.Count; r++)
            {
                y += ascents[r];
                double x = 0;
                for (int c = 0; c < columns; c++)
                {
                    if (c < cells[r].Count)
                    {
                        MathBox cell = cells[r][c];
                        double offset = m.AlignOf(c) switch { 'l' => 0, 'r' => widths[c] - cell.Width, _ => (widths[c] - cell.Width) / 2 };
                        table.Add(cell, x + offset, y);
                    }
                    x += widths[c] + (c < columns - 1 ? Gap(c) : 0);
                }
                y += descents[r] + rowGap;
            }
            table.Ascent = total / 2 + axis;
            table.Descent = total / 2 - axis;
            if (m.Left.Length == 0 && m.Right.Length == 0) return table;
            return Fenced(table, m.Left, m.Right, size);
        }

        private MathBox Accent(MathAccent ac, double size, int style)
        {
            MathBox body = Node(ac.Body, size, style);
            var box = new MathBox { Width = body.Width, Ascent = body.Ascent, Descent = body.Descent };
            box.Add(body, 0, 0);
            double w = Math.Max(0.4, size * 0.045);
            double top = -(Math.Max(body.Ascent, size * 0.5) + size * 0.1);
            double cx = body.Width / 2 + (ac.Body is MathAtom a && a.Font == MathFont.Normal && a.Text.All(char.IsLetter) ? size * 0.05 : 0);
            switch (ac.Accent)
            {
                case "underline":
                    box.Ops.Add(new LineOp { X1 = 0, Y1 = body.Descent + size * 0.1, X2 = body.Width, Y2 = body.Descent + size * 0.1, Width = w, Colour = colour });
                    box.Descent = body.Descent + size * 0.15;
                    return box;
                case "bar":
                case "overline":
                {
                    double x1 = ac.Accent == "bar" ? cx - Math.Min(body.Width, size * 0.5) / 2 : 0;
                    double x2 = ac.Accent == "bar" ? cx + Math.Min(body.Width, size * 0.5) / 2 : body.Width;
                    box.Ops.Add(new LineOp { X1 = x1, Y1 = top, X2 = x2, Y2 = top, Width = w, Colour = colour });
                    break;
                }
                case "vec":
                {
                    double half = Math.Max(body.Width, size * 0.5) / 2;
                    box.Ops.Add(new LineOp { X1 = cx - half, Y1 = top, X2 = cx + half, Y2 = top, Width = w, Colour = colour });
                    var head = new PathOp { Width = w, Colour = colour };
                    head.Parts.Add(('M', new[] { cx + half - size * 0.12, top - size * 0.08 }));
                    head.Parts.Add(('L', new[] { cx + half, top }));
                    head.Parts.Add(('L', new[] { cx + half - size * 0.12, top + size * 0.08 }));
                    box.Ops.Add(head);
                    break;
                }
                case "dot":
                    box.Ops.Add(Circle(cx, top, size * 0.045, fill: true));
                    break;
                case "ddot":
                    box.Ops.Add(Circle(cx - size * 0.1, top, size * 0.045, fill: true));
                    box.Ops.Add(Circle(cx + size * 0.1, top, size * 0.045, fill: true));
                    break;
                case "tilde":
                case "widetilde":
                {
                    double half = (ac.Accent == "widetilde" ? Math.Max(body.Width, size * 0.4) : size * 0.4) / 2;
                    var p = new PathOp { Width = w, Colour = colour };
                    p.Parts.Add(('M', new[] { cx - half, top + size * 0.03 }));
                    p.Parts.Add(('C', new[] { cx - half / 2, top - size * 0.12, cx + half / 2, top + size * 0.12, cx + half, top - size * 0.03 }));
                    box.Ops.Add(p);
                    break;
                }
                default:
                {
                    // hat and widehat
                    double half = (ac.Accent == "widehat" ? Math.Max(body.Width, size * 0.4) : size * 0.36) / 2;
                    var p = new PathOp { Width = w, Colour = colour };
                    p.Parts.Add(('M', new[] { cx - half, top + size * 0.06 }));
                    p.Parts.Add(('L', new[] { cx, top - size * 0.08 }));
                    p.Parts.Add(('L', new[] { cx + half, top + size * 0.06 }));
                    box.Ops.Add(p);
                    break;
                }
            }
            box.Ascent = Math.Max(body.Ascent, -top + size * 0.12);
            return box;
        }
    }
}
