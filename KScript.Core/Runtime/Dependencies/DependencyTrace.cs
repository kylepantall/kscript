using KScript.Syntax.Markup;

namespace KScript.Runtime.Dependencies
{
    /// <summary>A declared value that knows where it was declared, so reaching it can be recorded.</summary>
    public interface IDeclaredSource
    {
        /// <summary>The script it was declared in.</summary>
        Module DeclaredIn { get; }

        /// <summary>An offset inside its declaration.</summary>
        int DeclaredAt { get; }
    }

    /// <summary>
    /// What some code reached while it ran: for each file, the declarations it reached (<c>fn:greet</c>) and
    /// <see cref="SourceFingerprint.Rest"/> when it loaded the file (or read a file that isn't a script).
    /// </summary>
    public sealed class DependencySet
    {
        private readonly object gate = new object();
        private readonly Dictionary<string, HashSet<string>> parts = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        public void Add(string path, string key)
        {
            lock (gate)
            {
                if (!parts.TryGetValue(path, out HashSet<string> keys)) parts[path] = keys = new HashSet<string>(StringComparer.Ordinal);
                keys.Add(key);
            }
        }

        /// <summary>Adds everything another set reached.</summary>
        public void AddAll(DependencySet other)
        {
            if (other == null || ReferenceEquals(other, this)) return;
            foreach ((string path, IReadOnlyCollection<string> keys) in other.Files)
            {
                foreach (string key in keys) Add(path, key);
            }
        }

        /// <summary>The files reached, with the parts of each.</summary>
        public IReadOnlyDictionary<string, IReadOnlyCollection<string>> Files
        {
            get
            {
                lock (gate)
                {
                    return parts.ToDictionary(p => p.Key, p => (IReadOnlyCollection<string>)p.Value.OrderBy(k => k, StringComparer.Ordinal).ToList(), StringComparer.Ordinal);
                }
            }
        }
    }

    /// <summary>
    /// Records what running code reaches, for <c>kscript test</c> to rerun only the tests a change affects. Set
    /// <see cref="Current"/> and run: the interpreter notes each script it loads, each function and method it calls,
    /// each declared type it uses, and each file packages read on its behalf. With no current set, nothing is recorded.
    /// The set flows to threads the code starts.
    /// </summary>
    public static class DependencyTrace
    {
        private static readonly AsyncLocal<DependencySet> current = new AsyncLocal<DependencySet>();

        /// <summary>Where what runs now is recorded, or null.</summary>
        public static DependencySet Current
        {
            get => current.Value;
            set => current.Value = value;
        }

        /// <summary>A script was loaded: its imports, constants and top-level code are now in play.</summary>
        public static void Loaded(Module module)
        {
            if (current.Value is DependencySet set && UserFile(module?.Path)) set.Add(module.Path, SourceFingerprint.Rest);
        }

        /// <summary>A file other than a script was read, such as a page template, a config file or test data.</summary>
        public static void Read(string path)
        {
            if (current.Value is DependencySet set && !string.IsNullOrEmpty(path))
            {
                string full = System.IO.Path.GetFullPath(path);
                if (UserFile(full)) set.Add(full, SourceFingerprint.Rest);
            }
        }

        /// <summary>Reads a file's text, noting that it was read.</summary>
        public static string ReadText(string path)
        {
            Read(path);
            return File.ReadAllText(path);
        }

        /// <summary>Reads a file's lines, noting that it was read.</summary>
        public static string[] ReadLines(string path)
        {
            Read(path);
            return File.ReadAllLines(path);
        }

        /// <summary>Reads a file's bytes, noting that it was read.</summary>
        public static byte[] ReadBytes(string path)
        {
            Read(path);
            return File.ReadAllBytes(path);
        }

        /// <summary>Code ran inside the top-level declaration of <paramref name="module"/> that holds <paramref name="offset"/>.</summary>
        public static void Reached(Module module, int offset)
        {
            if (current.Value is not DependencySet set || !UserFile(module?.Path) || module.Root == null) return;
            if (module.DeclarationAt(offset) is string key) set.Add(module.Path, key);
        }

        /// <summary>A declared value (a type, model, enum, error or job) was used.</summary>
        public static void Reached(object declared)
        {
            if (current.Value != null && declared is IDeclaredSource source && source.DeclaredIn != null) Reached(source.DeclaredIn, source.DeclaredAt);
        }

        private static bool UserFile(string path) => path != null && !PackageSource.IsSource(path);
    }
}
