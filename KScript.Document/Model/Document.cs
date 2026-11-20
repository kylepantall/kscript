using KScript.Documents.Maths;

namespace KScript.Documents
{
    /// <summary>
    /// A document: metadata, a page style and a tree of blocks. Scripts build one with a <c>KScript.Document { }</c>
    /// block; the writers in Export/ and Pdf/ turn it into PDF, LaTeX, Word, Markdown or HTML.
    /// </summary>
    public sealed class Document
    {
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public List<Author> Authors { get; } = new List<Author>();
        public string Date { get; set; }
        public List<string> Keywords { get; } = new List<string>();

        /// <summary>The abstract, shown under the title (papers) or on the title page (reports).</summary>
        public List<Block> Abstract { get; } = new List<Block>();

        public DocumentStyle Style { get; set; } = Templates.Get("plain");

        public List<Block> Blocks { get; } = new List<Block>();

        /// <summary>Works cited with [@key], by key. Bibliography blocks list the ones that are cited.</summary>
        public Dictionary<string, Source> Sources { get; } = new Dictionary<string, Source>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Where relative image paths start.</summary>
        public string BaseDirectory { get; set; } = Directory.GetCurrentDirectory();

        /// <summary>Every block, depth first, including those inside sections, lists, quotes and items.</summary>
        public IEnumerable<Block> AllBlocks() => Walk(Abstract).Concat(Walk(Blocks));

        internal static IEnumerable<Block> Walk(IEnumerable<Block> blocks)
        {
            foreach (Block b in blocks)
            {
                yield return b;
                foreach (Block child in Walk(b.Children())) yield return child;
            }
        }
    }

    public sealed class Author
    {
        public string Name { get; set; }
        public string Affiliation { get; set; }
        public string Email { get; set; }

        public override string ToString() => Name;
    }

    public enum PaperAlign
    {
        Left,
        Center,
        Right,
        Justify,
    }

    public enum TableRules
    {
        /// <summary>Lines above and below the table and under the header, as in scientific papers.</summary>
        Booktabs,

        /// <summary>Lines around every cell.</summary>
        Grid,

        /// <summary>No lines.</summary>
        None,
    }

    public enum ListStyle
    {
        Bullet,
        Number,
        Letter,
        Roman,
    }

    // ---- blocks ------------------------------------------------------------------------------------------------------

    public abstract class Block
    {
        /// <summary>The name [#id] refers to it by, or null.</summary>
        public string Id { get; set; }

        public virtual IEnumerable<Block> Children() => Array.Empty<Block>();
    }

    /// <summary>A section with a heading. Sections inside sections are subsections.</summary>
    public sealed class Section : Block
    {
        public List<Inline> Title { get; set; } = new List<Inline>();
        public bool Numbered { get; set; } = true;
        public List<Block> Blocks { get; } = new List<Block>();

        /// <summary>1 for a top-level section, 2 for a subsection, and so on. Set by <see cref="Resolver"/>.</summary>
        public int Level { get; set; } = 1;

        /// <summary>Such as "2.1", or null when it isn't numbered. Set by <see cref="Resolver"/>.</summary>
        public string Number { get; set; }

        /// <summary>Starts on a new page (report chapters).</summary>
        public bool NewPage { get; set; }

        public override IEnumerable<Block> Children() => Blocks;
    }

    public sealed class Paragraph : Block
    {
        public List<Inline> Inlines { get; set; } = new List<Inline>();
        public PaperAlign? Align { get; set; }
        public bool Bold { get; set; }

        /// <summary>Text size relative to the body, such as 1.4 for a large line.</summary>
        public double Scale { get; set; } = 1;

        /// <summary>Extra space above, in points.</summary>
        public double SpaceBefore { get; set; }

        /// <summary>No first-line indent, for paragraphs that aren't running text (addresses, dates).</summary>
        public bool NoIndent { get; set; }

        /// <summary>Keep the line breaks written in the text, as in an address.</summary>
        public bool KeepLines { get; set; }

        public string Color { get; set; }
    }

    public sealed class ListBlock : Block
    {
        public ListStyle Style { get; set; }
        public int Start { get; set; } = 1;
        public List<ListItem> Items { get; } = new List<ListItem>();

        public bool Ordered => Style != ListStyle.Bullet;

        public override IEnumerable<Block> Children() => Items.SelectMany(i => i.Blocks);

        /// <summary>The marker of item <paramref name="index"/> (from 0): •, 3., c. or iv.</summary>
        public string Marker(int index)
        {
            int n = Start + index;
            return Style switch
            {
                ListStyle.Number => n + ".",
                ListStyle.Letter => Letters(n) + ".",
                ListStyle.Roman => Roman(n).ToLowerInvariant() + ".",
                _ => "•",
            };
        }

        internal static string Letters(int n)
        {
            string s = string.Empty;
            while (n > 0)
            {
                n--;
                s = (char)('a' + n % 26) + s;
                n /= 26;
            }
            return s;
        }

        internal static string Roman(int n)
        {
            var parts = new (int, string)[] { (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"), (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I") };
            var sb = new System.Text.StringBuilder();
            foreach (var (value, text) in parts)
            {
                while (n >= value)
                {
                    sb.Append(text);
                    n -= value;
                }
            }
            return sb.ToString();
        }
    }

    public sealed class ListItem
    {
        public List<Block> Blocks { get; } = new List<Block>();
    }

    public enum ColumnAlign
    {
        Left,
        Center,
        Right,
    }

    public sealed class TableCell
    {
        public List<Inline> Inlines { get; set; } = new List<Inline>();
        public bool Bold { get; set; }

        /// <summary>How many columns it covers (the last cells of a totals row, for example).</summary>
        public int Span { get; set; } = 1;
    }

    public sealed class TableRow
    {
        public List<TableCell> Cells { get; } = new List<TableCell>();
        public bool Header { get; set; }

        /// <summary>A line above this row, such as above an invoice's total.</summary>
        public bool RuleAbove { get; set; }
    }

    public sealed class Table : Block
    {
        public List<Inline> Caption { get; set; }
        public List<TableRow> Rows { get; } = new List<TableRow>();
        public List<ColumnAlign> Align { get; } = new List<ColumnAlign>();

        /// <summary>Relative column widths for a full-width table, such as [3, 1, 1]; empty sizes columns to their content.</summary>
        public List<double> Widths { get; } = new List<double>();

        public TableRules Rules { get; set; } = TableRules.Booktabs;

        /// <summary>Set by <see cref="Resolver"/> when the table has a caption.</summary>
        public int Number { get; set; }

        /// <summary>A smaller text size for the cells, relative to the body.</summary>
        public double Scale { get; set; } = 1;

        public int ColumnCount => Rows.Count == 0 ? 0 : Rows.Max(r => r.Cells.Sum(c => Math.Max(1, c.Span)));

        public ColumnAlign AlignOf(int column) => column < Align.Count ? Align[column] : ColumnAlign.Left;
    }

    public sealed class Figure : Block
    {
        /// <summary>The image file (PNG or JPEG), relative to the document's folder.</summary>
        public string Source { get; set; }

        public List<Inline> Caption { get; set; }

        /// <summary>The share of the text width, from 0 to 1.</summary>
        public double Width { get; set; } = 0;

        public int Number { get; set; }

        internal ImageData Image { get; set; }
    }

    public sealed class MathBlock : Block
    {
        public string Latex { get; set; }
        public MathNode Node { get; set; }
        public bool Numbered { get; set; } = true;
        public int Number { get; set; }
    }

    public sealed class CodeBlock : Block
    {
        public string Language { get; set; }
        public string Text { get; set; }
    }

    public sealed class Quote : Block
    {
        public List<Block> Blocks { get; } = new List<Block>();
        public string Attribution { get; set; }

        public override IEnumerable<Block> Children() => Blocks;
    }

    public sealed class PageBreak : Block
    {
    }

    /// <summary>The table of contents.</summary>
    public sealed class Contents : Block
    {
        public string Title { get; set; } = "Contents";

        /// <summary>How deep it goes: 2 lists sections and subsections.</summary>
        public int Depth { get; set; } = 3;
    }

    /// <summary>The list of works cited.</summary>
    public sealed class Bibliography : Block
    {
        public string Title { get; set; } = "References";
    }

    /// <summary>Space, in points.</summary>
    public sealed class Spacer : Block
    {
        public double Height { get; set; } = 12;
    }

    /// <summary>A horizontal line across the text.</summary>
    public sealed class Rule : Block
    {
    }

    // ---- inlines -----------------------------------------------------------------------------------------------------

    public abstract class Inline
    {
    }

    public sealed class TextInline : Inline
    {
        public TextInline(string text) => Text = text;

        public string Text { get; set; }
    }

    public sealed class StrongInline : Inline
    {
        public List<Inline> Children { get; } = new List<Inline>();
    }

    public sealed class EmphasisInline : Inline
    {
        public List<Inline> Children { get; } = new List<Inline>();
    }

    public sealed class CodeInline : Inline
    {
        public CodeInline(string text) => Text = text;

        public string Text { get; }
    }

    public sealed class LinkInline : Inline
    {
        public string Url { get; set; }
        public List<Inline> Children { get; } = new List<Inline>();
    }

    public sealed class MathInline : Inline
    {
        public MathInline(string latex, MathNode node)
        {
            Latex = latex;
            Node = node;
        }

        public string Latex { get; }
        public MathNode Node { get; }
    }

    /// <summary>[@key] or [@a; @b]. <see cref="Text"/> is filled in by <see cref="Resolver"/>, such as [1, 2].</summary>
    public sealed class CiteInline : Inline
    {
        public List<string> Keys { get; } = new List<string>();
        public string Text { get; set; }
    }

    /// <summary>[#id]: the number of a section, table, figure or equation. Filled in by <see cref="Resolver"/>.</summary>
    public sealed class RefInline : Inline
    {
        public RefInline(string id) => Id = id;

        public string Id { get; }
        public string Text { get; set; }
    }

    /// <summary>^[text]: a footnote. <see cref="Number"/> is set by <see cref="Resolver"/>.</summary>
    public sealed class FootnoteInline : Inline
    {
        public List<Inline> Children { get; } = new List<Inline>();
        public int Number { get; set; }
    }

    public sealed class LineBreakInline : Inline
    {
    }

    /// <summary>A placeholder in a header or footer: the page number, the page count.</summary>
    public sealed class FieldInline : Inline
    {
        public FieldInline(string name) => Name = name;

        public string Name { get; }
    }

    /// <summary>A work cited: a book, an article, a web page.</summary>
    public sealed class Source
    {
        public string Key { get; set; }
        public string Type { get; set; } = "misc";
        public Dictionary<string, string> Fields { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string this[string field] => Fields.TryGetValue(field, out string v) && !string.IsNullOrWhiteSpace(v) ? v : null;

        /// <summary>Set by <see cref="Resolver"/>: the source's number in order of first citation.</summary>
        public int Number { get; set; }
    }
}
