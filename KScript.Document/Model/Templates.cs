using KScript.Runtime;

namespace KScript.Documents
{
    /// <summary>The built-in looks: plain, paper, report, letter and invoice. Each is a <see cref="DocumentStyle"/> preset.</summary>
    public static class Templates
    {
        public static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>
        {
            ["plain"] = "Serif text with numbered sections and the title at the top.",
            ["paper"] = "A scientific paper: centred title, authors and abstract, indented paragraphs, numbered sections, equations and references.",
            ["report"] = "A report: a title page, a table of contents, sans-serif headings, and each top-level section on a new page.",
            ["letter"] = "A letter: the sender's address on the right, the date, the recipient, a subject line, the greeting and a signature.",
            ["invoice"] = "An invoice: from and to addresses, invoice details, a table of items with tax and a total, and payment notes.",
        };

        public static IEnumerable<string> Names => Descriptions.Keys;

        public static DocumentStyle Get(string name)
        {
            var s = new DocumentStyle { Template = (name ?? "plain").Trim().ToLowerInvariant() };
            switch (s.Template)
            {
                case "plain":
                    break;
                case "paper":
                    s.FontSize = 10.5;
                    s.LineHeight = 1.25;
                    s.ParagraphSpacing = 0;
                    s.MarginLeft = s.MarginRight = Units.Mm(27);
                    break;
                case "report":
                    s.HeadingFont = FontFamily.Sans;
                    s.TitlePage = true;
                    s.ChapterPages = true;
                    s.Accent = "#1f4e79";
                    s.Footer = "[title] | | [page]";
                    break;
                case "letter":
                    s.Font = FontFamily.Sans;
                    s.HeadingFont = FontFamily.Sans;
                    s.FontSize = 10.5;
                    s.NumberSections = false;
                    s.Align = PaperAlign.Left;
                    s.ParagraphSpacing = 0.8;
                    s.MarginTop = Units.Mm(20);
                    s.Footer = " | | Page [page] of [pages]";
                    break;
                case "invoice":
                    s.Font = FontFamily.Sans;
                    s.HeadingFont = FontFamily.Sans;
                    s.FontSize = 10;
                    s.NumberSections = false;
                    s.Align = PaperAlign.Left;
                    s.Accent = "#1f4e79";
                    s.MarginTop = Units.Mm(20);
                    s.Footer = " | | Page [page] of [pages]";
                    s.ShowTitle = false;
                    break;
                default:
                    throw new KScriptException($"template: '{name}' isn't a template. Use {string.Join(", ", Names)}.", "Argument");
            }
            return s;
        }
    }
}
