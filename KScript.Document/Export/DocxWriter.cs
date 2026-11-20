using System.IO.Compression;
using System.Security;
using System.Text;

using KScript.Documents.Maths;
using KScript.Documents.Text;

namespace KScript.Documents.Export
{
    /// <summary>
    /// Writes a document as a Word file (.docx, Office Open XML): real headings, lists, tables, footnotes, images,
    /// equations Word can edit, a contents field and page numbers. Word, LibreOffice, Pages and Google Docs open it.
    /// </summary>
    public sealed class DocxWriter
    {
        private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        private const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private const string Namespaces =
            "xmlns:w=\"" + W + "\" xmlns:r=\"" + R + "\" " +
            "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" " +
            "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
            "xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\" " +
            "xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\"";

        private readonly Document doc;
        private readonly Resolved resolved;
        private readonly Part main = new Part("document");
        private readonly Part notes = new Part("footnotes");
        private readonly List<(string Name, byte[] Bytes)> media = new List<(string, byte[])>();
        private readonly List<string> numbering = new List<string>();
        private int bookmarks;
        private int drawings;
        private bool afterHeading = true;

        /// <summary>A part of the package with its own relationships (hyperlinks, images).</summary>
        private sealed class Part
        {
            public Part(string name) => Name = name;

            public string Name { get; }
            public List<(string Id, string Type, string Target, bool External)> Rels { get; } = new List<(string, string, string, bool)>();

            public string Add(string type, string target, bool external)
            {
                string id = "rId" + (Rels.Count + 100);
                Rels.Add((id, type, target, external));
                return id;
            }
        }

        private DocxWriter(Document doc)
        {
            this.doc = doc;
            resolved = Resolver.Resolve(doc);
        }

        public static byte[] Write(Document doc) => new DocxWriter(doc).Build();

        private DocumentStyle S => doc.Style;

        private static string E(string s) => SecurityElement.Escape(s ?? string.Empty);

        private static int Twips(double pt) => (int)Math.Round(pt * 20);

        private static string FontName(FontFamily f) => f switch
        {
            FontFamily.Sans => "Arial",
            FontFamily.Mono => "Courier New",
            _ => "Times New Roman",
        };

        private byte[] Build()
        {
            var body = new StringBuilder();
            if (S.ShowTitle && doc.Title != null) TitleBlock(body);
            if (doc.Abstract.Count > 0)
            {
                body.Append("<w:p><w:pPr><w:pStyle w:val=\"AbstractHeading\"/></w:pPr>").Append(TextRun("Abstract", new RunStyle())).Append("</w:p>");
                foreach (Block b in doc.Abstract) Block(b, body, abstractBlock: true);
            }
            if (doc.Keywords.Count > 0)
                body.Append("<w:p>").Append(TextRun("Keywords: ", new RunStyle { Bold = true })).Append(TextRun(string.Join(", ", doc.Keywords), new RunStyle())).Append("</w:p>");
            foreach (Block b in doc.Blocks) Block(b, body);

            string header = main.Add("http://schemas.openxmlformats.org/officeDocument/2006/relationships/header", "header1.xml", false);
            string footer = main.Add("http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer", "footer1.xml", false);
            body.Append("<w:sectPr><w:headerReference w:type=\"default\" r:id=\"").Append(header).Append("\"/><w:footerReference w:type=\"default\" r:id=\"").Append(footer).Append("\"/>");
            body.Append("<w:pgSz w:w=\"").Append(Twips(S.PageWidth)).Append("\" w:h=\"").Append(Twips(S.PageHeight)).Append('"');
            if (S.PageWidth > S.PageHeight) body.Append(" w:orient=\"landscape\"");
            body.Append("/><w:pgMar w:top=\"").Append(Twips(S.MarginTop)).Append("\" w:right=\"").Append(Twips(S.MarginRight)).Append("\" w:bottom=\"").Append(Twips(S.MarginBottom))
                .Append("\" w:left=\"").Append(Twips(S.MarginLeft)).Append("\" w:header=\"").Append(Twips(S.MarginTop / 2)).Append("\" w:footer=\"").Append(Twips(S.MarginBottom / 2)).Append("\" w:gutter=\"0\"/>");
            if (S.PlainFirstPage) body.Append("<w:titlePg/>");
            body.Append("</w:sectPr>");

            using var stream = new MemoryStream();
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                Entry(zip, "[Content_Types].xml", ContentTypes());
                Entry(zip, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
                    "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/></Relationships>");
                Entry(zip, "docProps/core.xml", Core());
                Entry(zip, "word/document.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:document " + Namespaces + "><w:body>" + body + "</w:body></w:document>");
                Entry(zip, "word/styles.xml", Styles());
                Entry(zip, "word/settings.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:settings xmlns:w=\"" + W + "\">" +
                    (resolved.Sections.Count > 0 && doc.AllBlocks().Any(b => b is Contents) ? "<w:updateFields w:val=\"true\"/>" : string.Empty) +
                    "<w:defaultTabStop w:val=\"720\"/><w:footnotePr><w:footnote w:id=\"-1\"/><w:footnote w:id=\"0\"/></w:footnotePr><w:compat><w:compatSetting w:name=\"compatibilityMode\" w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"15\"/></w:compat></w:settings>");
                Entry(zip, "word/numbering.xml", Numbering());
                Entry(zip, "word/footnotes.xml", Footnotes());
                Entry(zip, "word/header1.xml", HeaderFooter("hdr", S.Header));
                Entry(zip, "word/footer1.xml", HeaderFooter("ftr", S.Footer));
                Entry(zip, "word/_rels/document.xml.rels", Rels(main, document: true));
                Entry(zip, "word/_rels/footnotes.xml.rels", Rels(notes, document: false));
                foreach (var (name, bytes) in media)
                {
                    ZipArchiveEntry entry = zip.CreateEntry("word/media/" + name, CompressionLevel.NoCompression);
                    entry.LastWriteTime = Fixed;
                    using Stream s = entry.Open();
                    s.Write(bytes, 0, bytes.Length);
                }
            }
            return stream.ToArray();
        }

        /// <summary>Every file in the package gets the same date, so the same document always makes the same bytes.</summary>
        private static readonly DateTimeOffset Fixed = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

        private static void Entry(ZipArchive zip, string name, string content)
        {
            ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            entry.LastWriteTime = Fixed;
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(content);
        }

        private string ContentTypes()
        {
            var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
            sb.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>");
            sb.Append("<Default Extension=\"png\" ContentType=\"image/png\"/><Default Extension=\"jpeg\" ContentType=\"image/jpeg\"/>");
            void Override(string part, string type) => sb.Append("<Override PartName=\"").Append(part).Append("\" ContentType=\"application/vnd.openxmlformats-officedocument.").Append(type).Append("\"/>");
            Override("/word/document.xml", "wordprocessingml.document.main+xml");
            Override("/word/styles.xml", "wordprocessingml.styles+xml");
            Override("/word/settings.xml", "wordprocessingml.settings+xml");
            Override("/word/numbering.xml", "wordprocessingml.numbering+xml");
            Override("/word/footnotes.xml", "wordprocessingml.footnotes+xml");
            Override("/word/header1.xml", "wordprocessingml.header+xml");
            Override("/word/footer1.xml", "wordprocessingml.footer+xml");
            sb.Append("<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/></Types>");
            return sb.ToString();
        }

        private string Core()
        {
            var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\">");
            if (doc.Title != null) sb.Append("<dc:title>").Append(E(doc.Title)).Append("</dc:title>");
            if (doc.Authors.Count > 0) sb.Append("<dc:creator>").Append(E(string.Join("; ", doc.Authors.Select(a => a.Name)))).Append("</dc:creator>");
            if (doc.Keywords.Count > 0) sb.Append("<cp:keywords>").Append(E(string.Join(", ", doc.Keywords))).Append("</cp:keywords>");
            sb.Append("<dc:language>").Append(E(S.Language)).Append("</dc:language></cp:coreProperties>");
            return sb.ToString();
        }

        private static string Rels(Part part, bool document)
        {
            var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            if (document)
            {
                foreach (var (id, type) in new[] { ("rId1", "styles"), ("rId2", "settings"), ("rId3", "numbering"), ("rId4", "footnotes") })
                    sb.Append("<Relationship Id=\"").Append(id).Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/").Append(type).Append("\" Target=\"").Append(type).Append(".xml\"/>");
            }
            foreach (var rel in part.Rels)
            {
                sb.Append("<Relationship Id=\"").Append(rel.Id).Append("\" Type=\"").Append(rel.Type).Append("\" Target=\"").Append(E(rel.Target)).Append('"');
                if (rel.External) sb.Append(" TargetMode=\"External\"");
                sb.Append("/>");
            }
            sb.Append("</Relationships>");
            return sb.ToString();
        }

        private string Styles()
        {
            string body = FontName(S.Font), heading = FontName(S.HeadingFont);
            int size = (int)Math.Round(S.FontSize * 2);
            string accent = S.Accent.Substring(1).ToUpperInvariant();
            int line = (int)Math.Round(240 * S.LineHeight / 1.15);
            int after = Twips(S.ParagraphSpacing * S.LineHeight * S.FontSize);
            var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:styles xmlns:w=\"" + W + "\">");
            sb.Append("<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii=\"").Append(body).Append("\" w:hAnsi=\"").Append(body).Append("\" w:cs=\"").Append(body)
                .Append("\" w:eastAsia=\"").Append(body).Append("\"/><w:sz w:val=\"").Append(size).Append("\"/><w:szCs w:val=\"").Append(size).Append("\"/><w:lang w:val=\"").Append(E(S.Language)).Append("\"/></w:rPr></w:rPrDefault>");
            sb.Append("<w:pPrDefault><w:pPr><w:spacing w:after=\"").Append(after).Append("\" w:line=\"").Append(line).Append("\" w:lineRule=\"auto\"/>");
            if (S.Align == PaperAlign.Justify) sb.Append("<w:jc w:val=\"both\"/>");
            sb.Append("</w:pPr></w:pPrDefault></w:docDefaults>");
            void Style(string id, string name, string ppr, string rpr, string type = "paragraph", string basedOn = "Normal")
            {
                sb.Append("<w:style w:type=\"").Append(type).Append("\" w:styleId=\"").Append(id).Append("\"><w:name w:val=\"").Append(name).Append("\"/>");
                if (basedOn != null && id != "Normal") sb.Append("<w:basedOn w:val=\"").Append(basedOn).Append("\"/>");
                sb.Append("<w:qFormat/>");
                if (ppr != null) sb.Append("<w:pPr>").Append(ppr).Append("</w:pPr>");
                if (rpr != null) sb.Append("<w:rPr>").Append(rpr).Append("</w:rPr>");
                sb.Append("</w:style>");
            }
            Style("Normal", "Normal", null, null, basedOn: null);
            Style("Title", "Title", "<w:jc w:val=\"center\"/><w:spacing w:before=\"0\" w:after=\"120\"/>", $"<w:rFonts w:ascii=\"{heading}\" w:hAnsi=\"{heading}\"/><w:sz w:val=\"{size * 17 / 10}\"/>");
            Style("Subtitle", "Subtitle", "<w:jc w:val=\"center\"/><w:spacing w:after=\"120\"/>", $"<w:sz w:val=\"{size * 12 / 10}\"/>");
            Style("Author", "Author", "<w:jc w:val=\"center\"/><w:spacing w:after=\"0\"/>", null);
            Style("AbstractHeading", "Abstract Heading", "<w:jc w:val=\"center\"/><w:spacing w:before=\"240\" w:after=\"60\"/>", "<w:b/>");
            Style("Abstract", "Abstract", "<w:ind w:left=\"720\" w:right=\"720\"/>", $"<w:sz w:val=\"{size * 92 / 100}\"/>");
            double[] scales = { 1.4, 1.2, 1.05, 1, 1, 1 };
            for (int level = 1; level <= 6; level++)
            {
                Style("Heading" + level, "heading " + level,
                    $"<w:keepNext/><w:keepLines/><w:jc w:val=\"left\"/><w:spacing w:before=\"{(level == 1 ? 360 : 240)}\" w:after=\"120\"/><w:outlineLvl w:val=\"{level - 1}\"/>",
                    $"<w:rFonts w:ascii=\"{heading}\" w:hAnsi=\"{heading}\"/><w:b/><w:color w:val=\"{accent}\"/><w:sz w:val=\"{(int)Math.Round(size * scales[level - 1])}\"/>");
            }
            Style("Caption", "caption", "<w:jc w:val=\"center\"/><w:spacing w:before=\"120\" w:after=\"240\"/>", $"<w:sz w:val=\"{size * 92 / 100}\"/>");
            Style("Quote", "Quote", "<w:ind w:left=\"720\" w:right=\"720\"/>", "<w:i/>");
            Style("Code", "Code", "<w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/><w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"F5F5F5\"/><w:jc w:val=\"left\"/>", $"<w:rFonts w:ascii=\"Courier New\" w:hAnsi=\"Courier New\"/><w:sz w:val=\"{size * 9 / 10}\"/>");
            Style("ListParagraph", "List Paragraph", "<w:spacing w:after=\"60\"/>", null);
            Style("Bibliography", "Bibliography", "<w:ind w:left=\"440\" w:hanging=\"440\"/><w:jc w:val=\"left\"/>", null);
            Style("FootnoteText", "footnote text", "<w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/>", $"<w:sz w:val=\"{size * 8 / 10}\"/>");
            for (int level = 1; level <= 3; level++) Style("TOC" + level, "toc " + level, $"<w:tabs><w:tab w:val=\"right\" w:leader=\"dot\" w:pos=\"{Twips(S.TextWidth)}\"/></w:tabs><w:spacing w:after=\"60\"/><w:ind w:left=\"{(level - 1) * 240}\"/>", null);
            Style("FootnoteReference", "footnote reference", null, "<w:vertAlign w:val=\"superscript\"/>", "character", null);
            Style("Hyperlink", "Hyperlink", null, $"<w:color w:val=\"{(accent == "000000" ? "1F4E79" : accent)}\"/>", "character", null);
            sb.Append("</w:styles>");
            return sb.ToString();
        }

        private string Numbering()
        {
            var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:numbering xmlns:w=\"" + W + "\">");
            string[] formats = { "bullet", "decimal", "lowerLetter", "lowerRoman" };
            string[] bullets = { "•", "–", "◦" };
            for (int a = 0; a < formats.Length; a++)
            {
                sb.Append("<w:abstractNum w:abstractNumId=\"").Append(a).Append("\"><w:multiLevelType w:val=\"hybridMultilevel\"/>");
                for (int level = 0; level < 9; level++)
                {
                    string fmt = level == 0 || a == 0 ? formats[a] : formats[1 + (level % 3)];
                    string text = fmt == "bullet" ? bullets[level % 3] : "%" + (level + 1) + ".";
                    sb.Append("<w:lvl w:ilvl=\"").Append(level).Append("\"><w:start w:val=\"1\"/><w:numFmt w:val=\"").Append(fmt).Append("\"/><w:lvlText w:val=\"").Append(text)
                        .Append("\"/><w:lvlJc w:val=\"left\"/><w:pPr><w:ind w:left=\"").Append(360 * (level + 1) + 360).Append("\" w:hanging=\"360\"/></w:pPr></w:lvl>");
                }
                sb.Append("</w:abstractNum>");
            }
            for (int n = 0; n < numbering.Count; n++) sb.Append(numbering[n].Replace("{id}", (n + 1).ToString()));
            sb.Append("</w:numbering>");
            return sb.ToString();
        }

        /// <summary>A new numbering instance for a list, so each ordered list starts again from its start number.</summary>
        private int NewList(ListBlock list)
        {
            int abstractId = list.Style switch { ListStyle.Bullet => 0, ListStyle.Number => 1, ListStyle.Letter => 2, _ => 3 };
            numbering.Add($"<w:num w:numId=\"{{id}}\"><w:abstractNumId w:val=\"{abstractId}\"/><w:lvlOverride w:ilvl=\"0\"><w:startOverride w:val=\"{list.Start}\"/></w:lvlOverride></w:num>");
            return numbering.Count;
        }

        private string Footnotes()
        {
            var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:footnotes " + Namespaces + ">");
            sb.Append("<w:footnote w:type=\"separator\" w:id=\"-1\"><w:p><w:pPr><w:spacing w:after=\"0\"/></w:pPr><w:r><w:separator/></w:r></w:p></w:footnote>");
            sb.Append("<w:footnote w:type=\"continuationSeparator\" w:id=\"0\"><w:p><w:pPr><w:spacing w:after=\"0\"/></w:pPr><w:r><w:continuationSeparator/></w:r></w:p></w:footnote>");
            foreach (FootnoteInline f in resolved.Footnotes)
            {
                sb.Append("<w:footnote w:id=\"").Append(f.Number).Append("\"><w:p><w:pPr><w:pStyle w:val=\"FootnoteText\"/></w:pPr><w:r><w:rPr><w:rStyle w:val=\"FootnoteReference\"/></w:rPr><w:footnoteRef/></w:r>");
                sb.Append(TextRun(" ", new RunStyle())).Append(Runs(f.Children, new RunStyle(), notes)).Append("</w:p></w:footnote>");
            }
            sb.Append("</w:footnotes>");
            return sb.ToString();
        }

        private string HeaderFooter(string tag, string spec)
        {
            var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:" + tag + " " + Namespaces + "><w:p><w:pPr><w:tabs>");
            sb.Append("<w:tab w:val=\"center\" w:pos=\"").Append(Twips(S.TextWidth / 2)).Append("\"/><w:tab w:val=\"right\" w:pos=\"").Append(Twips(S.TextWidth)).Append("\"/></w:tabs><w:spacing w:after=\"0\"/></w:pPr>");
            string[] parts = (spec ?? string.Empty).Split('|');
            if (parts.Length == 1) parts = new[] { string.Empty, parts[0], string.Empty };
            var small = new RunStyle { Scale = 0.9 };
            for (int i = 0; i < Math.Min(3, parts.Length); i++)
            {
                if (i > 0) sb.Append("<w:r><w:tab/></w:r>");
                foreach (string piece in System.Text.RegularExpressions.Regex.Split(parts[i].Trim(), @"(\[(?:page|pages|title|author|date)\])"))
                {
                    if (piece.Length == 0) continue;
                    switch (piece)
                    {
                        case "[page]":
                        case "[pages]":
                            sb.Append("<w:fldSimple w:instr=\" ").Append(piece == "[page]" ? "PAGE" : "NUMPAGES").Append(" \">").Append(TextRun("1", small)).Append("</w:fldSimple>");
                            break;
                        default:
                            sb.Append(TextRun(Field(piece), small));
                            break;
                    }
                }
            }
            sb.Append("</w:p></w:").Append(tag).Append('>');
            return sb.ToString();
        }

        /// <summary>[title], [author] and [date] in a header or footer; other text as it is.</summary>
        private string Field(string piece) => piece switch
        {
            "[title]" => doc.Title ?? string.Empty,
            "[author]" => string.Join(", ", doc.Authors.Select(a => a.Name)),
            "[date]" => doc.Date ?? string.Empty,
            _ => piece,
        };

        private void TitleBlock(StringBuilder body)
        {
            if (S.TitlePage) body.Append("<w:p><w:pPr><w:spacing w:before=\"").Append(Twips(S.PageHeight * 0.25)).Append("\"/></w:pPr></w:p>");
            body.Append("<w:p><w:pPr><w:pStyle w:val=\"Title\"/></w:pPr>").Append(TextRun(doc.Title, new RunStyle())).Append("</w:p>");
            if (doc.Subtitle != null) body.Append("<w:p><w:pPr><w:pStyle w:val=\"Subtitle\"/></w:pPr>").Append(TextRun(doc.Subtitle, new RunStyle())).Append("</w:p>");
            foreach (Author a in doc.Authors)
            {
                body.Append("<w:p><w:pPr><w:pStyle w:val=\"Author\"/><w:spacing w:before=\"120\" w:after=\"0\"/></w:pPr>").Append(TextRun(a.Name, new RunStyle()));
                if (a.Affiliation != null) body.Append("<w:r><w:br/></w:r>").Append(TextRun(a.Affiliation, new RunStyle { Scale = 0.9 }));
                if (a.Email != null) body.Append("<w:r><w:br/></w:r>").Append(TextRun(a.Email, new RunStyle { Scale = 0.9, Mono = true }));
                body.Append("</w:p>");
            }
            if (doc.Date != null) body.Append("<w:p><w:pPr><w:pStyle w:val=\"Author\"/><w:spacing w:before=\"120\" w:after=\"240\"/></w:pPr>").Append(TextRun(doc.Date, new RunStyle())).Append("</w:p>");
            if (S.TitlePage) body.Append("<w:p><w:r><w:br w:type=\"page\"/></w:r></w:p>");
        }

        private string Bookmark(Block b, string content)
        {
            if (b.Id == null) return content;
            int id = ++bookmarks;
            return $"<w:bookmarkStart w:id=\"{id}\" w:name=\"{BookmarkName(b.Id)}\"/>{content}<w:bookmarkEnd w:id=\"{id}\"/>";
        }

        /// <summary>Word bookmark names are letters, digits and _, starting with a letter, at most 40 characters.</summary>
        internal static string BookmarkName(string id)
        {
            string clean = new string(id.Select(c => char.IsLetterOrDigit(c) && c < 128 ? c : '_').ToArray());
            string name = "k_" + clean;
            return name.Length > 40 ? name.Substring(0, 40) : name;
        }

        private void Block(Block block, StringBuilder body, bool abstractBlock = false, int listLevel = -1, int listId = 0)
        {
            switch (block)
            {
                case Section s:
                {
                    body.Append("<w:p><w:pPr><w:pStyle w:val=\"Heading").Append(Math.Min(6, s.Level)).Append("\"/>");
                    if (s.NewPage) body.Append("<w:pageBreakBefore/>");
                    body.Append("</w:pPr>");
                    string title = (s.Number != null ? TextRun(s.Number + " ", new RunStyle()) : string.Empty) + Runs(s.Title, new RunStyle(), main);
                    body.Append(Bookmark(s, title)).Append("</w:p>");
                    afterHeading = true;
                    foreach (Block b in s.Blocks) Block(b, body);
                    break;
                }
                case Paragraph p:
                    Paragraph(p, body, abstractBlock ? "Abstract" : listLevel >= 0 ? "ListParagraph" : null, listLevel, listId);
                    break;
                case ListBlock list:
                {
                    int id = NewList(list);
                    foreach (ListItem item in list.Items)
                    {
                        bool first = true;
                        foreach (Block b in item.Blocks)
                        {
                            if (b is ListBlock nested)
                            {
                                Block(nested, body, abstractBlock, listLevel + 1);
                                continue;
                            }
                            // Only an item's first paragraph has the bullet; the rest line up under it.
                            Block(b, body, abstractBlock, first ? listLevel + 1 : -2 - (listLevel + 1), id);
                            first = false;
                        }
                    }
                    afterHeading = false;
                    break;
                }
                case Table t:
                    Table(t, body);
                    break;
                case Figure f:
                    Figure(f, body);
                    break;
                case MathBlock m:
                {
                    string math = MathOmml.Write(m.Node);
                    if (m.Numbered)
                    {
                        body.Append("<w:p><w:pPr><w:tabs><w:tab w:val=\"center\" w:pos=\"").Append(Twips(S.TextWidth / 2)).Append("\"/><w:tab w:val=\"right\" w:pos=\"").Append(Twips(S.TextWidth))
                            .Append("\"/></w:tabs><w:jc w:val=\"left\"/><w:spacing w:before=\"120\" w:after=\"120\"/></w:pPr><w:r><w:tab/></w:r>")
                            .Append(Bookmark(m, math)).Append("<w:r><w:tab/></w:r>").Append(TextRun("(" + m.Number + ")", new RunStyle())).Append("</w:p>");
                    }
                    else
                    {
                        body.Append("<w:p><w:pPr><w:jc w:val=\"center\"/></w:pPr><m:oMathPara>").Append(math).Append("</m:oMathPara></w:p>");
                    }
                    // The text after display maths carries on the paragraph, so it isn't indented.
                    afterHeading = true;
                    break;
                }
                case CodeBlock c:
                {
                    string[] lines = c.Text.Split('\n');
                    for (int i = 0; i < lines.Length; i++)
                    {
                        body.Append("<w:p><w:pPr><w:pStyle w:val=\"Code\"/>").Append(i == lines.Length - 1 ? "<w:spacing w:after=\"200\"/>" : string.Empty).Append("</w:pPr>")
                            .Append(TextRun(lines[i].Length == 0 ? " " : lines[i], new RunStyle())).Append("</w:p>");
                    }
                    afterHeading = false;
                    break;
                }
                case Quote q:
                    foreach (Block b in q.Blocks)
                    {
                        if (b is Paragraph qp) Paragraph(qp, body, "Quote", -1, 0);
                        else Block(b, body);
                    }
                    if (q.Attribution != null) body.Append("<w:p><w:pPr><w:pStyle w:val=\"Quote\"/><w:jc w:val=\"right\"/></w:pPr>").Append(TextRun("— " + q.Attribution, new RunStyle())).Append("</w:p>");
                    break;
                case PageBreak:
                    body.Append("<w:p><w:r><w:br w:type=\"page\"/></w:r></w:p>");
                    break;
                case Spacer sp:
                    body.Append("<w:p><w:pPr><w:spacing w:before=\"0\" w:after=\"0\" w:line=\"").Append(Math.Max(20, Twips(sp.Height))).Append("\" w:lineRule=\"exact\"/></w:pPr></w:p>");
                    break;
                case Rule:
                    body.Append("<w:p><w:pPr><w:pBdr><w:bottom w:val=\"single\" w:sz=\"6\" w:space=\"1\" w:color=\"auto\"/></w:pBdr></w:pPr></w:p>");
                    break;
                case Contents c:
                    Contents(c, body);
                    break;
                case Bibliography bib:
                    if (resolved.Cited.Count == 0) break;
                    body.Append("<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr>").Append(TextRun(bib.Title, new RunStyle())).Append("</w:p>");
                    foreach (Source src in resolved.Cited)
                    {
                        body.Append("<w:p><w:pPr><w:pStyle w:val=\"Bibliography\"/></w:pPr>");
                        if (S.Citations == CitationStyle.Numeric) body.Append(TextRun("[" + src.Number + "]", new RunStyle())).Append("<w:r><w:tab/></w:r>");
                        body.Append(Runs(BibTex.Format(src, S.Citations), new RunStyle(), main)).Append("</w:p>");
                    }
                    break;
            }
        }

        private void Paragraph(Paragraph p, StringBuilder body, string style, int listLevel, int listId)
        {
            body.Append("<w:p><w:pPr>");
            if (style != null) body.Append("<w:pStyle w:val=\"").Append(style).Append("\"/>");
            if (listLevel >= 0) body.Append("<w:numPr><w:ilvl w:val=\"").Append(listLevel).Append("\"/><w:numId w:val=\"").Append(listId).Append("\"/></w:numPr>");
            else if (listLevel <= -2) body.Append("<w:ind w:left=\"").Append(360 * (-2 - listLevel + 1) + 360).Append("\"/>");
            if (p.SpaceBefore > 0) body.Append("<w:spacing w:before=\"").Append(Twips(p.SpaceBefore)).Append("\"/>");
            if (style == null && S.ParagraphSpacing == 0 && !afterHeading && !p.NoIndent && p.Align == null) body.Append("<w:ind w:firstLine=\"").Append(Twips(S.FontSize * 1.5)).Append("\"/>");
            if (p.Align != null) body.Append("<w:jc w:val=\"").Append(p.Align switch { PaperAlign.Right => "right", PaperAlign.Center => "center", PaperAlign.Justify => "both", _ => "left" }).Append("\"/>");
            body.Append("</w:pPr>");
            var rs = new RunStyle { Bold = p.Bold, Scale = p.Scale, Color = p.Color };
            body.Append(Bookmark(p, Runs(p.Inlines, rs, main))).Append("</w:p>");
            afterHeading = false;
        }

        private void Contents(Contents c, StringBuilder body)
        {
            body.Append("<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr>").Append(TextRun(c.Title, new RunStyle())).Append("</w:p>");
            var entries = resolved.Sections.Where(s => s.Level <= Math.Min(3, c.Depth)).ToList();
            for (int i = 0; i < entries.Count; i++)
            {
                Section s = entries[i];
                body.Append("<w:p><w:pPr><w:pStyle w:val=\"TOC").Append(s.Level).Append("\"/></w:pPr>");
                if (i == 0)
                    body.Append("<w:r><w:fldChar w:fldCharType=\"begin\" w:dirty=\"true\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> TOC \\o \"1-").Append(Math.Min(3, c.Depth)).Append("\" \\h \\z \\u </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>");
                string title = (s.Number != null ? s.Number + " " : string.Empty) + InlineParser.Plain(s.Title);
                if (s.Id != null) body.Append("<w:hyperlink w:anchor=\"").Append(BookmarkName(s.Id)).Append("\">").Append(TextRun(title, new RunStyle())).Append("</w:hyperlink>");
                else body.Append(TextRun(title, new RunStyle()));
                if (i == entries.Count - 1) body.Append("<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>");
                body.Append("</w:p>");
            }
            if (S.TitlePage) body.Append("<w:p><w:r><w:br w:type=\"page\"/></w:r></w:p>");
            afterHeading = true;
        }

        /// <summary>
        /// How wide each column's text is on one line, plus padding: Word and LibreOffice size an autofit table from
        /// its grid, so a grid of the text's widths keeps a small table small, as in the PDF.
        /// </summary>
        private double[] NaturalWidths(Table t, int columns)
        {
            var widths = new double[columns];
            foreach (TableRow row in t.Rows)
            {
                int column = 0;
                foreach (TableCell cell in row.Cells)
                {
                    if (cell.Span <= 1 && column < columns)
                    {
                        var face = new Pdf.Face(S.Font, row.Header || cell.Bold, false);
                        double w = Pdf.StandardFonts.Measure(InlineParser.Plain(cell.Inlines), face, S.FontSize * t.Scale) + 12;
                        widths[column] = Math.Max(widths[column], w);
                    }
                    column += Math.Max(1, cell.Span);
                }
            }
            for (int c = 0; c < columns; c++) widths[c] = Math.Max(widths[c], 24);
            return widths;
        }

        private void Table(Table t, StringBuilder body)
        {
            if (t.Caption != null)
                body.Append("<w:p><w:pPr><w:pStyle w:val=\"Caption\"/><w:keepNext/></w:pPr>").Append(Bookmark(t, TextRun("Table " + t.Number + ": ", new RunStyle { Bold = true }) + Runs(t.Caption, new RunStyle(), main))).Append("</w:p>");
            int columns = Math.Max(1, t.ColumnCount);
            double width = S.TextWidth;
            bool full = t.Widths.Count > 0;
            double[] widths = full ? Enumerable.Range(0, columns).Select(c => c < t.Widths.Count ? t.Widths[c] : 1).ToArray() : NaturalWidths(t, columns);
            double total = widths.Sum();
            if (!full) width = Math.Min(width, total);
            body.Append("<w:tbl><w:tblPr><w:tblW w:w=\"").Append(full ? "5000\" w:type=\"pct" : "0\" w:type=\"auto").Append("\"/><w:jc w:val=\"center\"/><w:tblBorders>");
            string thick = "w:val=\"single\" w:sz=\"12\" w:space=\"0\" w:color=\"000000\"", thin = "w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"000000\"";
            switch (t.Rules)
            {
                case TableRules.Booktabs:
                    body.Append("<w:top ").Append(thick).Append("/><w:bottom ").Append(thick).Append("/>");
                    break;
                case TableRules.Grid:
                    foreach (string side in new[] { "top", "left", "bottom", "right", "insideH", "insideV" }) body.Append("<w:").Append(side).Append(' ').Append(thin).Append("/>");
                    break;
            }
            body.Append("</w:tblBorders><w:tblLayout w:type=\"").Append(full ? "fixed" : "autofit").Append("\"/><w:tblCellMar><w:left w:w=\"100\" w:type=\"dxa\"/><w:right w:w=\"100\" w:type=\"dxa\"/></w:tblCellMar><w:tblLook w:val=\"0000\"/></w:tblPr><w:tblGrid>");
            foreach (double w in widths) body.Append("<w:gridCol w:w=\"").Append(Twips(width * w / total)).Append("\"/>");
            body.Append("</w:tblGrid>");
            for (int i = 0; i < t.Rows.Count; i++)
            {
                TableRow row = t.Rows[i];
                bool lastHeader = row.Header && (i + 1 >= t.Rows.Count || !t.Rows[i + 1].Header);
                body.Append("<w:tr>");
                if (row.Header) body.Append("<w:trPr><w:tblHeader/></w:trPr>");
                int column = 0;
                foreach (TableCell cell in row.Cells)
                {
                    int span = Math.Max(1, cell.Span);
                    double cellWidth = widths.Skip(column).Take(span).Sum() / total * width;
                    ColumnAlign align = t.AlignOf(column + span - 1);
                    body.Append("<w:tc><w:tcPr><w:tcW w:w=\"").Append(Twips(cellWidth)).Append("\" w:type=\"dxa\"/>");
                    if (span > 1) body.Append("<w:gridSpan w:val=\"").Append(span).Append("\"/>");
                    if ((lastHeader && t.Rules == TableRules.Booktabs) || (row.RuleAbove && t.Rules != TableRules.Grid))
                    {
                        body.Append("<w:tcBorders>");
                        if (row.RuleAbove && t.Rules != TableRules.Grid) body.Append("<w:top ").Append(thin).Append("/>");
                        if (lastHeader && t.Rules == TableRules.Booktabs) body.Append("<w:bottom ").Append(thin).Append("/>");
                        body.Append("</w:tcBorders>");
                    }
                    body.Append("</w:tcPr><w:p><w:pPr><w:spacing w:before=\"40\" w:after=\"40\"/><w:jc w:val=\"").Append(align switch { ColumnAlign.Right => "right", ColumnAlign.Center => "center", _ => "left" }).Append("\"/></w:pPr>");
                    body.Append(Runs(cell.Inlines, new RunStyle { Bold = cell.Bold || row.Header, Scale = t.Scale }, main)).Append("</w:p></w:tc>");
                    column += span;
                }
                body.Append("</w:tr>");
            }
            body.Append("</w:tbl><w:p><w:pPr><w:spacing w:after=\"0\"/></w:pPr></w:p>");
            afterHeading = false;
        }

        private void Figure(Figure f, StringBuilder body)
        {
            ImageData image = f.Image ?? ImageData.Load(Path.Combine(doc.BaseDirectory, f.Source));
            string name = "image" + (media.Count + 1) + "." + image.Format;
            media.Add((name, image.Bytes));
            string rel = main.Add("http://schemas.openxmlformats.org/officeDocument/2006/relationships/image", "media/" + name, false);
            double widthPt = f.Width > 0 ? S.TextWidth * f.Width : Math.Min(S.TextWidth, image.Width * 0.75);
            double heightPt = widthPt * image.Height / Math.Max(1, image.Width);
            long cx = (long)(widthPt * 12700), cy = (long)(heightPt * 12700);
            int id = ++drawings;
            string alt = E(f.Caption != null ? InlineParser.Plain(f.Caption) : Path.GetFileName(f.Source));
            body.Append("<w:p><w:pPr><w:jc w:val=\"center\"/><w:keepNext/><w:spacing w:before=\"120\" w:after=\"0\"/></w:pPr><w:r><w:drawing><wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\">");
            body.Append("<wp:extent cx=\"").Append(cx).Append("\" cy=\"").Append(cy).Append("\"/><wp:docPr id=\"").Append(id).Append("\" name=\"Picture ").Append(id).Append("\" descr=\"").Append(alt).Append("\"/>");
            body.Append("<wp:cNvGraphicFramePr><a:graphicFrameLocks noChangeAspect=\"1\"/></wp:cNvGraphicFramePr><a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:pic>");
            body.Append("<pic:nvPicPr><pic:cNvPr id=\"").Append(id).Append("\" name=\"").Append(name).Append("\"/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip r:embed=\"").Append(rel).Append("\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>");
            body.Append("<pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"").Append(cx).Append("\" cy=\"").Append(cy).Append("\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>");
            if (f.Caption != null)
                body.Append("<w:p><w:pPr><w:pStyle w:val=\"Caption\"/></w:pPr>").Append(Bookmark(f, TextRun("Figure " + f.Number + ": ", new RunStyle { Bold = true }) + Runs(f.Caption, new RunStyle(), main))).Append("</w:p>");
            afterHeading = false;
        }

        private sealed class RunStyle
        {
            public bool Bold;
            public bool Italic;
            public bool Mono;
            public double Scale = 1;
            public string Color;
            public string CharStyle;

            public RunStyle With(Action<RunStyle> change)
            {
                var copy = (RunStyle)MemberwiseClone();
                change(copy);
                return copy;
            }
        }

        private string TextRun(string text, RunStyle style)
        {
            var sb = new StringBuilder("<w:r>");
            var rpr = new StringBuilder();
            if (style.CharStyle != null) rpr.Append("<w:rStyle w:val=\"").Append(style.CharStyle).Append("\"/>");
            if (style.Mono) rpr.Append("<w:rFonts w:ascii=\"Courier New\" w:hAnsi=\"Courier New\"/>");
            if (style.Bold) rpr.Append("<w:b/>");
            if (style.Italic) rpr.Append("<w:i/>");
            if (style.Color != null) rpr.Append("<w:color w:val=\"").Append(Units.Colour(style.Color).Substring(1)).Append("\"/>");
            if (style.Scale != 1) rpr.Append("<w:sz w:val=\"").Append((int)Math.Round(S.FontSize * 2 * style.Scale)).Append("\"/>");
            if (rpr.Length > 0) sb.Append("<w:rPr>").Append(rpr).Append("</w:rPr>");
            sb.Append("<w:t xml:space=\"preserve\">").Append(E(text)).Append("</w:t></w:r>");
            return sb.ToString();
        }

        private string Runs(IEnumerable<Inline> inlines, RunStyle style, Part part)
        {
            var sb = new StringBuilder();
            foreach (Inline i in inlines ?? Enumerable.Empty<Inline>())
            {
                switch (i)
                {
                    case TextInline t:
                        sb.Append(TextRun(t.Text, style));
                        break;
                    case StrongInline s:
                        sb.Append(Runs(s.Children, style.With(x => x.Bold = true), part));
                        break;
                    case EmphasisInline e:
                        sb.Append(Runs(e.Children, style.With(x => x.Italic = !x.Italic), part));
                        break;
                    case CodeInline c:
                        sb.Append(TextRun(c.Text, style.With(x => x.Mono = true)));
                        break;
                    case LinkInline l:
                    {
                        string rel = part.Add("http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink", l.Url, true);
                        sb.Append("<w:hyperlink r:id=\"").Append(rel).Append("\">").Append(Runs(l.Children, style.With(x => x.CharStyle = "Hyperlink"), part)).Append("</w:hyperlink>");
                        break;
                    }
                    case MathInline m:
                        sb.Append(MathOmml.Write(m.Node));
                        break;
                    case CiteInline c:
                        sb.Append(TextRun(c.Text, style));
                        break;
                    case RefInline r:
                        sb.Append("<w:hyperlink w:anchor=\"").Append(BookmarkName(r.Id)).Append("\">").Append(TextRun(r.Text, style)).Append("</w:hyperlink>");
                        break;
                    case FootnoteInline f:
                        sb.Append("<w:r><w:rPr><w:rStyle w:val=\"FootnoteReference\"/></w:rPr><w:footnoteReference w:id=\"").Append(f.Number).Append("\"/></w:r>");
                        break;
                    case LineBreakInline:
                        sb.Append("<w:r><w:br/></w:r>");
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
