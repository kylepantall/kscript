using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using KScript.Projects;

namespace KScript.Scaffolding
{
    public sealed class ProjectResult
    {
        /// <summary>The project folder.</summary>
        public string Directory { get; init; }
        /// <summary>The folder as the user gave it, for messages (e.g. "my-site" or ".").</summary>
        public string DisplayDirectory { get; init; }
        public string Name { get; init; }
        /// <summary>Files written (or that would be), relative to the project folder, with / separators.</summary>
        public IReadOnlyList<string> Files { get; init; }
        /// <summary>The rendered files by relative path (for --dry-run).</summary>
        public IReadOnlyDictionary<string, string> Contents { get; init; }
        /// <summary>Something worth telling the user, or null.</summary>
        public string Note { get; init; }
    }

    /// <summary>
    /// <c>kscript make project &lt;name&gt;</c>: creates a new KScript project folder with a kscript.json (serve and lint
    /// settings), a layout, a component, two pages, a script and a VS Code recommendation for the KScript extension.
    /// The files come from KScript/Scaffolding/Project and use the same {{ }} placeholders as other templates.
    /// </summary>
    public static class ProjectScaffolder
    {
        private const string Prefix = "KScript.Scaffolding.Project/";

        public static ProjectResult Create(string parent, string name, IDictionary<string, string> options = null, bool force = false, bool dryRun = false)
        {
            string raw = (name ?? "").Trim().Replace('\\', '/').TrimEnd('/');
            if (raw.Length == 0)
                throw new StubException("Give the project a folder name, e.g. kscript make project my-site (or . for the current folder).");

            string baseDir = Path.GetFullPath(string.IsNullOrEmpty(parent) ? "." : parent);
            string dir = Path.GetFullPath(Path.Combine(baseDir, raw));
            string folderName = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar));
            string kebab = StubNames.Kebab(folderName);
            if (kebab.Length == 0)
                throw new StubException($"'{name}' has no letters or digits to name the project after.");

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["type"] = "project",
                ["input"] = raw,
                ["name"] = folderName,
                ["kebab"] = kebab,
                ["snake"] = StubNames.Snake(folderName),
                ["pascal"] = StubNames.Pascal(folderName),
                ["camel"] = StubNames.Camel(folderName),
                ["title"] = StubNames.Title(folderName),
            };
            foreach (var kv in options ?? new Dictionary<string, string>())
                values[kv.Key.Replace('-', '_')] = kv.Value;

            if (values.TryGetValue("port", out string port) && !(int.TryParse(port, out int p) && p > 0 && p < 65536))
                throw new StubException($"--port should be a number from 1 to 65535, not '{port}'.");

            var files = TemplateFiles();
            var rendered = files.ToDictionary(f => f.Path, f => StubTemplate.Render(f.Source, values), StringComparer.Ordinal);

            if (!force)
            {
                var existing = rendered.Keys.Where(r => File.Exists(Path.Combine(dir, r))).ToList();
                if (existing.Count > 0)
                    throw new StubException($"{Display(raw)} already has {string.Join(", ", existing)}. Use --force to replace {(existing.Count == 1 ? "it" : "them")}.");
            }
            if (!force && File.Exists(dir))
                throw new StubException($"{Display(raw)} is a file, not a folder.");

            string outer = ProjectConfig.Find(Path.GetDirectoryName(dir));

            if (!dryRun)
            {
                foreach (var (rel, content) in rendered)
                {
                    string full = Path.Combine(dir, rel);
                    System.IO.Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                    File.WriteAllText(full, content);
                }
            }

            return new ProjectResult
            {
                Directory = dir,
                DisplayDirectory = raw,
                Name = kebab,
                Files = rendered.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList(),
                Contents = rendered,
                Note = outer == null ? null : $"Note: this project is inside another one ({outer}). Its own kscript.json applies to its files.",
            };
        }

        private static string Display(string raw) => raw == "." ? "This folder" : raw;

        private static List<(string Path, string Source)> cache;

        /// <summary>The project files, by path relative to the project folder (dot-vscode/ is written as .vscode/).</summary>
        internal static IReadOnlyList<(string Path, string Source)> TemplateFiles()
        {
            if (cache != null) return cache;
            var asm = typeof(ProjectScaffolder).Assembly;
            cache = asm.GetManifestResourceNames()
                // MSBuild writes %(RecursiveDir) with \ on Windows.
                .Where(n => n.Replace('\\', '/').StartsWith(Prefix, StringComparison.Ordinal))
                .Select(n =>
                {
                    using var stream = asm.GetManifestResourceStream(n)!;
                    using var reader = new StreamReader(stream);
                    string rel = string.Join("/", n.Replace('\\', '/').Substring(Prefix.Length).Split('/').Select(s => s.StartsWith("dot-", StringComparison.Ordinal) ? "." + s.Substring(4) : s));
                    return (rel, reader.ReadToEnd().Replace("\r\n", "\n"));
                })
                .OrderBy(f => f.rel, StringComparer.Ordinal)
                .ToList();
            return cache;
        }
    }
}
