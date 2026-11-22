using KScript.Documents.Export;
using KScript.Documents.Pdf;
using KScript.Metadata;
using KScript.Runtime;

namespace KScript.Documents
{
    /// <summary>A built document, kept in a variable with into: so it can be saved or turned into text later.</summary>
    public sealed class DocumentValue : IKObject
    {
        public DocumentValue(Document document) => Document = document;

        public Document Document { get; }

        public string TypeName => KType.Document;

        public string ToText() => $"document '{Document.Title ?? "untitled"}'";

        public override string ToString() => ToText();

        public bool TryGetMember(string name, out object value)
        {
            value = name switch
            {
                "title" => Document.Title ?? string.Empty,
                "sections" => (double)Document.AllBlocks().OfType<Section>().Count(),
                _ => null,
            };
            return value != null;
        }

        /// <summary>Methods go through the package, so their arguments are bound by name first.</summary>
        public bool TryCallMethod(CallContext call, string name, out object result)
        {
            result = null;
            return false;
        }

        internal static readonly (string Name, string Type, string Description)[] Fields =
        {
            ("title", KType.String, "The document's title ('' without one)."),
            ("sections", KType.Number, "How many sections it has."),
        };

        internal static readonly (string Name, string Params, string Description, string Returns)[] Methods =
        {
            ("save", "path", "Saves the document. The extension picks the format: .pdf, .docx, .tex, .md or .html.", KType.Null),
            ("pages", "", "How many pages the PDF has.", KType.Number),
            ("markdown", "", "The document as Markdown, with maths as $...$.", KType.String),
            ("latex", "", "The document as a LaTeX file to build with pdflatex.", KType.String),
            ("html", "", "The document as one HTML page, with maths as MathML.", KType.String),
            ("words", "", "How many words the text has, not counting maths and code.", KType.Number),
        };

        internal object Invoke(CallContext call, string name)
        {
            switch (name)
            {
                case "save":
                    Save(call.Host, call.Str(0), call.Module?.Directory);
                    return null;
                case "pages":
                    return (double)PdfWriter.CountPages(Document);
                case "markdown":
                    return MarkdownWriter.Write(Document, call.Module?.Directory);
                case "latex":
                    return LatexWriter.Write(Document, call.Module?.Directory);
                case "html":
                    return HtmlWriter.Write(Document);
                default:
                    return (double)Words();
            }
        }

        internal int Words()
        {
            int count = 0;
            foreach (Block block in Document.AllBlocks())
            {
                count += Count(Text.InlineParser.Plain(Resolver.InlinesOf(block).Where(i => !(i is MathInline || i is CodeInline))));
            }
            return count;
        }

        private static int Count(string text) => text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Count(w => w.Any(char.IsLetterOrDigit));

        /// <summary>The formats save understands, by extension.</summary>
        internal static readonly string[] Extensions = { ".pdf", ".docx", ".tex", ".md", ".markdown", ".html", ".htm" };

        /// <summary>Writes the document to <paramref name="path"/> in the format its extension names.</summary>
        internal void Save(KScriptHost host, string path, string baseDirectory)
        {
            string ext = Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
            if (!Extensions.Contains(ext))
                throw new KScriptException($"Can't save a document as '{path}': use .pdf, .docx, .tex, .md or .html.", "Argument");
            host.CheckWrite("Saving a document");
            string full = host.ResolvePath(path, baseDirectory);
            string folder = Path.GetDirectoryName(full);
            Directory.CreateDirectory(folder);
            switch (ext)
            {
                case ".pdf":
                    File.WriteAllBytes(full, PdfWriter.Write(Document));
                    break;
                case ".docx":
                    File.WriteAllBytes(full, DocxWriter.Write(Document));
                    break;
                case ".tex":
                    File.WriteAllText(full, LatexWriter.Write(Document, folder));
                    break;
                case ".html":
                case ".htm":
                    File.WriteAllText(full, HtmlWriter.Write(Document));
                    break;
                default:
                    File.WriteAllText(full, MarkdownWriter.Write(Document, folder));
                    break;
            }
        }
    }
}
