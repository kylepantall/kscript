namespace KScript.Language.Analysis
{
    /// <summary>File access for the analyzer (components, layouts, imports). The language server overlays open editors.</summary>
    public interface IFileSystem
    {
        bool FileExists(string path);

        bool DirectoryExists(string path);

        /// <summary>The file's text, or null if it can't be read.</summary>
        string ReadText(string path);

        IEnumerable<string> ListFiles(string directory, string pattern);

        /// <summary>The folders directly inside a folder (for path completion and project-wide checks). None by default.</summary>
        IEnumerable<string> ListDirectories(string directory) => Array.Empty<string>();

        /// <summary>
        /// A file's text read only to look for a name in it (project-wide checks scan every file this way). A
        /// <see cref="RecordingFileSystem"/> then depends on what the search found, not on the whole text.
        /// </summary>
        string ReadToScan(string path) => ReadText(path);

        /// <summary>Notes what a search of a scanned file found: whether <paramref name="name"/> is in it.</summary>
        void Scanned(string path, string name, bool found) { }

        /// <summary>Notes that a scanned file's whole text was used after all (it was analysed).</summary>
        void Used(string path, string text) { }
    }

    public sealed class DiskFileSystem : IFileSystem
    {
        public static readonly DiskFileSystem Instance = new DiskFileSystem();

        public bool FileExists(string path) => !string.IsNullOrEmpty(path) && File.Exists(path);

        public bool DirectoryExists(string path) => !string.IsNullOrEmpty(path) && Directory.Exists(path);

        public string ReadText(string path)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        public IEnumerable<string> ListFiles(string directory, string pattern)
        {
            try
            {
                return Directory.Exists(directory) ? Directory.GetFiles(directory, pattern) : Array.Empty<string>();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }
        }

        public IEnumerable<string> ListDirectories(string directory)
        {
            try
            {
                return Directory.Exists(directory) ? Directory.GetDirectories(directory) : Array.Empty<string>();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }
        }
    }

    /// <summary>
    /// Another file system, plus the scripts packages ship (KScript.Auth's ownable.kscript), at their paths under
    /// <see cref="KScript.Runtime.PackageSource.Root"/>, so they are read like any imported script.
    /// </summary>
    internal sealed class PackageSourceFiles : IFileSystem
    {
        private readonly IFileSystem inner;
        private readonly Catalog.LanguageCatalog catalog;

        public PackageSourceFiles(IFileSystem inner, Catalog.LanguageCatalog catalog)
        {
            this.inner = inner;
            this.catalog = catalog;
        }

        /// <summary>The file system with package scripts added, once.</summary>
        public static IFileSystem Over(IFileSystem files, Catalog.LanguageCatalog catalog) =>
            files is PackageSourceFiles ? files : new PackageSourceFiles(files, catalog);

        public bool FileExists(string path) => catalog.SourceAt(path) != null || inner.FileExists(path);
        public bool DirectoryExists(string path) => inner.DirectoryExists(path);
        public string ReadText(string path) => catalog.SourceAt(path)?.Text ?? inner.ReadText(path);
        public IEnumerable<string> ListFiles(string directory, string pattern) => inner.ListFiles(directory, pattern);
        public IEnumerable<string> ListDirectories(string directory) => inner.ListDirectories(directory);
        public string ReadToScan(string path) => catalog.SourceAt(path)?.Text ?? inner.ReadToScan(path);
        public void Scanned(string path, string name, bool found) => inner.Scanned(path, name, found);
        public void Used(string path, string text) => inner.Used(path, text);
    }

    /// <summary>A file system with no files, for analysing text that isn't saved anywhere.</summary>
    public sealed class EmptyFileSystem : IFileSystem
    {
        public static readonly EmptyFileSystem Instance = new EmptyFileSystem();
        public bool FileExists(string path) => false;
        public bool DirectoryExists(string path) => false;
        public string ReadText(string path) => null;
        public IEnumerable<string> ListFiles(string directory, string pattern) => Array.Empty<string>();
    }
}
