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
    /// desktop window that kscript compile builds into a program. Every file comes from the template, so a project's
    /// _templates/project or ~/.kscript/templates/project (made by kscript make templates project) changes them.
    /// </summary>
    public static class ProjectScaffolder
    {
        /// <summary>"a, b or c".</summary>
        internal static string Either(IReadOnlyList<string> choices) =>
            choices.Count == 1 ? choices[0] : string.Join(", ", choices.Take(choices.Count - 1)) + " or " + choices[^1];

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
            // --bare is short for --template bare.
            if (given.Remove("bare", out string bare) && StubTemplate.IsSet(bare))
            {
                if (templateOption != null) throw new StubException("Use --bare or --template, not both.");
                templateOption = "bare";
            }
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

            // Scripts are .kscript unless --syntax xml, or the project this one is made inside says otherwise.
            string outerConfig = ProjectConfig.Find(Path.GetDirectoryName(dir));
            ProjectConfig outerProject = null;
            if (outerConfig != null) { try { outerProject = ProjectConfig.Load(outerConfig); } catch (ProjectConfigException) { } }
            var formatOptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (given.TryGetValue(ScriptFormat.Option, out string givenFormat)) formatOptions[ScriptFormat.Option] = givenFormat;
            else if (outerProject?.Make.Format == null && values.TryGetValue(ScriptFormat.Option, out string templateFormat)) formatOptions[ScriptFormat.Option] = templateFormat;
            ScriptFormat.AddValues(values, ScriptFormat.Resolve(formatOptions, outerProject));

            if (given.TryGetValue("port", out string port) && !(int.TryParse(port, out int p) && p > 0 && p < 65536))
                throw new StubException($"--port should be a number from 1 to 65535, not '{port}'.");

            // An option with choices (--db) takes one of them.
            foreach (var o in template.Options.Where(o => o.Choices.Count > 0))
            {
                string key = o.Name.Replace('-', '_');
                if (!given.TryGetValue(key, out string chosen)) continue;
                string match = o.Choices.FirstOrDefault(c => string.Equals(c, chosen?.Trim(), StringComparison.OrdinalIgnoreCase));
                if (match == null)
                    throw new StubException($"--{o.Name} should be {Either(o.Choices)}, not '{chosen}'.");
                values[key] = match;
            }

            // A mistyped option (--no-dekstop), or one another template has (--docker), is refused: made as asked or not at all.
            var notes = new List<string>();
            if (template.Options.Count > 0)
            {
                var known = template.Options.Select(o => o.Name.Replace('-', '_')).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (string key in given.Keys.Where(k => !known.Contains(k)).OrderBy(k => k, StringComparer.Ordinal))
                {
                    if (key is "type" or "input" or "name" or "kebab" or "snake" or "pascal" or "camel" or "title" or ScriptFormat.Option) continue;
                    // A template may use a value it doesn't list as an option ("when: api", {{#if api}}).
                    var uses = new System.Text.RegularExpressions.Regex(@"(?:\{\{[^}]*|^(?:when|unless):.*)\b" + System.Text.RegularExpressions.Regex.Escape(key) + @"\b", System.Text.RegularExpressions.RegexOptions.Multiline);
                    if (template.Files.Any(f => uses.IsMatch(f.Source)) || template.Next.Concat(template.Makes).Concat(template.Notes).Any(uses.IsMatch)) continue;
                    string label = template.Name != null ? $"The {template.Name} project template" : "The project template";
                    throw new StubException($"{label} has no --{key.Replace('_', '-')} option. Its options: {string.Join(", ", template.Options.Select(o => "--" + o.Name))}.");
                }
            }

            var rendered = template.Render(values);
            var kits = ProjectTemplate.RenderLines(template.Makes, values).Select(line => line.Trim()).ToList();
            if (kits.RemoveAll(kit => string.Equals(kit, "docker", StringComparison.OrdinalIgnoreCase)) > 0)
            {
                var project = rendered.TryGetValue("kscript.json", out string config)
                    ? ProjectConfig.Parse(config, Path.Combine(dir, "kscript.json")) : null;
                foreach (var (path, content) in DockerMaker.Render(project))
                    if (!rendered.TryAdd(path, content)) throw new StubException($"The project template already makes the Docker kit's {path}.");
            }

            if (!force)
            {
                var existing = rendered.Keys.Where(r => File.Exists(Path.Combine(dir, r))).OrderBy(r => r, StringComparer.Ordinal).ToList();
                if (existing.Count > 0)
                    throw new StubException($"{Display(raw)} already has {string.Join(", ", existing)}. Use --force to replace {(existing.Count == 1 ? "it" : "them")}.");
            }
            if (!force && File.Exists(dir))
                throw new StubException($"{Display(raw)} is a file, not a folder.");

            string outer = outerConfig;
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

            var files = rendered.Keys.ToList();
            foreach (string line in template.Notes.Count > 0 ? ProjectTemplate.RenderLines(template.Notes, values) : Array.Empty<string>())
                notes.Add("Note: " + line.Trim());

            // Kits the template asks for ("make": ["{{#if auth}}auth{{/if}}"]), made in the new project.
            foreach (string kit in kits)
            {
                if (string.Equals(kit, "users", StringComparison.OrdinalIgnoreCase))
                {
                    if (!dryRun) files.AddRange(AuthMaker.UseUserModel(dir, values[ScriptFormat.Option]).Where(f => !files.Contains(f)));
                    continue;
                }
                if (!string.Equals(kit, "auth", StringComparison.OrdinalIgnoreCase))
                    throw new StubException($"The project template asks for 'make: {kit}', but only auth, users and docker can be made with a project.");
                if (dryRun)
                {
                    notes.Add("Note: --dry-run leaves out the sign-in files (kscript make auth), which are made once the project is.");
                    continue;
                }
                // A project without a database keeps its users in files.
                var authOptions = new Dictionary<string, string> { [ScriptFormat.Option] = values[ScriptFormat.Option] };
                if (values.TryGetValue("db", out string db) && db == "none") authOptions["users"] = "files";
                AuthResult auth = AuthMaker.Make(dir, authOptions, force, dryRun: false);
                foreach (var (_, path) in auth.Files) if (!files.Contains(path) && File.Exists(Path.Combine(dir, path))) files.Add(path);
            }

            // A template with an AGENTS.md gets its agent docs filled in from the new project's code.
            if (!dryRun && rendered.ContainsKey(global::KScript.Language.Agents.AgentDocs.AgentsFile))
            {
                files.AddRange(global::KScript.Language.Agents.AgentDocs.Write(dir).Where(f => !files.Contains(f)));
            }

            return new ProjectResult
            {
                Directory = dir,
                DisplayDirectory = raw,
                Name = kebab,
                Files = files.OrderBy(k => k, StringComparer.Ordinal).ToList(),
                Contents = rendered,
                Note = notes.Count == 0 ? null : string.Join("\n", notes),
                Template = template,
                Next = template.RenderNext(values),
            };
        }

        private static string Display(string raw) => raw == "." ? "This folder" : raw;
    }
}
