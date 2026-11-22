using KScript.Documents.Export;
using KScript.Metadata;
using KScript.Runtime;

using A = KScript.Metadata.AttributeInfo;
using K = KScript.Metadata.AttributeKind;

namespace KScript.Documents
{
    /// <summary>
    /// KScript.Document: scientific and professional documents. A <c>KScript.Document { }</c> block holds sections,
    /// paragraphs, lists, tables, figures, LaTeX maths, citations and footnotes, and saves them as PDF, LaTeX, Word,
    /// Markdown or HTML, with templates for papers, reports, letters and invoices. PDFs are made in-process, so
    /// nothing else (TeX, Word, a browser) needs to be installed.
    /// </summary>
    public sealed class DocumentPackage : KScriptPackage
    {
        public override string Name => "KScript.Document";

        public override string Description => "Papers, reports, letters and invoices with LaTeX maths, saved as PDF, LaTeX, Word, Markdown or HTML without a TeX install.";

        internal static readonly string[] Inside = { "Document", "fn" };

        private static A Id => new A("id", K.Text, "A name for [#id] cross-references, such as id: intro.");

        private static ElementInfo Info(string name, string description, ContentKind content, string example, params A[] attributes) =>
            new ElementInfo(name, description, content, attributes)
            {
                Context = ElementContext.Script,
                MustBeInside = name == "Document" ? null : Inside,
                InsideAnyAncestor = true,
                LiteralText = content == ContentKind.Template ? TextLiterals.Maths : TextLiterals.None,
                Example = example,
            };

        protected override void Register(PackageBuilder p)
        {
            var style = new List<A>
            {
                new A("template", K.Enum, "The look: plain (the default), paper, report, letter or invoice.", false, "plain", "paper", "report", "letter", "invoice"),
                new A("title", K.Template, "The document's title."),
                new A("subtitle", K.Template, "A line under the title."),
                new A("author", K.Template, "The author, or several separated by ; (Author elements can add affiliations and emails)."),
                new A("date", K.Template, "The date shown under the title, such as { @now()->format('d MMMM yyyy') }."),
                new A("keywords", K.Template, "Keywords, separated by commas: shown under the abstract and saved in the file's properties."),
                new A("to", K.Template, "Where to save it, by extension: paper.pdf, paper.tex, paper.docx, paper.md or paper.html. Save several with a list: [paper.pdf, paper.docx].") { NameList = true },
                new A("into", K.DeclareVariable, "A variable that receives the document, for ->save(), ->pages() or ->markdown() later."),
                new A("paper", K.Template, "Paper size: a4 (the default), a3, a5, b5, letter, legal, or a size such as 15cm x 20cm."),
                new A("landscape", K.Bool, "yes turns the paper sideways."),
                new A("margin", K.Template, "Margins: one size (2.5cm), or top/right/bottom/left like CSS ('2cm 2.5cm')."),
                new A("font", K.Enum, "Body text: serif (Times), sans (Helvetica) or mono (Courier).", false, "serif", "sans", "mono"),
                new A("heading_font", K.Enum, "Headings: serif, sans or mono.", false, "serif", "sans", "mono"),
                new A("size", K.Template, "Body text size in points, such as 11."),
                new A("line_height", K.Template, "Baseline to baseline as a multiple of the text size, such as 1.4."),
                new A("align", K.Enum, "Body text: justify, left, center or right.", false, "justify", "left", "center", "right"),
                new A("accent", K.Template, "The colour of headings and the title page: #rrggbb or a name such as navy."),
                new A("numbered", K.Bool, "no leaves sections without numbers."),
                new A("title_page", K.Bool, "yes puts the title, authors, date and abstract on a page of their own."),
                new A("chapters", K.Bool, "yes starts each top-level section on a new page."),
                new A("citations", K.Enum, "numeric ([1]) or author-year ((Conway, 1970)).", false, "numeric", "author-year"),
                new A("header", K.Template, "The page header: 'left | centre | right', with [page], [pages], [title], [author] and [date]."),
                new A("footer", K.Template, "The page footer, like header. The default shows the page number."),
                new A("rules", K.Enum, "Table lines: booktabs (above, below and under the header), grid or none.", false, "booktabs", "grid", "none"),
                new A("language", K.Template, "The language, such as en or en-GB, for Word and HTML."),
            };
            p.Element(new ElementInfo("Document", "A document. Its content (Section, Paragraph, List, Table, Figure, Math...) is laid out by the template and saved where to: says, or kept in into:. Statements such as for, if and let work inside it.", ContentKind.Statements, style.ToArray())
            {
                Context = ElementContext.Script,
                DeclaresType = KType.Document,
                Example = "KScript.Document(template: paper, title: \"Gliders\", author: \"Ada Lovelace\", to: \"gliders.pdf\") {\n  Section(title: Introduction) {\n    Paragraph \"A glider moves one cell every \\( 4 \\) generations.\"\n  }\n}",
            }, DocumentElements.Document);

            p.Element(Info("Author", "An author, with an affiliation and email. Its text is the name.", ContentKind.Template,
                "Author(affiliation: \"University of Cambridge\", email: \"ada@example.org\") \"Ada Lovelace\"",
                new A("affiliation", K.Template, "Where they work or study."),
                new A("email", K.Template, "Their email address.")), DocumentElements.Author);
            p.Element(Info("Abstract", "The summary under the title. A blank line starts a new paragraph.", ContentKind.Template,
                "Abstract \"We measure how gliders move.\""), DocumentElements.Abstract);
            p.Element(Info("Section", "A section with a numbered heading. A Section inside a Section is a subsection.", ContentKind.Statements,
                "Section(title: Methods, id: methods) {\n  Paragraph \"We ran each pattern for 1000 generations.\"\n}",
                new A("title", K.Template, "The heading. It may hold **bold**, *italic* and \\( maths \\).", true), Id,
                new A("numbered", K.Bool, "no leaves this section without a number."),
                new A("new_page", K.Bool, "yes starts it on a new page.")), DocumentElements.Section);
            p.Element(Info("Paragraph", "A paragraph. Its text may hold **bold**, *italic*, `code`, [links](https://...), \\( maths \\), [@key] citations, [#id] cross-references and ^[footnotes]. A blank line starts a new paragraph.", ContentKind.Template,
                "Paragraph \"Gliders travel diagonally [@gardner1970], as Figure [#fig-glider] shows.^[Every four generations.]\"",
                Id, new A("align", K.Enum, "left, center, right or justify (the document's setting is the default).", false, "left", "center", "right", "justify"),
                new A("lines", K.Bool, "yes keeps the line breaks in the text, as in an address or a poem.")), DocumentElements.Paragraph);
            p.Element(Info("Markdown", "Markdown, from its text or a file: # headings become sections, with paragraphs, lists, tables, code, quotes, images and maths (\\[ \\] in scripts, also $ $ and $$ $$ in files).", ContentKind.Template,
                "Markdown(path: \"chapters/results.md\")",
                new A("path", K.Path, "A Markdown file, relative to the script.")), DocumentElements.Markdown);
            p.Element(Info("List", "A bulleted or numbered list of Item elements. A List right after an Item is nested in it.", ContentKind.Statements,
                "List(style: number) {\n  Item \"Measure\"\n  Item \"Compare\"\n}",
                new A("style", K.Enum, "bullet (the default), number, letter or roman.", false, "bullet", "number", "letter", "roman"),
                new A("start", K.Expression, "The first number, such as 3.")), DocumentElements.List);
            p.Element(new ElementInfo("Item", "An item of a list. Its text works like a Paragraph's.", ContentKind.Template)
            {
                Context = ElementContext.Script,
                MustBeInside = new[] { "List" },
                LiteralText = TextLiterals.Maths,
                Example = "Item \"Gliders move by **one cell** diagonally.\"",
            }, DocumentElements.Item);
            p.Element(Info("Table", "A table: from data:, or from Tr rows inside it. Tables with a caption are numbered.", ContentKind.Statements,
                "Table(caption: \"Patterns\", data: $patterns, columns: { Pattern: 'name', Period: 'period' }, align: \"lr\")",
                new A("caption", K.Template, "The caption, shown above the table as 'Table 1: ...'."), Id,
                new A("data", K.Expression, "Rows: a list of maps (each a row) or a list of lists."),
                new A("columns", K.Expression, "Which values of each map to show: a list of keys ['name', 'age'], or a map from heading to key { Name: 'name' }."),
                new A("align", K.Template, "Column alignment, one letter a column: l, c or r, such as lrr."),
                new A("widths", K.Expression, "Relative column widths for a table across the page, such as [3, 1, 1]."),
                new A("rules", K.Enum, "booktabs, grid or none (the document's setting is the default).", false, "booktabs", "grid", "none"),
                new A("header", K.Bool, "no: the first row of data: isn't a heading row."),
                new A("size", K.Expression, "Text size relative to the body, such as 0.9.")), DocumentElements.Table);
            p.Element(new ElementInfo("Tr", "A table row: cells separated by |, each like a Paragraph's text. \\| is a | inside a cell.", ContentKind.Template,
                new A("header", K.Bool, "yes makes it a heading row, repeated at the top of each page."))
            {
                Context = ElementContext.Script,
                MustBeInside = new[] { "Table" },
                LiteralText = TextLiterals.Maths,
                Example = "Tr(header) \"Pattern | Period | Speed\"\nTr \"Glider | 4 | c/4\"",
            }, DocumentElements.Row);
            p.Element(Info("Figure", "An image (PNG or JPEG) with a caption. Figures with a caption are numbered.", ContentKind.Empty,
                "Figure(path: \"glider.png\", caption: \"A glider\", id: fig-glider, width: 0.5)",
                new A("path", K.Path, "The image, relative to the script.", true),
                new A("caption", K.Template, "The caption, shown below as 'Figure 1: ...'."), Id,
                new A("width", K.Expression, "The share of the text width, from 0 to 1 (or '60%').")), DocumentElements.Figure);
            p.Element(new ElementInfo("Math", "A displayed equation in LaTeX, numbered unless numbered: no. Its text is used as written, so braces are LaTeX's. Inside matrix, cases and aligned, a new line ends a row.", ContentKind.Template,
                Id, new A("numbered", K.Bool, "no leaves it without an equation number."))
            {
                Context = ElementContext.Script,
                MustBeInside = Inside,
                InsideAnyAncestor = true,
                LiteralText = TextLiterals.All,
                Example = "Math(id: eq-growth) \"N(t) = N_0 e^{rt}\"",
            }, DocumentElements.Math);
            p.Element(new ElementInfo("Code", "A block of source code in a monospaced font, used as written.", ContentKind.Template,
                Id, new A("language", K.Text, "The language, such as python, for Markdown and HTML."))
            {
                Context = ElementContext.Script,
                MustBeInside = Inside,
                InsideAnyAncestor = true,
                LiteralText = TextLiterals.All,
                Example = "Code(language: kscript) \"echo 'Hello'\"",
            }, DocumentElements.Code);
            p.Element(Info("Quote", "A quotation, set in from both margins.", ContentKind.Template,
                "Quote(by: \"John Conway\") \"I hate the Game of Life.\"",
                new A("by", K.Template, "Who said it.")), DocumentElements.Quote);
            p.Element(Info("Contents", "The table of contents, with page numbers in PDFs.", ContentKind.Empty, "Contents(depth: 2)",
                new A("title", K.Template, "The heading (default Contents)."),
                new A("depth", K.Expression, "How many levels to list: 2 lists sections and subsections (default 3).")), DocumentElements.Contents);
            p.Element(Info("Bibliography", "The list of works cited with [@key]: from a BibTeX file, Source elements inside it, or both. Without one, the list goes at the end.", ContentKind.Statements,
                "Bibliography(path: \"refs.bib\")",
                new A("path", K.Path, "A BibTeX (.bib) file, relative to the script."),
                new A("title", K.Template, "The heading (default References).")), DocumentElements.Bibliography);
            p.Element(new ElementInfo("Source", "A work to cite, written out: id: is the key for [@key], and other attributes are BibTeX fields (author, title, year, journal, volume, pages, publisher, url, doi...).", ContentKind.Empty,
                new A("id", K.Text, "The citation key, as in [@conway1970].", true),
                new A("type", K.Text, "article, book, inproceedings, misc..."),
                new A("author", K.Template, "Authors, separated by ' and ' as in BibTeX."),
                new A("title", K.Template, "The title."),
                new A("year", K.Template, "The year."))
            {
                Context = ElementContext.Script,
                MustBeInside = new[] { "Bibliography" },
                AllowsExtraAttributes = true,
                Example = "Source(id: gardner1970, type: article, author: \"Martin Gardner\", title: \"The fantastic combinations of John Conway's new solitaire game life\", journal: \"Scientific American\", year: 1970, volume: 223, pages: \"120--123\")",
            }, DocumentElements.Source);
            p.Element(Info("PageBreak", "Starts a new page.", ContentKind.Empty, "PageBreak()"), DocumentElements.PageBreak);
            p.Element(Info("VSpace", "Vertical space.", ContentKind.Empty, "VSpace(height: 1cm)",
                new A("height", K.Template, "How much: a length such as 12pt, 5mm or 1cm.", true)), DocumentElements.VSpace);
            p.Element(Info("Rule", "A horizontal line across the text.", ContentKind.Empty, "Rule()"), DocumentElements.Rule);
            p.Element(Info("Letter", "A letter: the sender's address and date on the right, the recipient, a subject line, a greeting, the body inside it, a closing and a signature.", ContentKind.Statements,
                "Letter(from: \"1 High Street\\nCambridge\", to: \"Sam Smith\\n2 Low Road\\nLeeds\", subject: \"Your order\", opening: \"Dear Sam,\", closing: \"Best wishes,\", signature: \"Ada\") {\n  Paragraph \"Thank you for your order.\"\n}",
                new A("from", K.Template, "The sender's name and address, one line each."),
                new A("to", K.Template, "The recipient's name and address, one line each."),
                new A("date", K.Template, "The date (default: today, as 20 November 2026)."),
                new A("subject", K.Template, "The subject line, shown in bold."),
                new A("opening", K.Template, "The greeting, such as 'Dear Sam,'."),
                new A("closing", K.Template, "The sign-off, such as 'Yours sincerely,'."),
                new A("signature", K.Template, "The name under the sign-off, with a line for a title if needed.")), DocumentElements.Letter);
            p.Element(Info("Invoice", "An invoice: from and to addresses, the invoice number and dates, a table of items with subtotal, tax and total, and notes.", ContentKind.Empty,
                "Invoice(number: \"INV-042\", from: \"Pantall Ltd\\n1 High Street\", to: \"Acme plc\\n2 Low Road\", currency: GBP, tax: 20, items: [{ description: 'Consulting', quantity: 3, price: 450 }])",
                new A("items", K.Expression, "The lines: a list of maps with description, quantity (default 1) and price (or amount).", true),
                new A("number", K.Template, "The invoice number."),
                new A("from", K.Template, "Your name and address, one line each."),
                new A("to", K.Template, "The customer's name and address."),
                new A("date", K.Template, "The invoice date (default: today)."),
                new A("due", K.Template, "When payment is due."),
                new A("currency", K.Template, "A currency code (GBP, USD, EUR, JPY...) or symbol (default GBP)."),
                new A("tax", K.Expression, "The tax rate in percent, such as 20. Leave it out for no tax line."),
                new A("tax_name", K.Template, "What the tax is called (default VAT)."),
                new A("paid", K.Expression, "An amount already paid, shown with the balance due."),
                new A("notes", K.Template, "Payment details or terms, under the table."),
                new A("title", K.Template, "The heading (default Invoice).")), DocumentElements.Invoice);

            foreach (var m in DocumentValue.Methods)
                p.Method(KType.Document, m.Name, m.Params, m.Description, m.Returns, (t, c) => ((DocumentValue)t).Invoke(c, m.Name));

            p.Command("latex_escape", "text", "Text made safe to put in a LaTeX file: \\ { } $ & % # _ ~ ^ escaped.", KType.String,
                c => LatexText.Escape(c.Str(0)), "@latex_escape('50% of $10 & up')");
            p.Command("document_templates", "", "The built-in document templates and what each is for: { paper: '...', report: '...' }.", KType.Map,
                c => Templates.Descriptions.ToDictionary(kv => kv.Key, kv => (object)kv.Value), "@document_templates()->keys()");

            p.Examples(
                ("document->save", "<do>$doc->save('report.docx')</do>"),
                ("document->pages", "<echo>{ $doc->pages() } pages</echo>"),
                ("document->markdown", "<echo>{ $doc->markdown() }</echo>"),
                ("document->latex", "<do>@write_file('paper.tex', $doc->latex())</do>"),
                ("document->html", "<echo>{ $doc->html()->length() }</echo>"),
                ("document->words", "<echo>{ $doc->words() } words</echo>"));
        }
    }
}
