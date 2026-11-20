using System.Collections.Concurrent;

using KScript.Projects;

namespace KScript.Runtime
{
    /// <summary>
    /// The project's defaults for built-in types, from the "types" section of the nearest kscript.json:
    /// <c>"types": { "uuid": { "v": 4 }, "password": { "min": 12 } }</c>. They apply wherever the type is used
    /// without arguments of its own.
    /// </summary>
    internal static class TypeDefaults
    {
        private sealed record Entry(string File, DateTime Modified, IReadOnlyDictionary<string, TypeRules> Types);

        private static readonly ConcurrentDictionary<string, Entry> Cache = new ConcurrentDictionary<string, Entry>(StringComparer.Ordinal);

        private static readonly IReadOnlyDictionary<string, TypeRules> None = new Dictionary<string, TypeRules>();

        /// <summary>The defaults for <paramref name="type"/> in the project <paramref name="module"/> belongs to, or null.</summary>
        public static TypeRules For(Module module, string type)
        {
            string dir = module?.Directory;
            if (dir == null) return null;
            Entry entry = Cache.TryGetValue(dir, out Entry cached) && Fresh(cached) ? cached : Cache[dir] = Load(dir);
            return entry.Types.TryGetValue(type, out TypeRules rules) ? rules : null;
        }

        private static bool Fresh(Entry entry) => entry.File == null || (File.Exists(entry.File) && File.GetLastWriteTimeUtc(entry.File) == entry.Modified);

        private static Entry Load(string dir)
        {
            string file = ProjectConfig.Find(dir);
            if (file == null) return new Entry(null, default, None);
            DateTime modified = File.GetLastWriteTimeUtc(file);
            ProjectConfig config;
            try
            {
                config = ProjectConfig.Load(file);
            }
            catch (ProjectConfigException)
            {
                // The project's own commands report a broken kscript.json.
                return new Entry(file, modified, None);
            }
            // ProjectConfig has already dropped (with a warning) any types it couldn't read.
            var types = config.Types.ToDictionary(kv => kv.Key, kv => TypeRules.FromOptions(kv.Value, out _), StringComparer.Ordinal);
            return new Entry(file, modified, types);
        }
    }
}
