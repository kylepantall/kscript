using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace KScript
{
    /// <summary>
    /// Finds out whether this kscript is a program built by <c>kscript compile</c>. Such a program's runtimeconfig.json
    /// names its manifest, and the .NET host extracts the program's files (its scripts, assets and manifest) to a folder
    /// it adds to NATIVE_DLL_SEARCH_DIRECTORIES. Plain kscript has neither, so the check costs one lookup.
    /// </summary>
    internal static class CompiledApp
    {
        /// <summary>Must match KScript.Compile.ProgramPackager.ConfigProperty; kscript compile also looks for this
        /// text in KScript.dll to tell that a runtime can run programs.</summary>
        public const string ConfigProperty = "KScript.CompiledApp";

        public static bool IsCompiled => AppContext.GetData(ConfigProperty) is string;

        public static string FindManifest() =>
            FindManifest(AppContext.GetData(ConfigProperty) as string, AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") as string);

        /// <summary>The manifest under one of the search folders, or null when it isn't in any.</summary>
        internal static string FindManifest(string relative, string searchDirectories)
        {
            if (string.IsNullOrEmpty(relative) || string.IsNullOrEmpty(searchDirectories)) return null;
            foreach (string dir in searchDirectories.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = Path.Combine(dir, relative);
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
            return null;
        }

        internal sealed record Manifest(string Name, string Kind, string Main);

        public static Manifest Read(string path)
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = doc.RootElement;
            string main = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path), root.GetProperty("main").GetString()));
            return new Manifest(root.GetProperty("name").GetString(), root.GetProperty("kind").GetString(), main);
        }

        /// <summary>Arguments the program was opened with, without the ones macOS adds (-psn_…).</summary>
        public static string[] ScriptArguments(string[] args) => args.Where(a => !a.StartsWith("-psn_", StringComparison.Ordinal)).ToArray();
    }
}
