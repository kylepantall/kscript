using KScript.Documents.Text;

namespace KScript.Documents.Pdf
{
    /// <summary>How a run of text looks: font, size, colour and what it links to.</summary>
    internal sealed class RunStyle
    {
        public Face Face;
        public double Size;
        public string Colour = "#000000";
        public string Url;
        public string Target;

        /// <summary>How far above the baseline it sits (footnote marks), in points.</summary>
        public double Rise;

        public RunStyle With(Action<RunStyle> change)
        {
            var copy = (RunStyle)MemberwiseClone();
            change(copy);
            return copy;
        }
    }

    /// <summary>A piece of a word in one style: text, or a maths box.</summary>
    internal sealed class Fragment
    {
        public string Text = string.Empty;
        public RunStyle Style;
        public MathBox Math;
        public double Width;
        public FootnoteInline Note;

        public double Ascent => Math != null ? Math.Ascent : Style.Size * 0.72 + Style.Rise;
        public double Descent => Math != null ? Math.Descent : Style.Size * 0.22 - Style.Rise;
    }

    /// <summary>Text that can't be broken across lines, and the space after it.</summary>
    internal sealed class Word
    {
        public List<Fragment> Fragments = new List<Fragment>();
        public double Width;
        public double SpaceAfter;
        public bool BreakAfter;
    }

    internal sealed class Line
    {
        public List<Word> Words = new List<Word>();
        public double Indent;
        public double Natural;
        public bool Last;
        public double Ascent;
        public double Descent;

        public IEnumerable<FootnoteInline> Notes => Words.SelectMany(w => w.Fragments).Where(f => f.Note != null).Select(f => f.Note);
    }

    /// <summary>Turns inlines into words, breaks them into lines and draws the lines.</summary>
    internal sealed class TextFlow
    {
        private readonly List<Word> words = new List<Word>();
        private Word current;
        private readonly FontFamily mathFamily;

        public TextFlow(IEnumerable<Inline> inlines, RunStyle style, FontFamily mathFamily)
        {
            this.mathFamily = mathFamily;
            Add(inlines, style);
            End(0);
            foreach (Word w in words)
            {
                foreach (Fragment f in w.Fragments)
                {
                    if (f.Math == null) f.Width = StandardFonts.Measure(f.Text, f.Style.Face, f.Style.Size);
                }
                w.Width = w.Fragments.Sum(f => f.Width);
            }
        }

        public IReadOnlyList<Word> Words => words;

        private void End(double space)
        {
            if (current != null && current.Fragments.Count > 0)
            {
                current.SpaceAfter = space;
                words.Add(current);
            }
            else if (words.Count > 0 && space > 0 && words[^1].SpaceAfter == 0 && current?.BreakAfter != true)
            {
                words[^1].SpaceAfter = space;
            }
            current = null;
        }

        private Word Current => current ??= new Word();

        private void Text(string text, RunStyle style, FootnoteInline note = null)
        {
            foreach (char c in text)
            {
                if (c == ' ' || c == '\t' || c == '\n')
                {
                    End(StandardFonts.Measure(" ", style.Face, style.Size));
                    continue;
                }
                Word w = Current;
                Fragment last = w.Fragments.Count > 0 ? w.Fragments[^1] : null;
                if (last == null || last.Math != null || last.Style != style || last.Note != note)
                {
                    last = new Fragment { Style = style, Note = note };
                    w.Fragments.Add(last);
                }
                last.Text += c;
            }
        }

        private void Add(IEnumerable<Inline> inlines, RunStyle style)
        {
            foreach (Inline i in inlines ?? Enumerable.Empty<Inline>())
            {
                switch (i)
                {
                    case TextInline t:
                        Text(t.Text, style);
                        break;
                    case StrongInline s:
                        Add(s.Children, style.With(x => x.Face = x.Face.WithBold()));
                        break;
                    case EmphasisInline e:
                        Add(e.Children, style.With(x => x.Face = x.Face.WithItalic(!x.Face.Italic)));
                        break;
                    case CodeInline c:
                        Text(c.Text, style.With(x =>
                        {
                            x.Face = new Face(FontFamily.Mono, x.Face.Bold, false);
                            x.Size *= 0.95;
                        }));
                        break;
                    case LinkInline l:
                        Add(l.Children, style.With(x => x.Url = l.Url));
                        break;
                    case MathInline m:
                    {
                        MathBox box = MathLayout.Layout(m.Node, style.Size, display: false, style.Colour, mathFamily);
                        Current.Fragments.Add(new Fragment { Math = box, Width = box.Width, Style = style });
                        break;
                    }
                    case CiteInline c:
                        Text(c.Text, style.With(x => x.Target = "ref-" + c.Keys[0]));
                        break;
                    case RefInline r:
                        Text(r.Text, style.With(x => x.Target = r.Id));
                        break;
                    case FootnoteInline f:
                        Text(f.Number.ToString(), style.With(x =>
                        {
                            x.Size = style.Size * 0.68;
                            x.Rise = style.Size * 0.36;
                            x.Target = "fn" + f.Number;
                            x.Url = null;
                        }), f);
                        break;
                    case LineBreakInline:
                        Current.BreakAfter = true;
                        if (current.Fragments.Count == 0) current.Fragments.Add(new Fragment { Style = style });
                        End(0);
                        break;
                }
            }
        }

        /// <summary>The width the text takes on one line.</summary>
        public double NaturalWidth => words.Sum(w => w.Width) + words.Take(Math.Max(0, words.Count - 1)).Sum(w => w.SpaceAfter);

        /// <summary>The widest word: no column can be narrower without breaking it.</summary>
        public double MinWidth => words.Count == 0 ? 0 : words.Max(w => w.Width);

        /// <summary>Breaks the words into lines of at most <paramref name="width"/>; the first is indented.</summary>
        public List<Line> Break(double width, double firstIndent = 0)
        {
            var lines = new List<Line>();
            var line = new Line { Indent = firstIndent };
            double x = firstIndent;
            double pending = 0;
            void Finish(bool last)
            {
                line.Natural = x;
                line.Last = last;
                Measure(line);
                lines.Add(line);
                line = new Line();
                x = 0;
                pending = 0;
            }
            foreach (Word word in words.SelectMany(w => w.Width > width ? Split(w, width) : new[] { w }))
            {
                if (line.Words.Count > 0 && x + pending + word.Width > width + 0.01) Finish(false);
                x += (line.Words.Count > 0 ? pending : 0) + word.Width;
                line.Words.Add(word);
                pending = word.SpaceAfter;
                if (word.BreakAfter) Finish(true);
            }
            if (line.Words.Count > 0 || lines.Count == 0) Finish(true);
            else lines[^1].Last = true;
            return lines;
        }

        /// <summary>A word too wide for the line, cut into pieces that fit (a long URL, say).</summary>
        private static IEnumerable<Word> Split(Word word, double width)
        {
            var piece = new Word();
            foreach (Fragment f in word.Fragments)
            {
                if (f.Math != null)
                {
                    piece.Fragments.Add(f);
                    piece.Width += f.Width;
                    continue;
                }
                var part = new Fragment { Style = f.Style, Note = f.Note };
                foreach (char c in f.Text)
                {
                    double w = StandardFonts.Measure(c.ToString(), f.Style.Face, f.Style.Size);
                    if (piece.Width + w > width && piece.Width > 0)
                    {
                        if (part.Text.Length > 0) piece.Fragments.Add(part);
                        yield return piece;
                        piece = new Word();
                        part = new Fragment { Style = f.Style, Note = f.Note };
                    }
                    part.Text += c;
                    part.Width += w;
                    piece.Width += w;
                }
                if (part.Text.Length > 0) piece.Fragments.Add(part);
            }
            piece.SpaceAfter = word.SpaceAfter;
            piece.BreakAfter = word.BreakAfter;
            yield return piece;
        }

        private static void Measure(Line line)
        {
            foreach (Fragment f in line.Words.SelectMany(w => w.Fragments))
            {
                line.Ascent = Math.Max(line.Ascent, f.Ascent);
                line.Descent = Math.Max(line.Descent, f.Descent);
            }
        }

        /// <summary>
        /// Draws a line with its baseline at <paramref name="baseline"/>, from <paramref name="x"/> across
        /// <paramref name="width"/>. Links become clickable areas.
        /// </summary>
        public static void Draw(Line line, double x, double baseline, double width, PaperAlign align, PdfPage page)
        {
            double extra = width - line.Natural;
            double gapExtra = 0;
            double start = x + line.Indent;
            switch (align)
            {
                case PaperAlign.Right:
                    start += extra;
                    break;
                case PaperAlign.Center:
                    start += extra / 2;
                    break;
                case PaperAlign.Justify when !line.Last && line.Words.Count > 1 && extra > 0:
                    gapExtra = extra / (line.Words.Count - 1);
                    break;
            }
            double pos = start;
            for (int i = 0; i < line.Words.Count; i++)
            {
                Word w = line.Words[i];
                foreach (Fragment f in w.Fragments)
                {
                    if (f.Math != null)
                    {
                        foreach (Op op in f.Math.Ops) page.Ops.Add(op.Moved(pos, baseline));
                    }
                    else if (f.Text.Length > 0)
                    {
                        double fx = pos;
                        foreach (var (font, bytes, runWidth) in StandardFonts.Runs(f.Text, f.Style.Face, f.Style.Size))
                        {
                            page.Ops.Add(new TextOp { Font = font, Size = f.Style.Size, X = fx, Y = baseline - f.Style.Rise, Bytes = bytes, Colour = f.Style.Colour });
                            fx += runWidth;
                        }
                    }
                    if (f.Style.Url != null || f.Style.Target != null)
                        page.Links.Add(new LinkArea { X = pos, Y = baseline - f.Ascent, W = f.Width, H = f.Ascent + f.Descent, Url = f.Style.Url, Target = f.Style.Target });
                    pos += f.Width;
                }
                if (i < line.Words.Count - 1) pos += w.SpaceAfter + gapExtra;
            }
        }

        /// <summary>Plain text of inlines, for headings in bookmarks.</summary>
        public static string Plain(IEnumerable<Inline> inlines) => InlineParser.Plain(inlines);
    }
}
