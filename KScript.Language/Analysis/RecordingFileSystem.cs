using KScript.Runtime.Dependencies;

namespace KScript.Language.Analysis
{
    /// <summary>
    /// A file system that notes everything an analysis looked at: each file it read (by fingerprint), each file and
    /// folder it asked about, each folder it listed, and, for files it only searched for a name, what each search
    /// found. When none of that has changed, the analysis would come out the same, so <see cref="CheckCache"/> can
    /// reuse it.
    /// </summary>
    public sealed class RecordingFileSystem : IFileSystem
    {
        private readonly IFileSystem inner;

        public RecordingFileSystem(IFileSystem inner = null) => this.inner = inner ?? DiskFileSystem.Instance;

        /// <summary>Each file read, with the fingerprint of its text (null when it couldn't be read).</summary>
        public Dictionary<string, string> Reads { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Each path asked about: "f" + path for a file, "d" + path for a folder, with the answer.</summary>
        public Dictionary<string, bool> Exists { get; } = new Dictionary<string, bool>(StringComparer.Ordinal);

        /// <summary>Each listing ("f|folder|pattern" or "d|folder"), with a fingerprint of the names it gave.</summary>
        public Dictionary<string, string> Lists { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>For files only searched: each name searched for, and whether it was there.</summary>
        public Dictionary<string, Dictionary<string, bool>> Scans { get; } = new Dictionary<string, Dictionary<string, bool>>(StringComparer.Ordinal);

        public bool FileExists(string path) => Note("f" + path, inner.FileExists(path));

        public bool DirectoryExists(string path) => Note("d" + path, inner.DirectoryExists(path));

        public string ReadText(string path)
        {
            string text = inner.ReadText(path);
            if (path != null) Reads[path] = text == null ? null : SourceFingerprint.Hash(text);
            return text;
        }

        public IEnumerable<string> ListFiles(string directory, string pattern)
        {
            List<string> names = inner.ListFiles(directory, pattern).ToList();
            Lists[$"f|{directory}|{pattern}"] = ListHash(names);
            return names;
        }

        public IEnumerable<string> ListDirectories(string directory)
        {
            List<string> names = inner.ListDirectories(directory).ToList();
            // kscript's own folder (where this cache lives) appearing doesn't change what a check finds.
            Lists[$"d|{directory}"] = ListHash(names.Where(n => Path.GetFileName(n) != Projects.ProjectConfig.GeneratedFolder));
            return names;
        }

        public string ReadToScan(string path) => inner.ReadToScan(path);

        public void Scanned(string path, string name, bool found)
        {
            if (path == null) return;
            if (!Scans.TryGetValue(path, out Dictionary<string, bool> names)) Scans[path] = names = new Dictionary<string, bool>(StringComparer.Ordinal);
            names[name] = found;
        }

        public void Used(string path, string text)
        {
            if (path != null) Reads[path] = text == null ? null : SourceFingerprint.Hash(text);
        }

        internal static string ListHash(IEnumerable<string> names) => SourceFingerprint.Hash(string.Join("\n", names.OrderBy(n => n, StringComparer.Ordinal)));

        private bool Note(string key, bool answer)
        {
            Exists[key] = answer;
            return answer;
        }
    }
}
