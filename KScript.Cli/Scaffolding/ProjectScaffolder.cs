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
        /// <summary>The template the project was made from.</summary>
        public ProjectTemplate Template { get; init; }
        /// <summary>What to run next, from the template.</summary>
        public IReadOnlyList<string> Next { get; init; } = Array.Empty<string>();
    }

    /// <summary>
    /// <c>kscript make project &lt;name&gt;</c>: creates a new KScript project folder from a project template (see
    /// <see cref="ProjectTemplate"/>). The built-in one is a web app with a kscript.json, a layout, a component, pages
    /// (one of them a live Tasks app), a script, tests, editor settings and, unless --no-desktop, the same Tasks app as a
    /// desktop window that kscript app build packages. Every file comes from the template, so a project's
    /// _templates/project or ~/.kscript/templates/project (made by kscript make templates project) changes them.
    /// </summary>
    public static class ProjectScaffolder
    {
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

            var given = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in options ?? new Dictionary<string, string>()) given[kv.Key.Replace('-', '_')] = kv.Value;
            given.Remove("template", out string templateOption);
            if (templateOption != null && !Path.IsPathRooted(templateOption)) templateOption = Path.Combine(Directory.GetCurrentDirectory(), templateOption);
            var template = ProjectTemplate.Find(dir, templateOption);

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
            foreach (var o in template.Options)
                if (o.Default != null) values[o.Name.Replace('-', '_')] = o.Default;
            foreach (var kv in given) values[kv.Key] = kv.Value;

            if (given.TryGetValue("port", out string port) && !(int.TryParse(port, out int p) && p > 0 && p < 65536))
                throw new StubException($"--port should be a number from 1 to 65535, not '{port}'.");

            // A mistyped option (--no-dekstop) would otherwise be silently ignored.
            var notes = new List<string>();
            if (template.Options.Count > 0)
            {
                var known = template.Options.Select(o => o.Name.Replace('-', '_')).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (string key in given.Keys.Where(k => !known.Contains(k)).OrderBy(k => k, StringComparer.Ordinal))
                {
                    if (key is "type" or "input" or "name" or "kebab" or "snake" or "pascal" or "camel" or "title") continue;
                    notes.Add($"Note: the project template has no '{key.Replace('_', '-')}' option, so --{key.Replace('_', '-')} was not used. Its options: {string.Join(", ", template.Options.Select(o => o.Name))}.");
                }
            }

            var rendered = template.Render(values);

            if (!force)
            {
                var existing = rendered.Keys.Where(r => File.Exists(Path.Combine(dir, r))).OrderBy(r => r, StringComparer.Ordinal).ToList();
                if (existing.Count > 0)
                    throw new StubException($"{Display(raw)} already has {string.Join(", ", existing)}. Use --force to replace {(existing.Count == 1 ? "it" : "them")}.");
            }
            if (!force && File.Exists(dir))
                throw new StubException($"{Display(raw)} is a file, not a folder.");

            string outer = ProjectConfig.Find(Path.GetDirectoryName(dir));
            if (outer != null) notes.Add($"Note: this project is inside another one ({outer}). Its own kscript.json applies to its files.");

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
                Note = notes.Count == 0 ? null : string.Join("\n", notes),
                Template = template,
                Next = template.RenderNext(values),
            };
        }

        private static string Display(string raw) => raw == "." ? "This folder" : raw;
    }
}
