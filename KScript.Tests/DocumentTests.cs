using System.Globalization;
using System.IO.Compression;
using System.Text;

using KScript.Documents;
using KScript.Documents.Export;
using KScript.Documents.Maths;
using KScript.Documents.Pdf;
using KScript.Documents.Text;
using KScript.Runtime;

namespace KScript.Tests;

/// <summary>Runs KScript.Document scripts in a temporary folder and reads what they write.</summary>
public abstract class DocumentFixture : IDisposable
{
    protected readonly string Folder = Path.Combine(Path.GetTempPath(), "kscript-doc-" + Guid.NewGuid().ToString("N"));

    protected DocumentFixture()
    {
        Directory.CreateDirectory(Folder);
    }

    public void Dispose()
    {
        try { Directory.Delete(Folder, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    protected string PathOf(string name) => Path.Combine(Folder, name);

    protected void FileOf(string name, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathOf(name)));
        File.WriteAllText(PathOf(name), text);
    }

    protected void BytesOf(string name, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathOf(name)));
        File.WriteAllBytes(PathOf(name), bytes);
    }

    protected string Read(string name) => File.ReadAllText(PathOf(name));

    /// <summary>Runs .kscript code saved as script.kscript in the folder.</summary>
    protected string Run(string code, Action<KScriptHost> setup = null)
    {
        var output = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        var host = KScriptHost.ForText(output);
        setup?.Invoke(host);
        // Document is a statement of KScript.Document, so a script names the package once, in an import.
        if (!code.Contains("import KScript.Document", StringComparison.Ordinal)) code = "import KScript.Document\n" + code;
        new Interpreter(host).RunText(code, PathOf("script.kscript"));
        return output.ToString();
    }

    protected KScriptException Fails(string code, Action<KScriptHost> setup = null) => Assert.Throws<KScriptException>(() => Run(code, setup));

    /// <summary>A document's Word XML part.</summary>
    protected string Docx(string name, string part = "word/document.xml")
    {
        using var zip = ZipFile.OpenRead(PathOf(name));
        using var reader = new StreamReader(zip.GetEntry(part).Open());
        return reader.ReadToEnd();
    }

    /// <summary>The text of a PDF's page streams, inflated.</summary>
    protected static string PdfText(byte[] pdf)
    {
        string raw = Encoding.Latin1.GetString(pdf);
        var sb = new StringBuilder();
        int at = 0;
        while ((at = raw.IndexOf(">>\nstream\n", at, StringComparison.Ordinal)) >= 0)
        {
            at += 3;
            int start = at + 7;
            int end = raw.IndexOf("\nendstream", start, StringComparison.Ordinal);
            byte[] data = pdf.AsSpan(start, end - start).ToArray();
            try
            {
                using var z = new ZLibStream(new MemoryStream(data), CompressionMode.Decompress);
                using var r = new StreamReader(z, Encoding.Latin1);
                sb.Append(r.ReadToEnd()).Append('\n');
            }
            catch (InvalidDataException)
            {
                // Not a Flate stream (a JPEG).
            }
            at = end;
        }
        return sb.ToString();
    }

    /// <summary>A small PNG: <paramref name="colourType"/> 0 grey, 2 RGB, 3 palette, 4 grey+alpha, 6 RGBA.</summary>
    internal static byte[] Png(int width, int height, int colourType, int filter = 0, bool interlaced = false, bool transparentPalette = false, int bitDepth = 8)
    {
        int channels = colourType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, _ => 4 };
        int rowBytes = (width * channels * bitDepth + 7) / 8;
        var raw = new MemoryStream();
        for (int y = 0; y < height; y++)
        {
            raw.WriteByte((byte)((filter + y) % 5));
            for (int x = 0; x < rowBytes; x++) raw.WriteByte((byte)(x * 37 + y * 11));
        }
        byte[] Chunk(string type, byte[] data)
        {
            var ms = new MemoryStream();
            byte[] len = BitConverter.GetBytes(data.Length);
            Array.Reverse(len);
            ms.Write(len);
            byte[] body = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
            ms.Write(body);
            byte[] crc = BitConverter.GetBytes(Crc(body));
            Array.Reverse(crc);
            ms.Write(crc);
            return ms.ToArray();
        }
        var png = new MemoryStream();
        png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var ihdr = new byte[13];
        ihdr[0] = (byte)(width >> 24); ihdr[1] = (byte)(width >> 16); ihdr[2] = (byte)(width >> 8); ihdr[3] = (byte)width;
        ihdr[4] = (byte)(height >> 24); ihdr[5] = (byte)(height >> 16); ihdr[6] = (byte)(height >> 8); ihdr[7] = (byte)height;
        ihdr[8] = (byte)bitDepth; ihdr[9] = (byte)colourType; ihdr[12] = (byte)(interlaced ? 1 : 0);
        png.Write(Chunk("IHDR", ihdr));
        if (colourType == 3)
        {
            png.Write(Chunk("PLTE", Enumerable.Range(0, 256 * 3).Select(i => (byte)i).ToArray()));
            if (transparentPalette) png.Write(Chunk("tRNS", new byte[] { 0, 128, 255 }));
        }
        var z = new MemoryStream();
        using (var deflate = new ZLibStream(z, CompressionLevel.Optimal, leaveOpen: true)) deflate.Write(raw.ToArray());
        byte[] idat = z.ToArray();
        // Split the data over two IDAT chunks, as encoders may.
        png.Write(Chunk("IDAT", idat.Take(idat.Length / 2).ToArray()));
        png.Write(Chunk("IDAT", idat.Skip(idat.Length / 2).ToArray()));
        png.Write(Chunk("IEND", Array.Empty<byte>()));
        return png.ToArray();
    }

    private static uint Crc(byte[] data)
    {
        uint c = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            c ^= b;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        }
        return c ^ 0xFFFFFFFF;
    }

    /// <summary>The smallest JPEG header the size reader needs: SOI, an APP0 segment and SOF0.</summary>
    internal static byte[] Jpeg(int width, int height, int components = 3)
    {
        var ms = new MemoryStream();
        ms.Write(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00 });
        ms.Write(new byte[] { 0xFF, 0xC0, 0x00, 0x11, 0x08, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, (byte)components });
        ms.Write(new byte[9]);
        ms.Write(new byte[] { 0xFF, 0xD9 });
        return ms.ToArray();
    }
}

public sealed class DocumentScriptTests : DocumentFixture
{
    private const string Bib = @"
% A comment line
@string{sa = ""Scientific American""}
@article{gardner1970,
  author  = {Martin Gardner},
  title   = {The Fantastic Combinations of {John Conway's} New Solitaire Game ``Life''},
  journal = sa,
  volume  = {223}, number = {4}, pages = {120--123},
  year    = 1970,
  doi     = {10.1038/scientificamerican1070-120}
}
@book{berlekamp1982,
  author    = ""Berlekamp, Elwyn R. and Conway, John H. and Guy, Richard K."",
  title     = {Winning Ways},
  publisher = {Academic Press}, year = {1982}, url = {https://example.org/ww}
}
@misc{erdos, author = {Paul Erd{\""o}s and Andr\'{e} Weil}, title = {Caf\'e \& Co}, year = {1950}, note = {\v{S}koda}}
@comment{ignored}
";

    [Fact]
    public void Paper_in_every_format()
    {
        BytesOf("fig.png", Png(40, 20, 6));
        BytesOf("photo.jpg", Jpeg(64, 48));
        FileOf("refs.bib", Bib);
        FileOf("ch/intro.md", "## From a file {#from-file}\n\nInline $x^2$ and display:\n\n$$ y = mx + c $$ {#eq-line}\n\n![A photo](photo.jpg){#fig-photo}\n");
        string output = Run(@"
import KScript.Document
rows = [{ name: 'Ada', score: 9.5, team: { city: 'London' } }, { name: 'Alan', score: 12, team: { city: 'Manchester' } }]
Document(template: paper, title: ""Kitchen Sink"", subtitle: ""All of it"", author: ""Ada Lovelace; Alan Turing"", date: ""1 May 2026"", keywords: ""one, two"", header: ""[title] | [author] | [date]"", footer: "" | Page [page] of [pages] | "", into: doc, to: ""out/sink.pdf, out/sink.docx"") {
  Author(affiliation: ""Analytical Engines"", email: ""ada@example.org"") ""Ada King""
  Abstract ""First paragraph of the abstract.

  Second paragraph with \( \alpha \).""
  Contents(depth: 2)
  Section(title: ""Intro with \( x \)"", id: intro) {
    Paragraph(id: p1) ""**Bold**, *italic*, __strong__, _em_, `code`, [a link](https://example.org), \( \frac{a}{b} \), [@gardner1970], [@berlekamp1982; @erdos], see [#methods] and [#eq-one] and [#tab-data] and [#fig-one] and [#p1].^[A *note*.] -- and --- dashes.""
    Paragraph(align: center) ""Centred.""
    Paragraph(align: right) ""Right.""
    Paragraph(align: left) ""Left.""
    Paragraph(lines: yes) ""Line one
Line two""
    Section(title: Methods, id: methods, numbered: no) {
      List(style: number, start: 3) {
        Item ""Three""
        List(style: letter) {
          Item ""a""
          List(style: roman) {
            Item ""i""
          }
        }
        Item ""Four""
      }
      List {
        Item ""Bullet with **bold**""
      }
      Section(title: Deeper) {
        Section(title: Deeper still) {
          Paragraph ""Level four.""
        }
      }
    }
  }
  Section(title: Results, new_page: yes) {
    Math(id: eq-one) ""E = mc^2""
    Math(numbered: no) ""\int_0^1 x \, dx = \tfrac{1}{2}""
    Code(language: python) ""def f(x):
    return x * 2  # { not interpolated }""
    Quote(by: ""Someone"") ""A quotation.

Two paragraphs.""
    Table(caption: ""Data from maps."", id: tab-data, rows: $rows, columns: { Name: 'name', Score: 'score', City: 'team.city', Missing: 'nope' })
    Table(rows: [[1, 2], [3, 4]], header: false, rules: grid, widths: [2, 1], size: 0.9)
    Table(rows: $rows, columns: ['name', 'score'], rules: none, align: ['left', 'center'])
    Table(rows: [['a', 'b'], ['c', 'd']])
    Table(rows: [5, 6])
    Table(caption: ""Hand made."", align: ['left', 'center', 'right']) {
      Tr(header: yes) ""A | B | C""
      Tr ""1 \| one | 2 | 3""
      Tr ""wide cell that keeps going and going and going and going and going and going | x | y""
    }
    Figure(path: ""fig.png"", caption: ""A figure."", id: fig-one, width: '50%')
    Figure(path: ""photo.jpg"", width: 0.3)
    Markdown(path: ""ch/intro.md"")
    Markdown ""
      ### Inline markdown

      Text with \( y \).
    ""
    VSpace(height: ""1cm"")
    Rule
    PageBreak
    Paragraph ""After the break.""
  }
  Bibliography(path: ""refs.bib"", title: ""Sources"") {
    Source(id: extra, type: book, author: ""Jane Doe"", title: ""Uncited"", year: 2000)
  }
}
echo ""{ $doc.title } | { $doc.sections } | { $doc->pages() } | { $doc->words() } | { @type($doc) } | { $doc }""
$doc->save('out/sink.tex')
$doc->save('out/sink.md')
$doc->save('out/sink.markdown')
$doc->save('out/sink.html')
$doc->save('out/sink.htm')
echo $doc->latex()->length() > 0
echo $doc->markdown()->length() > 0
echo $doc->html()->length() > 0
");
        Assert.StartsWith("Kitchen Sink | 7 | ", output);
        Assert.Contains("| document | document 'Kitchen Sink'", output);

        byte[] pdf = File.ReadAllBytes(PathOf("out/sink.pdf"));
        Assert.StartsWith("%PDF-1.7", Encoding.ASCII.GetString(pdf, 0, 8));
        Assert.EndsWith("%%EOF\n", Encoding.ASCII.GetString(pdf));
        string ops = PdfText(pdf);
        Assert.Contains("(Kitchen Sink) Tj", ops);
        Assert.Contains("/Image", Encoding.Latin1.GetString(pdf));
        Assert.Contains("/DCTDecode", Encoding.Latin1.GetString(pdf));
        Assert.Contains("/SMask", Encoding.Latin1.GetString(pdf));
        Assert.Contains("/Outlines", Encoding.Latin1.GetString(pdf));

        string tex = Read("out/sink.tex");
        Assert.Contains("\\title{Kitchen Sink", tex);
        Assert.Contains("\\cite{gardner1970}", tex);
        Assert.Contains("\\label{eq-one}", tex);
        Assert.Contains("\\begin{verbatim}", tex);
        Assert.Contains("\\includegraphics", tex);
        Assert.Contains("\\bibitem", tex);

        string md = Read("out/sink.md");
        Assert.Contains("# Kitchen Sink", md);
        Assert.Contains("$$", md);
        Assert.Equal(md, Read("out/sink.markdown"));

        string html = Read("out/sink.html");
        Assert.Contains("<math", html);
        Assert.Contains("data:image/png;base64,", html);
        Assert.Contains("data:image/jpeg;base64,", html);

        string word = Docx("out/sink.docx");
        Assert.Contains("<m:oMath>", word);
        Assert.Contains("TOC \\o", word);
        Assert.Contains("w:footnoteReference", word);
        Assert.Contains("<a:blip", word);
        Assert.Contains("Page ", Docx("out/sink.docx", "word/footer1.xml"));
        Assert.Contains("PAGE", Docx("out/sink.docx", "word/footer1.xml"));
        Assert.Contains("Kitchen Sink", Docx("out/sink.docx", "word/header1.xml"));
    }

    [Fact]
    public void Report_letter_and_invoice_templates()
    {
        Run(@"
Document(template: report, title: ""Report"", author: ""Team"", citations: author-year, to: ""report.pdf, report.docx, report.tex, report.md, report.html"") {
  Contents
  Section(title: One) {
    Paragraph ""See [@a] and [@a; @b].""
  }
  Section(title: Two) {
    Paragraph ""Two.""
  }
  Bibliography {
    Source(id: a, author: ""Zed Last and Amy First"", title: ""Alpha"", year: 2001, journal: ""J"")
    Source(id: b, author: ""Bee"", title: ""Beta"", year: 1999, publisher: ""P"", url: ""https://b.example"")
  }
}
Document(template: letter, to: ""letter.pdf, letter.docx"") {
  Letter(from: ""Me\n1 Street"", to: ""You"", subject: ""Hello"", opening: ""Dear you,"", closing: ""Yours,"", signature: ""Me"") {
    Paragraph ""Body.""
  }
  Letter {
    Paragraph ""Only the date.""
  }
}
Document(template: invoice, to: ""invoice.pdf, invoice.docx, invoice.html"") {
  Invoice(number: 7, title: ""Bill"", from: ""Us"", to: ""Them"", due: ""Soon"", tax: 10, tax_name: ""Sales tax"", paid: 50, notes: ""Thanks"", currency: USD, items: [{ description: 'A', qty: 2, unit_price: 10 }, { item: 'B', amount: 5 }, { name: 'C', rate: 1.5, quantity: 3 }, { description: 'Free' }])
  Invoice(items: [{ description: 'Yen', price: 1000 }], currency: JPY)
  Invoice(items: [{ description: 'Other', price: -3 }], currency: XYZ)
  Invoice(items: [{ description: 'Odd', price: 3 }], currency: ""Ð"")
}
");
        Assert.Contains("(Last and First, 2001)", Read("report.md"));
        Assert.Contains("\\citep{a}", Read("report.tex"));
        Assert.Contains("natbib", Read("report.tex"));
        string invoice = Docx("invoice.docx");
        Assert.Contains("$29.50", invoice);
        Assert.Contains("−$17.55", invoice);
        Assert.Contains("Sales tax at 10%", invoice);
        Assert.Contains("Balance due", invoice);
        Assert.Contains("¥1,000", invoice);
        Assert.Contains("−XYZ\u00A03.00", invoice);
        Assert.Contains("Ð3.00", invoice);
        Assert.Contains("Page ", Docx("letter.docx", "word/footer1.xml"));
        Assert.Contains("Hello", Docx("letter.docx"));
    }

    [Fact]
    public void Style_options_change_the_page()
    {
        Run(@"
Document(paper: letter, landscape: yes, margin: ""1in 2cm 30mm 40pt"", font: sans, heading_font: mono, size: 12, line_height: 1.5, align: left, accent: navy, numbered: no, title_page: yes, chapters: yes, citations: numeric, header: ""Left"", footer: """", rules: grid, language: ""en-GB"", title: ""Styled"", to: ""styled.pdf, styled.docx, styled.html, styled.tex"") {
  Abstract ""On the title page.""
  Section(title: A) {
    Paragraph ""Text.""
  }
  Section(title: B) {
    Paragraph ""More.""
  }
}
Document(paper: ""15cm x 20cm"", margin: ""1cm 2cm"", accent: ""#336699"", font: mono, to: ""custom.pdf, custom.tex"") {
  Paragraph ""Custom paper.""
}
Document(paper: a5, margin: ""1cm 2cm 3cm"", to: ""a5.pdf"") {
  Paragraph ""A5.""
}
Document(paper: a3, to: ""a3.pdf"") {
  Paragraph ""A3.""
}
Document(paper: b5, to: ""b5.pdf"") {
  Paragraph ""B5.""
}
Document(paper: legal, to: ""legal.pdf"") {
  Paragraph ""Legal.""
}
");
        Assert.Contains("en-GB", Read("styled.html"));
        Assert.Contains("w:orient=\"landscape\"", Docx("styled.docx"));
        Assert.Contains("helvet", Read("styled.tex"));
        Assert.Contains("paperwidth=425.", Read("custom.tex"));
        Assert.True(File.Exists(PathOf("legal.pdf")));
    }

    [Fact]
    public void To_takes_a_list_of_files()
    {
        Run(@"
import KScript.Document
name = 'list'
Document(title: ""Bare"", to: [bare.pdf, bare.md]) {
  Paragraph ""Bare names.""
}
Document(title: ""Quoted"", to: [""quoted.tex"", 'quoted.html']) {
  Paragraph ""Quoted names.""
}
Document(title: ""Values"", to: [""{ $name }.docx""]) {
  Paragraph ""A filled-in name.""
}
");
        foreach (string file in new[] { "bare.pdf", "bare.md", "quoted.tex", "quoted.html", "list.docx" })
            Assert.True(File.Exists(PathOf(file)), file);
        Assert.Equal(new[] { "a.pdf", "b.docx", "c.tex" }, DocumentElements.OutputPaths("[a.pdf, \"b.docx\", 'c.tex']"));
        Assert.Equal(new[] { "a.pdf", "b.docx" }, DocumentElements.OutputPaths("a.pdf, b.docx"));
        Assert.Empty(DocumentElements.OutputPaths("[\"\", ]"));
    }

    [Theory]
    [InlineData("import KScript.Document\n\nDocument(title: \"x\") {\n}", "needs to:")]
    [InlineData("import KScript.Document\n\nDocument(to: \"a.pdf\") {\n  Document(to: \"b.pdf\") {\n  }\n}", "can't be inside another")]
    [InlineData("import KScript.Document\n\nDocument(to: \"a.txt\") {\n}", "use .pdf")]
    [InlineData("import KScript.Document\n\nDocument(to: \"a.pdf\", template: poster) {\n}", "template")]
    [InlineData("import KScript.Document\n\nDocument(to: \"a.pdf\", paper: huge) {\n}", "paper")]
    [InlineData("import KScript.Document\n\nDocument(to: \"a.pdf\", margin: \"\") {\n}", "margin")]
    [InlineData("import KScript.Document\n\nDocument(to: \"a.pdf\", size: big) {\n}", "size: 'big' isn't a length")]
    [InlineData("import KScript.Document\n\nDocument(to: \"a.pdf\", accent: \"#zz\") {\n}", "accent: '#zz' isn't a colour")]
    [InlineData("import KScript.Document\n\nDocument(to: \"a.pdf\") {\n  Paragraph \"[#nowhere]\"\n}", "nowhere")]
    [InlineData("import KScript.Document\n\nDocument(to: \"a.pdf\") {\n  Paragraph \"[@nobody]\"\n}", "nobody")]
    [InlineData("import KScript.Document\n\nDocument(to: \"a.pdf\") {\n  Paragraph(id: x) \"a\"\n  Paragraph(id: x) \"b\"\n}", "Two parts of the document have id: x")]
    [InlineData("import KScript.Document\n\nDocument(to: \"a.pdf\") {\n  Math \"\\frac{1}\"\n}", "In the maths")]
    [InlineData("import KScript.Document\n\nDocument(to: \"a.pdf\") {\n  Paragraph \"\\( \\nosuch \\)\"\n}", "nosuch")]
    [InlineData("import KScript.Document\n\nDocument(to: \"a.pdf\") {\n  Figure(path: \"missing.png\")\n}", "missing.png")]
    [InlineData("import KScript.Document\n\nDocument(to: \"a.pdf\") {\n  Figure(path: \"text.png\")\n}", "PNG or JPEG")]
    [InlineData("import KScript.Document\n\nDocument(to: \"a.pdf\") {\n  Figure(path: \"x.png\", width: 2)\n}", "width")]
    [InlineData("import KScript.Document\n\nDocument(to: \"a.pdf\") {\n  Markdown(path: \"none.md\")\n}", "no Markdown file")]
    [InlineData("import KScript.Document\n\nDocument(to: \"a.pdf\") {\n  Bibliography(path: \"none.bib\")\n}", "no BibTeX file")]
    [InlineData("s = 'stars'\nimport KScript.Document\n\nDocument(to: \"a.pdf\") {\n  List(style: \"{ $s }\") {\n  }\n}", "should be bullet")]
    [InlineData("import KScript.Document\n\nDocument(to: \"a.pdf\") {\n  List {\n    List {\n    }\n  }\n}", "after the Item")]
    [InlineData("import KScript.Document\n\nDocument(to: \"a.pdf\") {\n  Table(align: ['left', 'x'])\n}", "isn't a column alignment")]
    [InlineData("import KScript.Document\n\nDocument(to: \"a.pdf\") {\n  Table(rows: 5)\n}", "list of rows")]
    [InlineData("import KScript.Document\n\nDocument(to: \"a.pdf\") {\n  Table(rows: [], columns: 5)\n}", "columns should be a list of keys")]
    [InlineData("import KScript.Document\n\nDocument(to: \"a.pdf\") {\n  Table(rules: double)\n}", "rules")]
    [InlineData("import KScript.Document\n\nDocument(to: \"a.pdf\") {\n  VSpace(height: tall)\n}", "tall")]
    [InlineData("import KScript.Document\n\nDocument(to: \"a.pdf\") {\n  Invoice(items: 5)\n}", "items:")]
    [InlineData("import KScript.Document\n\nDocument(to: \"a.pdf\") {\n  Invoice(items: [5])\n}", "Each of items")]
    [InlineData("import KScript.Document\n\nDocument(to: \"a.pdf\") {\n  Paragraph(align: middle) \"a\"\n}", "align")]
    public void Mistakes_are_explained(string code, string message)
    {
        BytesOf("x.png", Png(2, 2, 2));
        FileOf("text.png", "not an image");
        KScriptException e = Fails(code);
        Assert.Contains(message, e.Message);
    }

    [Fact]
    public void Read_only_hosts_cannot_save()
    {
        KScriptException e = Fails("import KScript.Document\n\nDocument(to: \"a.pdf\") {\n}", host => host.CanWriteFiles = false);
        Assert.Contains("read-only", e.Message);
    }

    [Fact]
    public void Elements_outside_a_document_are_refused()
    {
        Assert.Contains("only works inside", Fails("import KScript.Document\nfn f() {\n  Paragraph \"x\"\n}\n@f()").Message);
    }

    [Fact]
    public void Commands_escape_latex_and_list_templates()
    {
        string output = Run("import KScript.Document\necho @latex_escape('50% of $10 & {x} #1 a_b ~ ^ \\ end')\necho @document_templates()->keys()->join(',')");
        Assert.Equal("50\\% of \\$10 \\& \\{x\\} \\#1 a\\_b \\textasciitilde{} \\textasciicircum{} \\textbackslash{} end\nplain,paper,report,letter,invoice\n", output);
    }

    [Fact]
    public void Contents_follow_page_numbers_and_long_content_flows_over_pages()
    {
        var sb = new StringBuilder("Document(title: \"Long\", footer: \"[page]/[pages]\", into: doc, to: \"long.pdf\") {\n  Contents(title: \"Index\")\n");
        for (int s = 0; s < 6; s++)
        {
            sb.Append("  Section(title: \"Part ").Append(s).Append("\") {\n");
            for (int p = 0; p < 8; p++) sb.Append("    Paragraph \"").Append(string.Concat(Enumerable.Repeat("Lorem ipsum dolor sit amet, consectetur adipiscing elit. ", 6))).Append("^[Note ").Append(p).Append(".]\"\n");
            sb.Append("  }\n");
        }
        sb.Append("  Code \"").Append(string.Join("\n", Enumerable.Range(0, 120).Select(i => "line " + i + " " + new string('x', i % 140)))).Append("\"\n");
        sb.Append("  Table(caption: \"Tall.\", rows: [").Append(string.Join(", ", Enumerable.Range(0, 90).Select(i => $"{{ n: {i}, word: 'word {i}' }}"))).Append("])\n");
        sb.Append("  Paragraph \"").Append("https://example.org/" + new string('a', 400)).Append("\"\n");
        sb.Append("}\necho $doc->pages()\n");
        int pages = int.Parse(Run(sb.ToString()).Trim(), CultureInfo.InvariantCulture);
        Assert.True(pages > 8, pages.ToString(CultureInfo.InvariantCulture));
        string ops = PdfText(File.ReadAllBytes(PathOf("long.pdf")));
        Assert.Contains($"(2/{pages})", ops);
    }
}
