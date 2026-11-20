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
