using System.Globalization;

using KScript.Runtime;

namespace KScript.Documents
{
    public enum FontFamily
    {
        Serif,
        Sans,
        Mono,
    }

    public enum CitationStyle
    {
        /// <summary>[1], numbered in order of first citation.</summary>
        Numeric,

        /// <summary>(Conway, 1970), listed by author.</summary>
        AuthorYear,
    }

    /// <summary>How a document looks: paper, margins, fonts, numbering and headers. Templates are presets of it.</summary>
    public sealed class DocumentStyle
    {
        public string Template { get; set; } = "plain";

        /// <summary>Page size in points (1/72 inch).</summary>
        public double PageWidth { get; set; } = Units.A4.Width;

        public double PageHeight { get; set; } = Units.A4.Height;

        public double MarginTop { get; set; } = Units.Mm(25);
        public double MarginBottom { get; set; } = Units.Mm(25);
        public double MarginLeft { get; set; } = Units.Mm(25);
        public double MarginRight { get; set; } = Units.Mm(25);

        public FontFamily Font { get; set; } = FontFamily.Serif;
        public FontFamily HeadingFont { get; set; } = FontFamily.Serif;

        /// <summary>Body text size in points.</summary>
        public double FontSize { get; set; } = 11;

        /// <summary>Baseline to baseline, as a multiple of the font size.</summary>
        public double LineHeight { get; set; } = 1.3;

        /// <summary>Space between paragraphs as a multiple of the line height; 0 indents first lines instead, as LaTeX does.</summary>
        public double ParagraphSpacing { get; set; } = 0.5;

        public PaperAlign Align { get; set; } = PaperAlign.Justify;

        /// <summary>Colour of headings and rules, such as #1f4e79.</summary>
        public string Accent { get; set; } = "#000000";

        public bool NumberSections { get; set; } = true;

        /// <summary>A page of its own for the title, author and date (reports).</summary>
        public bool TitlePage { get; set; }

        /// <summary>Top-level sections start on a new page.</summary>
        public bool ChapterPages { get; set; }

        /// <summary>Show the title block (title, authors, date, abstract) at the start.</summary>
        public bool ShowTitle { get; set; } = true;

        public CitationStyle Citations { get; set; } = CitationStyle.Numeric;

        /// <summary>Header and footer: "left | center | right", with [page], [pages], [title], [author] and [date].</summary>
        public string Header { get; set; } = string.Empty;

        public string Footer { get; set; } = " | [page] | ";

        /// <summary>No header or footer on the first page (the title page, or a letter's first page).</summary>
        public bool PlainFirstPage { get; set; } = true;

        public TableRules TableRules { get; set; } = TableRules.Booktabs;

        public string Language { get; set; } = "en";

        public double TextWidth => PageWidth - MarginLeft - MarginRight;


        /// <summary>Sets one style option by its attribute name, such as paper: a4 or margin: 2cm.</summary>
        public void Set(string name, string value)
        {
            value = value?.Trim() ?? string.Empty;
            switch (name)
            {
                case "paper":
                    (double w, double h) = Units.Paper(value);
                    PageWidth = w;
                    PageHeight = h;
                    break;
                case "landscape":
                    if (Values.Truthy(Values.ParseBool(value) ?? true) && PageHeight > PageWidth) (PageWidth, PageHeight) = (PageHeight, PageWidth);
                    break;
                case "margin":
                {
                    double[] m = value.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(Units.Length).ToArray();
                    if (m.Length == 0) throw new KScriptException("margin is empty. Give a size such as 2.5cm, or top/right/bottom/left sizes such as '2cm 2.5cm'.", "Argument");
                    // CSS order: all; vertical horizontal; top horizontal bottom; top right bottom left.
                    MarginTop = m[0];
                    MarginRight = m.Length > 1 ? m[1] : m[0];
                    MarginBottom = m.Length > 2 ? m[2] : m[0];
                    MarginLeft = m.Length > 3 ? m[3] : MarginRight;
                    break;
                }
                case "font":
                    Font = Units.Family(value);
                    break;
                case "heading_font":
                    HeadingFont = Units.Family(value);
                    break;
                case "size":
                    FontSize = Units.Length(value);
                    if (FontSize < 4 || FontSize > 72) throw new KScriptException($"size: {value} is too {(FontSize < 4 ? "small" : "big")} for body text. Use a size from 4pt to 72pt, such as 11.", "Argument");
                    break;
                case "line_height":
                    LineHeight = Units.Number(value, name);
                    break;
                case "align":
                    Align = Units.Align(value);
                    break;
                case "accent":
                    Accent = Units.Colour(value);
                    break;
                case "numbered":
                    NumberSections = Units.Bool(value, name);
                    break;
                case "title_page":
                    TitlePage = Units.Bool(value, name);
                    break;
                case "chapters":
                    ChapterPages = Units.Bool(value, name);
                    break;
                case "citations":
                    Citations = value.ToLowerInvariant() switch
                    {
                        "numeric" or "number" or "numbers" => CitationStyle.Numeric,
                        "author-year" or "author_year" or "authoryear" => CitationStyle.AuthorYear,
                        _ => throw new KScriptException($"citations: '{value}' isn't a citation style. Use numeric ([1]) or author-year ((Conway, 1970)).", "Argument"),
                    };
                    break;
                case "header":
                    Header = value;
                    break;
                case "footer":
                    Footer = value;
                    break;
                case "rules":
                    TableRules = Units.Rules(value);
                    break;
                case "language":
                    Language = value;
                    break;
                default:
                    throw new KScriptException($"'{name}' isn't a document setting. Use {string.Join(", ", Options)}.", "Argument");
            }
        }

        /// <summary>The style options a document element takes, for its attributes.</summary>
        public static readonly string[] Options =
        {
            "paper", "landscape", "margin", "font", "heading_font", "size", "line_height", "align", "accent", "numbered",
            "title_page", "chapters", "citations", "header", "footer", "rules", "language",
        };
    }

    /// <summary>Lengths, paper sizes, colours and option words.</summary>
    public static class Units
    {
        public static readonly (double Width, double Height) A4 = (Mm(210), Mm(297));

        public static double Mm(double mm) => mm * 72 / 25.4;

        /// <summary>A length with a unit (mm, cm, in, pt, px) into points. A bare number is points.</summary>
        public static double Length(string text)
        {
            string t = (text ?? string.Empty).Trim().ToLowerInvariant();
            double factor = 1;
            foreach (var (unit, f) in new[] { ("mm", 72 / 25.4), ("cm", 72 / 2.54), ("in", 72.0), ("pt", 1.0), ("px", 0.75) })
            {
                if (t.EndsWith(unit, StringComparison.Ordinal))
                {
                    factor = f;
                    t = t.Substring(0, t.Length - unit.Length).Trim();
                    break;
                }
            }
            if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) || n < 0)
                throw new KScriptException($"'{text}' isn't a length. Use a number with a unit, such as 2.5cm, 20mm, 1in or 11pt.", "Argument");
            return n * factor;
        }

        public static double Number(string text, string name)
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) || n <= 0)
                throw new KScriptException($"{name}: '{text}' isn't a positive number.", "Argument");
            return n;
        }

        public static bool Bool(string text, string name) =>
            Values.ParseBool(text) ?? throw new KScriptException($"{name}: '{text}' should be yes or no.", "Argument");

        public static (double Width, double Height) Paper(string name) => name.ToLowerInvariant().Replace(" ", string.Empty) switch
        {
            "a3" => (Mm(297), Mm(420)),
            "a4" => A4,
            "a5" => (Mm(148), Mm(210)),
            "b5" => (Mm(176), Mm(250)),
            "letter" or "us-letter" => (612, 792),
            "legal" => (612, 1008),
            _ => CustomPaper(name),
        };

        private static (double, double) CustomPaper(string name)
        {
            string[] parts = name.ToLowerInvariant().Split('x');
            if (parts.Length == 2)
            {
                try
                {
                    return (Length(parts[0]), Length(parts[1]));
                }
                catch (KScriptException)
                {
                    // Reported below with the paper names.
                }
            }
            throw new KScriptException($"paper: '{name}' isn't a paper size. Use a4, a3, a5, b5, letter, legal, or a size such as 15cm x 20cm.", "Argument");
        }

        public static FontFamily Family(string name) => name.ToLowerInvariant() switch
        {
            "serif" or "times" => FontFamily.Serif,
            "sans" or "sans-serif" or "helvetica" or "arial" => FontFamily.Sans,
            "mono" or "monospace" or "courier" => FontFamily.Mono,
            _ => throw new KScriptException($"font: '{name}' isn't a font. Use serif (Times), sans (Helvetica) or mono (Courier).", "Argument"),
        };

        public static PaperAlign Align(string name) => name.ToLowerInvariant() switch
        {
            "left" => PaperAlign.Left,
            "center" or "centre" => PaperAlign.Center,
            "right" => PaperAlign.Right,
            "justify" or "justified" => PaperAlign.Justify,
            _ => throw new KScriptException($"align: '{name}' should be left, center, right or justify.", "Argument"),
        };

        public static TableRules Rules(string name) => name.ToLowerInvariant() switch
        {
            "booktabs" or "lines" => TableRules.Booktabs,
            "grid" => TableRules.Grid,
            "none" => TableRules.None,
            _ => throw new KScriptException($"rules: '{name}' should be booktabs (lines above, below and under the header), grid or none.", "Argument"),
        };

        private static readonly Dictionary<string, string> Named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["black"] = "#000000", ["white"] = "#ffffff", ["grey"] = "#808080", ["gray"] = "#808080", ["red"] = "#c0392b",
            ["green"] = "#2e7d32", ["blue"] = "#1f4e79", ["navy"] = "#1b2a4a", ["teal"] = "#00796b", ["purple"] = "#6a1b9a",
            ["orange"] = "#e67e22", ["maroon"] = "#7b1e1e",
        };

        /// <summary>A colour as #rrggbb: a name such as navy, #rgb or #rrggbb.</summary>
        public static string Colour(string text)
        {
            string t = (text ?? string.Empty).Trim();
            if (Named.TryGetValue(t, out string named)) return named;
            if (t.StartsWith("#", StringComparison.Ordinal)) t = t.Substring(1);
            if (t.Length == 3) t = string.Concat(t.Select(c => $"{c}{c}"));
            if (t.Length == 6 && t.All(Uri.IsHexDigit)) return "#" + t.ToLowerInvariant();
            throw new KScriptException($"'{text}' isn't a colour. Use #rrggbb (such as #1f4e79) or a name: {string.Join(", ", Named.Keys)}.", "Argument");
        }

        /// <summary>A #rrggbb colour as red, green and blue from 0 to 1.</summary>
        public static (double R, double G, double B) Rgb(string colour)
        {
            string hex = Colour(colour).Substring(1);
            return (Convert.ToInt32(hex.Substring(0, 2), 16) / 255.0, Convert.ToInt32(hex.Substring(2, 2), 16) / 255.0, Convert.ToInt32(hex.Substring(4, 2), 16) / 255.0);
        }

        /// <summary>A number as text for files: invariant, at most 3 decimals, no trailing zeros.</summary>
        public static string N(double value)
        {
            double r = Math.Round(value, 3);
            if (r == 0) r = 0; // no -0
            return r.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
