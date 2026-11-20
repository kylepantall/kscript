using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace KScript.Documents.Pdf
{
    /// <summary>Something drawn on a page. Coordinates are points from the top-left corner; y grows downwards.</summary>
    internal abstract class Op
    {
        public abstract Op Moved(double dx, double dy);
    }

    internal sealed class TextOp : Op
    {
        public string Font;
        public double Size;
        public double X;
        public double Y;
        public byte[] Bytes;
        public string Colour;

        /// <summary>A vertical stretch, for tall brackets: 1 draws the glyph as it is.</summary>
        public double StretchY = 1;

        public override Op Moved(double dx, double dy) => new TextOp { Font = Font, Size = Size, X = X + dx, Y = Y + dy, Bytes = Bytes, Colour = Colour, StretchY = StretchY };
    }

    internal sealed class LineOp : Op
    {
        public double X1, Y1, X2, Y2, Width;
        public string Colour;

        public override Op Moved(double dx, double dy) => new LineOp { X1 = X1 + dx, Y1 = Y1 + dy, X2 = X2 + dx, Y2 = Y2 + dy, Width = Width, Colour = Colour };
    }

    internal sealed class RectOp : Op
    {
        public double X, Y, W, H;
        public string Fill;

        public override Op Moved(double dx, double dy) => new RectOp { X = X + dx, Y = Y + dy, W = W, H = H, Fill = Fill };
    }

    /// <summary>A path of lines and curves: points are (x, y) pairs; 'M' moves, 'L' draws a line, 'C' a curve.</summary>
    internal sealed class PathOp : Op
    {
        public List<(char Kind, double[] Points)> Parts = new List<(char, double[])>();
        public double Width;
        public bool Fill;
        public string Colour;

        public override Op Moved(double dx, double dy) => new PathOp
        {
            Parts = Parts.Select(p => (p.Kind, p.Points.Select((v, i) => i % 2 == 0 ? v + dx : v + dy).ToArray())).ToList(),
            Width = Width,
            Fill = Fill,
            Colour = Colour,
        };
    }

    internal sealed class ImageOp : Op
    {
        public ImageData Image;
        public double X, Y, W, H;

        public override Op Moved(double dx, double dy) => new ImageOp { Image = Image, X = X + dx, Y = Y + dy, W = W, H = H };
    }

    /// <summary>A clickable area: a web link, or a jump to a place in the document.</summary>
    internal sealed class LinkArea
    {
        public double X, Y, W, H;
        public string Url;
        public string Target;
    }

    internal sealed class PdfPage
    {
        public List<Op> Ops { get; } = new List<Op>();
        public List<LinkArea> Links { get; } = new List<LinkArea>();
    }

    internal sealed class Bookmark
    {
        public string Title;
        public int Level;
        public int Page;
        public double Y;
    }

    /// <summary>Writes pages of drawing operations as a PDF 1.7 file: base 14 fonts, Flate-compressed streams, links and bookmarks.</summary>
    internal static class PdfFile
    {
        private static string N(double v) => Units.N(v);

        public static byte[] Write(List<PdfPage> pages, double width, double height, Dictionary<string, (int Page, double Y)> destinations,
            List<Bookmark> bookmarks, Dictionary<string, string> info)
        {
            var objects = new List<byte[]>();
            int Reserve()
            {
                objects.Add(null);
                return objects.Count;
            }
            void Set(int id, string content) => objects[id - 1] = Encoding.Latin1.GetBytes(content);
            void SetStream(int id, string dict, byte[] data)
            {
                var head = Encoding.Latin1.GetBytes($"<< {dict} /Length {data.Length} >>\nstream\n");
                var tail = Encoding.Latin1.GetBytes("\nendstream");
                objects[id - 1] = head.Concat(data).Concat(tail).ToArray();
            }

            int catalog = Reserve(), pagesId = Reserve(), infoId = Reserve();
            var pageIds = pages.Select(_ => Reserve()).ToList();

            // Fonts and images, numbered as they are first used.
            var fonts = new Dictionary<string, int>();
            var images = new Dictionary<ImageData, (int Id, string Name)>();
            foreach (Op op in pages.SelectMany(p => p.Ops))
            {
                if (op is TextOp t && !fonts.ContainsKey(t.Font)) fonts[t.Font] = Reserve();
                if (op is ImageOp i && !images.ContainsKey(i.Image)) images[i.Image] = (Reserve(), "Im" + (images.Count + 1));
            }
            var fontNames = fonts.Keys.Select((f, i) => (f, "F" + (i + 1))).ToDictionary(x => x.f, x => x.Item2);
            foreach (var (font, id) in fonts)
            {
                string encoding = font == StandardFonts.Symbol ? string.Empty : " /Encoding /WinAnsiEncoding";
                Set(id, $"<< /Type /Font /Subtype /Type1 /BaseFont /{font}{encoding} >>");
            }
            foreach (var (image, (id, _)) in images) WriteImage(image, id, Reserve, SetStream);

            string resources = "<< /Font << " + string.Join(" ", fonts.Select(f => $"/{fontNames[f.Key]} {f.Value} 0 R")) + " >> /XObject << "
                + string.Join(" ", images.Select(i => $"/{i.Value.Name} {i.Value.Id} 0 R")) + " >> /ProcSet [/PDF /Text /ImageB /ImageC] >>";

            for (int p = 0; p < pages.Count; p++)
            {
                byte[] content = Content(pages[p], height, fontNames, images.ToDictionary(i => i.Key, i => i.Value.Name));
                int contentId = Reserve();
                SetStream(contentId, "/Filter /FlateDecode", ImageData.Compress(content));
                var annots = new List<int>();
                foreach (LinkArea link in pages[p].Links)
                {
                    string action;
                    if (link.Url != null) action = $"/A << /S /URI /URI ({Escape(link.Url)}) >>";
                    else if (link.Target != null && destinations.TryGetValue(link.Target, out var dest)) action = $"/Dest [{pageIds[dest.Page]} 0 R /XYZ 0 {N(height - dest.Y)} null]";
                    else continue;
                    int a = Reserve();
                    Set(a, $"<< /Type /Annot /Subtype /Link /Rect [{N(link.X)} {N(height - link.Y - link.H)} {N(link.X + link.W)} {N(height - link.Y)}] /Border [0 0 0] {action} >>");
                    annots.Add(a);
                }
                string annotText = annots.Count > 0 ? " /Annots [" + string.Join(" ", annots.Select(a => a + " 0 R")) + "]" : string.Empty;
                Set(pageIds[p], $"<< /Type /Page /Parent {pagesId} 0 R /MediaBox [0 0 {N(width)} {N(height)}] /Resources {resources} /Contents {contentId} 0 R{annotText} >>");
            }
            Set(pagesId, $"<< /Type /Pages /Kids [{string.Join(" ", pageIds.Select(id => id + " 0 R"))}] /Count {pages.Count} >>");

            string outlines = string.Empty;
            if (bookmarks.Count > 0)
            {
                int root = Reserve();
                WriteOutline(bookmarks, root, pageIds, height, Reserve, Set);
                outlines = $" /Outlines {root} 0 R /PageMode /UseOutlines";
            }
            Set(catalog, $"<< /Type /Catalog /Pages {pagesId} 0 R{outlines} >>");
            Set(infoId, "<< " + string.Join(" ", info.Where(kv => !string.IsNullOrEmpty(kv.Value)).Select(kv => $"/{kv.Key} {TextString(kv.Value)}")) + " /Producer (KScript.Document) >>");

            using var output = new MemoryStream();
            void Raw(string s) => output.Write(Encoding.Latin1.GetBytes(s));
            Raw("%PDF-1.7\n%\u00E2\u00E3\u00CF\u00D3\n");
            var offsets = new List<long>();
            for (int i = 0; i < objects.Count; i++)
            {
                offsets.Add(output.Position);
                Raw($"{i + 1} 0 obj\n");
                output.Write(objects[i]);
                Raw("\nendobj\n");
            }
            long xref = output.Position;
            Raw($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
            foreach (long o in offsets) Raw(o.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
            Raw($"trailer\n<< /Size {objects.Count + 1} /Root {catalog} 0 R /Info {infoId} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
            return output.ToArray();
        }

        private static void WriteImage(ImageData image, int id, Func<int> reserve, Action<int, string, byte[]> setStream)
        {
            string size = $"/Type /XObject /Subtype /Image /Width {image.Width} /Height {image.Height} /BitsPerComponent 8";
            if (image.Format == "jpeg")
            {
                string space = image.Components switch { 1 => "/DeviceGray", 4 => "/DeviceCMYK /Decode [1 0 1 0 1 0 1 0]", _ => "/DeviceRGB" };
                setStream(id, $"{size} /ColorSpace {space} /Filter /DCTDecode", image.Bytes);
                return;
            }
            var (colour, alpha, components) = image.DecodePng();
            string smask = string.Empty;
            if (alpha != null)
            {
                int mask = reserve();
                setStream(mask, $"/Type /XObject /Subtype /Image /Width {image.Width} /Height {image.Height} /BitsPerComponent 8 /ColorSpace /DeviceGray /Filter /FlateDecode", alpha);
                smask = $" /SMask {mask} 0 R";
            }
            setStream(id, $"{size} /ColorSpace {(components == 1 ? "/DeviceGray" : "/DeviceRGB")} /Filter /FlateDecode{smask}", colour);
        }

        private static void WriteOutline(List<Bookmark> marks, int root, List<int> pageIds, double height, Func<int> reserve, Action<int, string> set)
        {
            var ids = marks.Select(_ => reserve()).ToList();
            // Parent of each bookmark: the nearest earlier one with a smaller level.
            var parent = new int[marks.Count];
            for (int i = 0; i < marks.Count; i++)
            {
                parent[i] = -1;
                for (int j = i - 1; j >= 0; j--)
                {
                    if (marks[j].Level < marks[i].Level)
                    {
                        parent[i] = j;
                        break;
                    }
                }
            }
            List<int> Children(int p) => Enumerable.Range(0, marks.Count).Where(i => parent[i] == p).ToList();
            int Count(int p) => Children(p).Sum(c => 1 + Count(c));
            void Level(int p)
            {
                List<int> kids = Children(p);
                for (int k = 0; k < kids.Count; k++)
                {
                    int i = kids[k];
                    Bookmark b = marks[i];
                    var sb = new StringBuilder($"<< /Title {TextString(b.Title)} /Parent {(p < 0 ? root : ids[p])} 0 R /Dest [{pageIds[b.Page]} 0 R /XYZ 0 {N(height - b.Y)} null]");
                    if (k > 0) sb.Append($" /Prev {ids[kids[k - 1]]} 0 R");
                    if (k < kids.Count - 1) sb.Append($" /Next {ids[kids[k + 1]]} 0 R");
                    List<int> grand = Children(i);
                    if (grand.Count > 0) sb.Append($" /First {ids[grand[0]]} 0 R /Last {ids[grand[^1]]} 0 R /Count -{Count(i)}");
                    sb.Append(" >>");
                    set(ids[i], sb.ToString());
                    Level(i);
                }
            }
            Level(-1);
            List<int> top = Children(-1);
            set(root, $"<< /Type /Outlines /First {ids[top[0]]} 0 R /Last {ids[top[^1]]} 0 R /Count {Count(-1)} >>");
        }

        /// <summary>A PDF text string: Latin text as is, anything else as UTF-16.</summary>
        internal static string TextString(string s)
        {
            if (s.All(c => c < 128)) return "(" + Escape(s) + ")";
            var sb = new StringBuilder("<FEFF");
            foreach (char c in s) sb.Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
            return sb.Append('>').ToString();
        }

        internal static string Escape(string s) => s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)").Replace("\r", "\\r").Replace("\n", "\\n");

        private static string Colour(string colour, bool fill)
        {
            var (r, g, b) = Units.Rgb(colour ?? "#000000");
            return $"{N(r)} {N(g)} {N(b)} {(fill ? "rg" : "RG")}";
        }

        private static byte[] Content(PdfPage page, double height, Dictionary<string, string> fonts, Dictionary<ImageData, string> images)
        {
            var sb = new StringBuilder();
            foreach (Op op in page.Ops)
            {
                switch (op)
                {
                    case TextOp t:
                    {
                        sb.Append("BT ").Append(Colour(t.Colour, fill: true)).Append(" /").Append(fonts[t.Font]).Append(' ').Append(N(t.Size)).Append(" Tf ");
                        if (t.StretchY != 1) sb.Append("1 0 0 ").Append(N(t.StretchY)).Append(' ').Append(N(t.X)).Append(' ').Append(N(height - t.Y)).Append(" Tm (");
                        else sb.Append(N(t.X)).Append(' ').Append(N(height - t.Y)).Append(" Td (");
                        foreach (byte b in t.Bytes)
                        {
                            if (b == '(' || b == ')' || b == '\\') sb.Append('\\').Append((char)b);
                            else if (b < 32 || b > 126) sb.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
                            else sb.Append((char)b);
                        }
                        sb.Append(") Tj ET\n");
                        break;
                    }
                    case LineOp l:
                        sb.Append(Colour(l.Colour, fill: false)).Append(' ').Append(N(l.Width)).Append(" w ").Append(N(l.X1)).Append(' ').Append(N(height - l.Y1)).Append(" m ")
                            .Append(N(l.X2)).Append(' ').Append(N(height - l.Y2)).Append(" l S\n");
                        break;
                    case RectOp r:
                        sb.Append(Colour(r.Fill, fill: true)).Append(' ').Append(N(r.X)).Append(' ').Append(N(height - r.Y - r.H)).Append(' ').Append(N(r.W)).Append(' ').Append(N(r.H)).Append(" re f\n");
                        break;
                    case PathOp p:
                    {
                        sb.Append(Colour(p.Colour, fill: p.Fill)).Append(' ');
                        if (!p.Fill) sb.Append(N(p.Width)).Append(" w 1 J 1 j ");
                        foreach (var (kind, pts) in p.Parts)
                        {
                            for (int i = 0; i < pts.Length; i += 2) sb.Append(N(pts[i])).Append(' ').Append(N(height - pts[i + 1])).Append(' ');
                            sb.Append(kind switch { 'M' => "m ", 'C' => "c ", _ => "l " });
                        }
                        sb.Append(p.Fill ? "f\n" : "S\n");
                        break;
                    }
                    case ImageOp i:
                        sb.Append("q ").Append(N(i.W)).Append(" 0 0 ").Append(N(i.H)).Append(' ').Append(N(i.X)).Append(' ').Append(N(height - i.Y - i.H)).Append(" cm /").Append(images[i.Image]).Append(" Do Q\n");
                        break;
                }
            }
            return Encoding.Latin1.GetBytes(sb.ToString());
        }
    }
}
