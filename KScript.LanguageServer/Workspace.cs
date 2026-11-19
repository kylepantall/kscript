using KScript.Language.Analysis;
using KScript.Language.Markup;

namespace KScript.LanguageServer
{
    public sealed class OpenDocument
    {
        public string Uri { get; set; }
        public string Path { get; set; }
        public string LanguageId { get; set; }
        public string Text { get; set; }
        public int Version { get; set; }
        public DocumentAnalysis Analysis { get; set; }

        public DocumentKind Kind => Path != null ? MarkupParser.KindFromPath(Path) : LanguageId switch
        {
            "html" => DocumentKind.Page,
            "markdown" => DocumentKind.Markdown,
            _ => DocumentKind.Script,
        };
    }

    /// <summary>The documents open in the editor, layered over the disk so imports and components see unsaved edits.</summary>
    public sealed class Workspace : IFileSystem
    {
        private readonly Dictionary<string, OpenDocument> documents = new Dictionary<string, OpenDocument>(StringComparer.Ordinal);
        private readonly IFileSystem disk;

        public Workspace(IFileSystem disk = null) => this.disk = disk ?? DiskFileSystem.Instance;

        public IEnumerable<OpenDocument> Documents => documents.Values;

        public OpenDocument Get(string uri) => uri != null && documents.TryGetValue(uri, out OpenDocument d) ? d : null;

        public OpenDocument Open(string uri, string languageId, string text, int version)
        {
            OpenDocument doc = new OpenDocument { Uri = uri, Path = UriToPath(uri), LanguageId = languageId, Text = text, Version = version };
            documents[uri] = doc;
            return doc;
        }

        public void Close(string uri) => documents.Remove(uri);

        private OpenDocument ByPath(string path) =>
            path == null ? null : documents.Values.FirstOrDefault(d => d.Path != null && string.Equals(System.IO.Path.GetFullPath(d.Path), System.IO.Path.GetFullPath(path), PathComparison));

        private static StringComparison PathComparison => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        public bool FileExists(string path) => ByPath(path) != null || disk.FileExists(path);

        public bool DirectoryExists(string path) => disk.DirectoryExists(path);

        public string ReadText(string path) => ByPath(path)?.Text ?? disk.ReadText(path);

        public IEnumerable<string> ListFiles(string directory, string pattern) => disk.ListFiles(directory, pattern);

        public static string UriToPath(string uri)
        {
            if (uri == null || !uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            try
            {
                return new Uri(uri).LocalPath;
            }
            catch (UriFormatException)
            {
                return null;
            }
        }

        public static string PathToUri(string path) => new Uri(System.IO.Path.GetFullPath(path)).AbsoluteUri;
    }
}
