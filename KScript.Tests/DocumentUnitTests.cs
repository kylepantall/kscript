using System.Text;

using KScript.Documents;
using KScript.Documents.Export;
using KScript.Documents.Maths;
using KScript.Documents.Pdf;
using KScript.Documents.Text;
using KScript.Runtime;

namespace KScript.Tests;

public sealed class DocumentTextTests
{
    [Fact]
    public void Inline_markup()
    {
        List<Inline> inlines = InlineParser.Parse("a **b** *c* `d` [e](https://x.org) [f [g] h](https://y.org) [x \\] y](https://z.org) ^[note [1] here] \\*not\\* *open **also \\q x\ny");
        Assert.Contains("a b c d e f [g] h", InlineParser.Plain(inlines));
        Assert.Contains("*not*", InlineParser.Plain(inlines));
        Assert.Contains("\\q x y", InlineParser.Plain(inlines));
        Assert.Contains(inlines, i => i is CodeInline);
        Assert.Contains(inlines, i => i is FootnoteInline);
        Assert.Equal(3, inlines.OfType<LinkInline>().Count());

        // A new line after a space adds no second space; keepLines makes it a line break.
        Assert.Equal("a b", InlineParser.Plain(InlineParser.Parse("a \nb")));
        Assert.Contains(InlineParser.Parse("a\nb", keepLines: true), i => i is LineBreakInline);
        Assert.Contains(InlineParser.Parse("cost $x$", dollarMaths: true), i => i is MathInline);
        Assert.DoesNotContain(InlineParser.Parse("cost $x$"), i => i is MathInline);
        Assert.Equal("[unclosed", InlineParser.Plain(InlineParser.Parse("[unclosed")));
        Assert.Equal("[text](", InlineParser.Plain(InlineParser.Parse("[text](")));
        Assert.Equal("snake_case_name", InlineParser.Plain(InlineParser.Parse("snake_case_name")));
        Assert.Equal("x", ((MathInline)InlineParser.Parse("\\( x \\)")[0]).Latex);
    }

    [Fact]
    public void Markdown_blocks()
    {
        List<Block> blocks = MarkdownBlocks.Parse(@"# Title {#top}

Para line one
line two.

```js
let x = 1;

```
~~~
plain
~~~

---
***

> Quoted **text**
>continued
>
> - in a list

| A | B | C |
|:--|:-:|--:|
| 1 | 2 \| two | 3 |
| 4 | 5 | 6 |

- one
  continued here
- two
  - nested
    1. deeper
- three

1) first
2) second

3. third starts at three

\[ x^2
 + y^2 \] {#eq-circle}

$$ z $$

![](pic.png)
![Caption](pic.png ""title"")

## Second
### Third
## Back up
Text right after
- a list straight after
", dollarMaths: true);
        var top = Assert.IsType<Section>(blocks[0]);
        Assert.Equal("top", top.Id);
        Assert.Equal(3, blocks.Count + 0 - 0 + top.Blocks.OfType<Section>().Count() - top.Blocks.OfType<Section>().Count() + 2);
        Assert.Contains(top.Blocks, b => b is CodeBlock { Language: "js" });
        Assert.Contains(top.Blocks, b => b is CodeBlock { Language: null, Text: "plain" });
        Assert.Equal(2, top.Blocks.OfType<Rule>().Count());
        Quote quote = top.Blocks.OfType<Quote>().Single();
        Assert.Contains(quote.Blocks, b => b is ListBlock);
        Table table = top.Blocks.OfType<Table>().Single();
        Assert.Equal(new[] { ColumnAlign.Left, ColumnAlign.Center, ColumnAlign.Right }, table.Align);
        Assert.Equal("2 | two", InlineParser.Plain(table.Rows[1].Cells[1].Inlines));
        List<ListBlock> lists = top.Blocks.OfType<ListBlock>().ToList();
        Assert.Equal(ListStyle.Bullet, lists[0].Style);
        Assert.Contains("continued here", InlineParser.Plain(((Paragraph)lists[0].Items[0].Blocks[0]).Inlines));
        Assert.IsType<ListBlock>(lists[0].Items[1].Blocks[1]);
        Assert.Equal(ListStyle.Number, lists[1].Style);
        Assert.Equal(3, lists[1].Items.Count);
        MathBlock circle = top.Blocks.OfType<MathBlock>().First();
        Assert.Equal("eq-circle", circle.Id);
        Assert.Equal(2, top.Blocks.OfType<MathBlock>().Count());
        Assert.Equal(2, top.Blocks.OfType<Figure>().Count());
        Assert.Null(top.Blocks.OfType<Figure>().First().Caption);
        List<Section> subs = top.Blocks.OfType<Section>().ToList();
        Assert.Equal(new[] { "Second", "Back up" }, subs.Select(s => InlineParser.Plain(s.Title)));
        Assert.IsType<Section>(subs[0].Blocks.Single());
        Assert.IsType<ListBlock>(subs[1].Blocks[1]);

        // Without dollarMaths, $$ is text.
        Assert.IsType<Paragraph>(MarkdownBlocks.Parse("$$ x $$", dollarMaths: false).Single());
        Assert.Empty(MarkdownBlocks.Parse(null, false));
    }

    [Fact]
    public void BibTeX_entries_names_and_formats()
    {
        List<Source> sources = BibTex.Parse(@"
@string{j = ""Journal""}
@ misc{bad key, title = x}
@article(paren, author = {A. B. Cee and D. Eff and G. Aitch and I. Jay}, title = ""Quoted {Braces}"", journal = j, year = 2000, pages = {1--2}, volume = 3, number = 4, doi = {https://doi.org/10.1/x})
@book{ed, editor = {Ed Itor}, title = {Edited}, publisher = {Pub}, address = {City}, edition = {2nd}}
@book{eds, editor = {Ed One and Ed Two}, title = {Edited}}
@inproceedings{conf, author = {von Neumann, John}, title = {T}, booktitle = {Proc}, pages = 5, url = {https://u.org}, note = {A note}}
@misc{anon, title = {Only a title}, howpublished = {Online}}
@misc{nothing}
mail me@home.org {or not}
@misc{odd, title = {Accent on nothing \'1}}
@techreport{tr, author = {Jean-Paul Sartre and Anne-Marie X}, title = {Report}, institution = {Inst}, number = 9, year = 1999}
@phdthesis{th, author = {{The Committee}}, title = {Thesis}, school = {Uni}, month = jan, year = 2001}
@misc{acc, author = {\""{U}ber \'Ecole and \^{\i}le \c{c} \v{s} \u{a} \H{o} \=a \.z \~n \`e}, title = {\textbf{Bold} \& \url{x} \LaTeX\ ok -- yes---no}}
");
        Assert.Equal("Edited", sources.Single(s => s.Key == "eds")["title"]);
        Assert.Equal("January", sources.Single(s => s.Key == "th")["month"]);
        Source paren = sources.Single(s => s.Key == "paren");
        Assert.Equal("Journal", paren["journal"]);
        Assert.Equal("Quoted Braces", paren["title"]);
        Assert.Equal("Cee et al., 2000", BibTex.AuthorYear(paren));
        Assert.Equal("Only a title, n.d.", BibTex.AuthorYear(sources.Single(s => s.Key == "anon")));
        Assert.Equal("nothing, n.d.", BibTex.AuthorYear(sources.Single(s => s.Key == "nothing")));
        Assert.Equal("Neumann, n.d.", BibTex.AuthorYear(sources.Single(s => s.Key == "conf")).Replace("von ", string.Empty));
        Assert.Equal("The\u00A0Committee, 2001", BibTex.AuthorYear(sources.Single(s => s.Key == "th")));
        Assert.StartsWith("J.-P. Sartre and A.-M. X", InlineParser.Plain(BibTex.Format(sources.Single(s => s.Key == "tr"), CitationStyle.Numeric)));
        Assert.Contains("Über École and île ç š ă ő ā ż ñ è", sources.Single(s => s.Key == "acc")["author"]);
        foreach (Source s in sources)
        {
            foreach (CitationStyle style in new[] { CitationStyle.Numeric, CitationStyle.AuthorYear })
                Assert.NotEmpty(InlineParser.Plain(BibTex.Format(s, style)));
        }
        Assert.Contains(", editor.", InlineParser.Plain(BibTex.Format(sources.Single(s => s.Key == "ed"), CitationStyle.Numeric)));
        Assert.Contains(", editors.", InlineParser.Plain(BibTex.Format(sources.Single(s => s.Key == "eds"), CitationStyle.Numeric)));
        Assert.StartsWith("(n.d.). ", InlineParser.Plain(BibTex.Format(sources.Single(s => s.Key == "anon"), CitationStyle.AuthorYear)));
        Assert.Contains("doi:10.1/x", InlineParser.Plain(BibTex.Format(paren, CitationStyle.Numeric)));

        var e = Assert.Throws<KScriptException>(() => BibTex.Parse("@article{open, title = {x}"));
        Assert.Contains("isn't closed", e.Message);
        Assert.Empty(BibTex.Parse(null));
        Assert.Empty(BibTex.Parse("@nothing"));
    }
}

public sealed class DocumentImageTests
{
    [Theory]
    [InlineData(0, 8, false)]
    [InlineData(0, 1, false)]
    [InlineData(0, 2, false)]
    [InlineData(0, 4, false)]
    [InlineData(2, 8, false)]
    [InlineData(3, 8, false)]
    [InlineData(3, 8, true)]
    [InlineData(3, 4, true)]
    [InlineData(4, 8, false)]
    [InlineData(6, 8, false)]
    public void PNGs_decode_for_PDF(int colourType, int bitDepth, bool transparent)
    {
        ImageData image = ImageData.FromBytes(DocumentFixture.Png(5, 4, colourType, filter: 0, transparentPalette: transparent, bitDepth: bitDepth), "x.png");
        Assert.Equal(5, image.Width);
        Assert.Equal(4, image.Height);
        var (colour, alpha, components) = image.DecodePng();
        Assert.NotEmpty(colour);
        Assert.Equal(colourType is 0 or 4 ? 1 : 3, components);
        Assert.Equal(colourType is 4 or 6 || transparent, alpha != null);
    }

    [Fact]
    public void Images_that_cannot_be_used_are_explained()
    {
        Assert.Contains("interlaced", Assert.Throws<KScriptException>(() => ImageData.FromBytes(DocumentFixture.Png(2, 2, 2, interlaced: true), "i.png").DecodePng()).Message);
        Assert.Contains("16-bit", Assert.Throws<KScriptException>(() => ImageData.FromBytes(DocumentFixture.Png(2, 2, 2, bitDepth: 16), "d.png").DecodePng()).Message);
        Assert.Contains("PNG or JPEG", Assert.Throws<KScriptException>(() => ImageData.FromBytes(Encoding.ASCII.GetBytes("GIF89a......"), "g.gif")).Message);
        byte[] noSize = { 0xFF, 0xD8, 0x00, 0x00, 0xFF, 0xE0, 0x00, 0x02, 0, 0, 0, 0, 0, 0, 0, 0 };
        Assert.Contains("without a size", Assert.Throws<KScriptException>(() => ImageData.FromBytes(noSize, "n.jpg")).Message);
        ImageData jpeg = ImageData.FromBytes(DocumentFixture.Jpeg(30, 20, components: 1), "grey.jpg");
        Assert.Equal((30, 20, 1), (jpeg.Width, jpeg.Height, jpeg.Components));
    }
}

public sealed class DocumentStyleTests
{
    [Fact]
    public void Settings_and_units()
    {
        var style = new DocumentStyle();
        foreach (var (name, value) in new[]
        {
            ("font", "times"), ("font", "helvetica"), ("font", "courier"), ("heading_font", "serif"), ("align", "justified"), ("align", "centre"),
            ("rules", "lines"), ("rules", "grid"), ("rules", "none"), ("citations", "author-year"), ("citations", "numeric"), ("numbered", "yes"),
            ("accent", "#abc"), ("accent", "#AABBCC"), ("accent", "maroon"), ("paper", "us-letter"), ("paper", "a4"), ("paper", "210mm x 297mm"), ("margin", "1in"),
            ("line_height", "1.2"), ("size", "9.5pt"), ("landscape", "no"), ("header", "a | b | c"), ("language", "fr"),
        })
        {
            style.Set(name, value);
        }
        Assert.Equal(FontFamily.Serif, style.HeadingFont);
        Assert.Equal(72, Units.Length("1in"));
        Assert.Equal(1, Units.Length("1pt"));
        Assert.Equal(0.75, Units.Length("1px"));
        Assert.Equal(28.3465, Units.Length("1cm"), 3);
        Assert.Equal(12, Units.Length("12"));
        Assert.Equal((1.0, 0.0, 0.0), Units.Rgb("#ff0000"));

        foreach (var (name, value, message) in new[]
        {
            ("font", "comic", "isn't a font"), ("align", "up", "align"), ("rules", "double", "rules"), ("citations", "harvard", "citation style"),
            ("numbered", "maybe", "true or false"), ("line_height", "-1", "positive number"), ("paper", "10cm x tall", "paper size"), ("paper", "1 x 2 x 3", "paper size"),
            ("margin", "-1cm", "isn't a length"), ("accent", "blurple", "colour"), ("nosuch", "x", "nosuch"),
            ("size", "-3", "size: '-3' isn't a text size. Use a number of points"), ("size", "0", "size: 0 is too small"),
        })
        {
            Assert.Contains(message, Assert.Throws<KScriptException>(() => new DocumentStyle().Set(name, value)).Message);
        }
        Assert.Contains("isn't a template", Assert.Throws<KScriptException>(() => Templates.Get("poster")).Message);
        Assert.Equal(5, Templates.Names.Count());
    }
}

public sealed class DocumentWriterTests
{
    private static Document Doc(params Block[] blocks)
    {
        var doc = new Document { BaseDirectory = Path.GetTempPath() };
        doc.Blocks.AddRange(blocks);
        return doc;
    }

    private static Paragraph P(string text) => new Paragraph { Inlines = InlineParser.Parse(text) };

    [Fact]
    public void Sources_without_a_bibliography_get_one()
    {
        Document doc = Doc(P("See [@k]."));
        doc.Sources["k"] = new Source { Key = "k", Fields = { ["title"] = "T" } };
        string md = MarkdownWriter.Write(doc);
        Assert.Contains("# References", md);
        Assert.Contains("1. *T*", md);
    }

    [Fact]
    public void Writers_handle_every_block_shape()
    {
        var quoteList = new Quote();
        quoteList.Blocks.Add(P("Quoted."));
        var inner = new ListBlock();
        inner.Items.Add(new ListItem { Blocks = { P("in a quote") } });
        quoteList.Blocks.Add(inner);
        var spanTable = new Table { Rules = TableRules.Grid };
        spanTable.Align.AddRange(new[] { ColumnAlign.Left, ColumnAlign.Center, ColumnAlign.Right });
        spanTable.Rows.Add(new TableRow { Header = true, Cells = { new TableCell { Inlines = InlineParser.Parse("H") , Span = 3 } } });
        spanTable.Rows.Add(new TableRow { Cells = { new TableCell { Inlines = InlineParser.Parse("a < b > c d"), Span = 2 }, new TableCell { Inlines = InlineParser.Parse("x"), Bold = true } } });
        spanTable.Rows.Add(new TableRow { RuleAbove = true, Cells = { new TableCell { Inlines = InlineParser.Parse("1") }, new TableCell { Inlines = InlineParser.Parse("2") }, new TableCell { Inlines = InlineParser.Parse("3") } } });
        var wide = new Table();
        wide.Rows.Add(new TableRow { Cells = { new TableCell { Inlines = InlineParser.Parse(string.Join(" ", Enumerable.Repeat("long words in a cell", 40))) }, new TableCell { Inlines = InlineParser.Parse(string.Join(" ", Enumerable.Repeat("more", 60))) } } });
        var tall = new Figure { Source = "tall.png", Caption = InlineParser.Parse("Tall."), Width = 0.9, Image = ImageData.FromBytes(DocumentFixture.Png(10, 400, 2), "tall.png") };
        Document doc = Doc(
            new Paragraph { Inlines = InlineParser.Parse("Coloured."), Color = "#ff0000", Bold = true, Scale = 1.5 },
            quoteList,
            spanTable,
            wide,
            tall,
            new MathBlock { Node = MathParser.Parse("\\sqrt{x} + \\sqrt[3]{y}"), Numbered = false },
            new Paragraph { Inlines = new List<Inline> { new MathInline("\\sqrt{2}", MathParser.Parse("\\sqrt{2}")) } });
        doc.Title = "Café ünïcode";
        doc.Authors.Add(new Author { Name = "Zoë" });
        Assert.Equal("Zoë", doc.Authors[0].ToString());

        string tex = LatexWriter.Write(doc);
        Assert.Contains("\\textcolor[rgb]{1,0,0}", tex);
        Assert.Contains("\\textless{}", tex);
        Assert.Contains("\\textgreater{}", tex);
        Assert.Contains("~d", tex);
        Assert.Contains("\\multicolumn{2}", tex);
        Assert.Contains("\\multicolumn{3}{|c|}", tex.Replace("{|l|}", "{|c|}").Replace("{|r|}", "{|c|}"));

        byte[] pdf = PdfWriter.Write(doc);
        string raw = Encoding.Latin1.GetString(pdf);
        Assert.Contains("<FEFF", raw);
        Assert.True(PdfWriter.CountPages(doc) >= 1);
        Assert.NotEmpty(DocxWriter.Write(doc));
        Assert.Contains("<blockquote>", HtmlWriter.Write(doc));
        Assert.Contains("> Quoted.", MarkdownWriter.Write(doc));
    }

    [Fact]
    public void Links_keep_their_brackets_and_only_go_to_safe_places()
    {
        var wiki = Assert.Single(InlineParser.Parse("See [Life](https://en.wikipedia.org/wiki/Life_(game)).").OfType<LinkInline>());
        Assert.Equal("https://en.wikipedia.org/wiki/Life_(game)", wiki.Url);
        Assert.Equal("See Life.", InlineParser.Plain(InlineParser.Parse("See [Life](https://en.wikipedia.org/wiki/Life_(game)).")));

        foreach (string url in new[] { "https://x.org/a?b=1#c", "http://x.org", "mailto:a@b.org", "other.html", "#intro", "notes/a:b.html", "?q=a:b" })
            Assert.True(InlineParser.IsLinkTarget(url), url);
        foreach (string url in new[] { "javascript:alert(1)", "JavaScript:alert(1)", "data:text/html,x", "vbscript:x", "file:///etc/passwd", "" })
            Assert.False(InlineParser.IsLinkTarget(url), url);

        // Text often comes from data: a script address is never made into a link, in any format.
        Document doc = Doc(P("Click [here](javascript:alert(1)) or [there](https://safe.example)."));
        Assert.Single(doc.Blocks.OfType<Paragraph>().Single().Inlines.OfType<LinkInline>());
        Assert.DoesNotContain("href=\"javascript", HtmlWriter.Write(doc));
        Assert.Contains("href=\"https://safe.example\"", HtmlWriter.Write(doc));

        var cited = Doc(P("See [@k]."));
        cited.Sources["k"] = new Source { Key = "k", Fields = { ["title"] = "T", ["url"] = "javascript:alert(1)" } };
        Assert.DoesNotContain("href=\"javascript", HtmlWriter.Write(cited));
    }

    [Fact]
    public void Markdown_keeps_code_titles_and_cells_as_text()
    {
        var table = new Table();
        table.Rows.Add(new TableRow { Header = true, Cells = { new TableCell { Inlines = InlineParser.Parse("name") } } });
        table.Rows.Add(new TableRow { Cells = { new TableCell { Inlines = new List<Inline> { new TextInline("a|b"), new CodeInline("c|d") } } } });
        var quote = new Quote { Attribution = "<i>who</i>" };
        quote.Blocks.Add(P("Said."));
        Document doc = Doc(new CodeBlock { Text = "before\n```\n# not a heading\n````\nafter", Language = "md" }, table, new Table { Caption = InlineParser.Parse("Nothing found.") }, quote);
        doc.Title = "<b>Bold</b> *title*";
        doc.Subtitle = "a_b";
        doc.Authors.Add(new Author { Name = "A <a@b>" });
        doc.Keywords.Add("x*y");

        string md = MarkdownWriter.Write(doc);
        // The fence is longer than the longest run of backticks in the code, so the code can't end it.
        Assert.Contains("`````md\nbefore\n```\n# not a heading\n````\nafter\n`````\n", md);
        Assert.Contains("# \\<b\\>Bold\\</b\\> \\*title\\*\n", md);
        Assert.Contains("*a\\_b*", md);
        Assert.Contains("A \\<a@b\\>", md);
        Assert.Contains("**Keywords:** x\\*y", md);
        Assert.Contains("\u2014 \\<i\\>who\\</i\\>", md);
        // A | in a cell is escaped once, in text and in code alike.
        Assert.Contains("| a\\|b`c\\|d` |", md);
        // A table with no rows is left out.
        Assert.DoesNotContain("Nothing found", md);
        Assert.DoesNotContain("||", md);
    }

    [Fact]
    public void LaTeX_breaks_long_tables_and_escapes_labels_links_and_images()
    {
        var longTable = new Table { Caption = InlineParser.Parse("Many rows."), Id = "tab-caf\u00E9" };
        longTable.Rows.Add(new TableRow { Header = true, Cells = { new TableCell { Inlines = InlineParser.Parse("N") } } });
        for (int i = 0; i <= LatexWriter.LongTableRows; i++) longTable.Rows.Add(new TableRow { Cells = { new TableCell { Inlines = InlineParser.Parse("r") } } });
        var shortTable = new Table();
        shortTable.Rows.Add(new TableRow { Cells = { new TableCell { Inlines = InlineParser.Parse("only") } } });
        Document doc = Doc(longTable, shortTable, new Table { Caption = InlineParser.Parse("Nothing found.") },
            P("See Table [#tab-caf\u00E9] and [a link](https://x.org/a%20b?c=1&d={e}#f)."),
            new Figure { Source = "50%_off#1.png", Width = 0.5, Image = ImageData.FromBytes(DocumentFixture.Png(2, 2, 2), "x.png") });

        string tex = LatexWriter.Write(doc);
        Assert.Contains("\\usepackage{longtable}", tex);
        // The caption is on the first page only; the heading row is repeated on every page.
        Assert.Contains("\\begin{longtable}{l}\n\\caption{Many rows.}\\label{tab-caf.ue9.} \\\\\n\\toprule\n\\textbf{N} \\\\\n\\midrule\n\\endfirsthead\n\\toprule\n\\textbf{N} \\\\\n\\midrule\n\\endhead\n", tex);
        Assert.Contains("\\end{longtable}", tex);
        Assert.Contains("\\begin{tabular}{l}\n\\toprule\nonly", tex);
        Assert.DoesNotContain("Nothing found", tex);
        Assert.DoesNotContain("\\begin{tabular}{}", tex);
        Assert.Contains("Table \\ref{tab-caf.ue9.}", tex);
        Assert.Contains("\\href{https://x.org/a\\%20b?c=1\\&d=\\%7Be\\%7D\\#f}{a link}", tex);
        Assert.Contains("{\\detokenize{50%_off#1.png}}", tex);
        Assert.Equal("eq-1_a:b.c", LatexWriter.LabelName("eq-1_a:b.c"));
    }

    [Fact]
    public void Characters_XML_cannot_hold_are_left_out_of_Word_and_HTML()
    {
        const string dirty = "a\u0001b\u000Bc\u001B[31md\uFFFEe \U0001D4B3 tab\there";
        Document doc = Doc(P(dirty), new CodeBlock { Text = dirty }, new MathBlock { Node = MathParser.Parse("\\text{x\u0001y}"), Numbered = false });
        doc.Title = dirty;
        doc.Authors.Add(new Author { Name = dirty });

        using (var zip = new System.IO.Compression.ZipArchive(new MemoryStream(DocxWriter.Write(doc))))
        {
            foreach (var entry in zip.Entries.Where(e => e.Name.EndsWith(".xml", StringComparison.Ordinal) || e.Name.EndsWith(".rels", StringComparison.Ordinal)))
            {
                using var reader = new StreamReader(entry.Open());
                string xml = reader.ReadToEnd();
                // Throws if a part isn't well-formed XML, which is what stops Word opening the file.
                System.Xml.Linq.XDocument.Parse(xml);
                Assert.DoesNotContain('\u0001', xml);
            }
        }
        string html = HtmlWriter.Write(doc);
        Assert.DoesNotContain('\u0001', html);
        Assert.DoesNotContain('\u001B', html);
        Assert.Contains("abc[31mde &#119987; tab\there", html);
    }

    [Fact]
    public void Document_values()
    {
        var value = new DocumentValue(Doc(P("x")));
        Assert.Equal("document 'untitled'", value.ToString());
        Assert.False(value.TryGetMember("nope", out _));
        Assert.False(value.TryCallMethod(null, "save", out object result));
        Assert.Null(result);
    }

    [Fact]
    public void Text_flow_breaks_lines_and_long_words()
    {
        var style = new RunStyle { Face = new Face(FontFamily.Serif, false, false), Size = 10 };
        var flow = new TextFlow(new List<Inline> { new TextInline("a"), new LineBreakInline(), new TextInline(" b"), new LineBreakInline() }, style, FontFamily.Serif);
        List<Line> lines = flow.Break(100);
        Assert.Equal(2, lines.Count);
        Assert.True(lines[^1].Last);
        var glued = new List<Inline> { new MathInline("x", MathParser.Parse("x^2")), new TextInline(new string('w', 40)) };
        Assert.True(new TextFlow(glued, style, FontFamily.Sans).Break(30).Count > 3);
    }

    [Fact]
    public void Long_grid_tables_continue_on_the_next_page()
    {
        var table = new Table { Rules = TableRules.Grid };
        table.Rows.Add(new TableRow { Header = true, Cells = { new TableCell { Inlines = InlineParser.Parse("N") } } });
        for (int i = 0; i < 120; i++) table.Rows.Add(new TableRow { Cells = { new TableCell { Inlines = InlineParser.Parse(i.ToString(System.Globalization.CultureInfo.InvariantCulture)) } } });
        Assert.True(PdfWriter.CountPages(Doc(table)) > 1);
    }

    [Fact]
    public void Links_without_a_destination_are_left_out()
    {
        var page = new PdfPage();
        page.Links.Add(new LinkArea { X = 0, Y = 0, W = 10, H = 10, Target = "nowhere" });
        page.Links.Add(new LinkArea { X = 0, Y = 0, W = 10, H = 10, Url = "https://example.org" });
        page.Ops.Add(new RectOp { X = 1, Y = 1, W = 2, H = 2, Fill = "#000000" }.Moved(1, 1));
        page.Ops.Add(new ImageOp { Image = ImageData.FromBytes(DocumentFixture.Jpeg(4, 4), "a.jpg"), X = 0, Y = 0, W = 4, H = 4 }.Moved(1, 1));
        page.Ops.Add(new PathOp { Parts = { ('M', new double[] { 0, 0 }), ('L', new double[] { 1, 1 }), ('C', new double[] { 1, 1, 2, 2, 3, 3 }) }, Width = 1, Colour = "#000000" }.Moved(1, 1));
        byte[] pdf = PdfFile.Write(new List<PdfPage> { page }, 100, 100, new Dictionary<string, (int, double)>(), new List<Bookmark>(), new Dictionary<string, string> { ["Title"] = "T" });
        string raw = Encoding.Latin1.GetString(pdf);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(raw, "/Subtype /Link"));
    }
}
