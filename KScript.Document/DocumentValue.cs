using KScript.Documents.Export;
using KScript.Documents.Pdf;
using KScript.Documents.Text;
using KScript.Metadata;
using KScript.Runtime;

namespace KScript.Documents
{
    /// <summary>A built document, kept in a variable with into: so it can be saved or turned into text later.</summary>
    public sealed class DocumentValue : IKObject
    {
        public DocumentValue(Document document) => Document = document;

        private readonly FencedFile source;
        private readonly string sourceDirectory;

        private DocumentValue(FencedFile source, string directory)
        {
            this.source = source;
            sourceDirectory = directory;
            if (source.TypeName == KType.Markdown)
            {
                Document = new Document { BaseDirectory = directory };
                Document.Blocks.AddRange(MarkdownBlocks.Parse(source.ToText(), dollarMaths: true));
            }
        }

        /// <summary>A Markdown or LaTeX fence as a document, for the document methods a fence has itself: $notes->html().</summary>
        internal static DocumentValue FromFence(object fence, CallContext call) =>
            new DocumentValue((FencedFile)fence, call.Module?.Directory ?? call.Host.WorkingDirectory);

        /// <summary>The methods a Markdown fence shares with a document. Its own source is .text, as a LaTeX fence's is.</summary>
        internal static readonly string[] MarkdownOutputs = { "latex", "html", "pages", "words" };

        /// <summary><c>$fence->save(path, as?)</c>: the source as it is for .md or .tex (or no extension), else rendered.</summary>
        internal static object SaveFence(object fence, CallContext call)
        {
            var file = (FencedFile)fence;
            string path = call.Str(0), format = call.Str(1, null);
            if (format == null && !string.IsNullOrWhiteSpace(path) && !Path.EndsInDirectorySeparator(path) && Path.GetExtension(path).Length == 0)
                file.Save(call.Host, path, call.Module?.Directory);
            else
                FromFence(file, call).Save(call.Host, path, call.Module?.Directory, format);
            return null;
        }

        internal static object FromSource(CallContext call)
        {
            object input = call.Arg(0);
            string format = call.Str(1, null);
            FencedFile source = input switch
            {
                string text => FencedFile.Source(text, format == "latex"),
                FencedFile file when file.TypeName is KType.Markdown or KType.Latex && (format == null || format == file.TypeName) => file,
                _ => throw new KScriptException("Use a Markdown or LaTeX fence, or a string with the source format.", "Argument"),
            };
            return new DocumentValue(source, call.Module?.Directory ?? call.Host.WorkingDirectory);
        }

        public Document Document { get; }

        public string TypeName => KType.Document;

        public string ToText() => $"document '{Document?.Title ?? "untitled"}'";

        public override string ToString() => ToText();

        public bool TryGetMember(string name, out object value)
        {
            value = name switch
            {
                "title" => Document?.Title ?? string.Empty,
                "sections" => (double)(Document?.AllBlocks().OfType<Section>().Count() ?? 0),
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
            ("save", "path: string, as?: pdf|docx|latex|markdown|html", "Saves the document. The filename extension selects the format unless as is given.", KType.Null),
            ("find", "text: string", "The zero-based match offset in the document's source, or -1.", KType.Number),
            ("replace", "search: string, replacement: string", "Returns a document made from the source with every literal match replaced.", KType.Document),
            ("insert", "content, at?: int", "Returns a document with same-format source or plain text added at the given offset.", KType.Document),
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
                    Save(call.Host, call.Str(0), call.Module?.Directory, call.Str(1, null));
                    return null;
                case "find":
                    return (double)RequireSource().Find(call.Str(0));
                case "replace":
                    return new DocumentValue(RequireSource().Replace(call.Str(0), call.Str(1)), sourceDirectory);
                case "insert":
                    object content = call.Arg(0) is DocumentValue doc ? doc.RequireSource() : call.Arg(0);
                    return new DocumentValue(RequireSource().Insert(content, call.Has(1) ? call.Num(1) : null), sourceDirectory);
                case "pages":
                    return (double)PdfWriter.CountPages(RequireStructured());
                case "markdown":
                    return source?.TypeName == KType.Markdown ? source.ToText() : MarkdownWriter.Write(RequireStructured(), call.Module?.Directory);
                case "latex":
                    return source?.TypeName == KType.Latex ? source.ToText() : LatexWriter.Write(RequireStructured(), call.Module?.Directory);
                case "html":
                    return HtmlWriter.Write(RequireStructured());
                default:
                    return (double)Words();
            }
        }

        private FencedFile RequireSource() => source
            ?? throw new KScriptException("This operation needs source text supplied through @document().", "Argument");

        private Document RequireStructured() => Document
            ?? throw new KScriptException("LaTeX source can be edited or saved as LaTeX. Use a Markdown or structured document to render another format.", "Argument");

        internal int Words()
        {
            int count = 0;
            foreach (Block block in RequireStructured().AllBlocks())
            {
                count += Count(Text.InlineParser.Plain(Resolver.InlinesOf(block).Where(i => !(i is MathInline || i is CodeInline))));
            }
            return count;
        }

        private static int Count(string text) => text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Count(w => w.Any(char.IsLetterOrDigit));

        /// <summary>The formats save understands, by extension.</summary>
        internal static readonly string[] Extensions = { ".pdf", ".docx", ".tex", ".md", ".markdown", ".html", ".htm" };

        /// <summary>Writes the document to <paramref name="path"/> in the format its extension names.</summary>
        internal void Save(KScriptHost host, string path, string baseDirectory, string format = null)
        {
            if (string.IsNullOrWhiteSpace(path) || Path.EndsInDirectorySeparator(path))
                throw new KScriptException("Give the document a filename.", "Argument");
            string ext = format switch
            {
                "pdf" => ".pdf",
                "docx" => ".docx",
                "latex" => ".tex",
                "markdown" => ".md",
                "html" => ".html",
                _ => Path.GetExtension(path).ToLowerInvariant(),
            };
            if (Path.GetExtension(path).Length == 0 && format != null) path += ext;
            if (!Extensions.Contains(ext))
                throw new KScriptException($"Can't save a document as '{path}': use .pdf, .docx, .tex, .md or .html.", "Argument");
            if ((ext == ".tex" && source?.TypeName == KType.Latex) || (ext is ".md" or ".markdown" && source?.TypeName == KType.Markdown))
            {
                source.Save(host, path, baseDirectory);
                return;
            }

            RequireStructured();
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
