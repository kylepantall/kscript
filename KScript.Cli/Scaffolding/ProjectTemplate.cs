using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using KScript.Projects;

namespace KScript.Scaffolding
{
    /// <summary>An option a project template takes, from its kscript-template.json.</summary>
    public sealed class ProjectTemplateOption
    {
        public string Name { get; init; }
        public string Default { get; init; }
        public string Description { get; init; }
        /// <summary>What the setup wizard asks for it ("question"), or null to ask with the description.</summary>
        public string Question { get; init; }
        /// <summary>The values the option may take ("choices" in kscript-template.json), or empty for any value.</summary>
        public IReadOnlyList<string> Choices { get; init; } = Array.Empty<string>();
        /// <summary>True for a yes/no option: its default is true or false.</summary>
        public bool IsFlag => Default is "true" or "false";
    }

    /// <summary>
    /// The files <c>kscript make project</c> copies: the built-in template (KScript.Cli/Scaffolding/Project), or a folder
    /// of the same shape. Every file is a template with {{ }} placeholders. A file may start with a <c>--- make</c>
    /// header: <c>when: desktop</c> (or <c>unless: desktop</c>) copies it only when that option is on (or off), and
    /// <c>to: path</c> gives it another name. A name ending in <c>.stub</c> loses the ending, so <c>kscript.json.stub</c>
    /// becomes <c>kscript.json</c> without tools reading the template as a real project file.
    /// <c>kscript-template.json</c> describes the template: its options and their defaults, and the "Next:" lines.
    /// </summary>
    public sealed class ProjectTemplate
    {
        public const string ManifestName = "kscript-template.json";
        public const string FolderName = "project";
        public const string StubExtension = ".stub";

        /// <summary>The folder the template was read from, or null for the built-in one.</summary>
        public string Folder { get; init; }
        public bool BuiltIn => Folder == null;
        /// <summary>The template's name (web, bare, or the name it was asked for by), or null for a --template folder.</summary>
        public string Name { get; init; }
        public string Description { get; init; }
        public IReadOnlyList<ProjectTemplateOption> Options { get; init; } = Array.Empty<ProjectTemplateOption>();
        /// <summary>Lines printed under "Next:" once the project is made (templates themselves).</summary>
        public IReadOnlyList<string> Next { get; init; } = Array.Empty<string>();
        /// <summary>
        /// kscript make kits run in the new project once its files are written ("make" in kscript-template.json):
        /// lines such as "{{#if auth}}auth{{/if}}", left out when they come out empty. Only auth is known.
        /// </summary>
        public IReadOnlyList<string> Makes { get; init; } = Array.Empty<string>();
        /// <summary>Notes printed once the project is made ("notes"), such as what an option can't do yet.</summary>
        public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
        /// <summary>The template files by path relative to the template folder, with / separators.</summary>
        public IReadOnlyList<(string Path, string Source)> Files { get; init; }

        // ---- finding a template -----------------------------------------------------------------------------------

        /// <summary>
        /// The user's own templates: $KSCRIPT_TEMPLATES, or ~/.kscript/templates. <c>kscript make templates project</c>
        /// copies the built-in project template to its project/ folder when run outside a project (or with --global).
        /// </summary>
        public static string UserTemplatesFolder()
        {
            string env = Environment.GetEnvironmentVariable("KSCRIPT_TEMPLATES");
            if (!string.IsNullOrWhiteSpace(env)) return Path.GetFullPath(env);
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kscript", "templates");
        }

        /// <summary>The built-in template used when none is named.</summary>
        public const string DefaultName = "web";

        /// <summary>The folder a named template lives in under _templates or the user's templates: project for the default, project-bare for bare.</summary>
        public static string FolderFor(string name) =>
            string.IsNullOrEmpty(name) || string.Equals(name, DefaultName, StringComparison.OrdinalIgnoreCase) ? FolderName : FolderName + "-" + name.ToLowerInvariant();

        /// <summary>
        /// The template for a new project in <paramref name="projectDir"/>. <paramref name="templateOption"/> (--template)
        /// is a folder (a path, relative to the current folder) or a template's name, such as bare; without it the
        /// default (web) template is used. A name is looked up as _templates/project[-name] in the project around the new
        /// one (for projects made inside a project, such as a monorepo), then the user's templates/project[-name]
        /// folder, then the built-in templates.
        /// </summary>
        public static ProjectTemplate Find(string projectDir, string templateOption)
        {
            string name = string.IsNullOrWhiteSpace(templateOption) ? DefaultName : templateOption.Trim();
            bool looksLikePath = name.Contains('/') || name.Contains('\\') || name.StartsWith('.') || Path.IsPathRooted(name);
            if (looksLikePath || (!BuiltInNames().Contains(name, StringComparer.OrdinalIgnoreCase) && Directory.Exists(name)))
            {
                string folder = Path.GetFullPath(name);
                if (Directory.Exists(folder)) return FromFolder(folder);
                throw new StubException($"--template: there is no template folder at '{templateOption}'.");
            }

            string folderName = FolderFor(name);
            string around = ProjectConfig.Find(Path.GetDirectoryName(Path.GetFullPath(projectDir)));
            if (around != null)
            {
                string siteRoot = TryLoad(around)?.SiteRoot ?? Path.GetDirectoryName(around);
                string inProject = Path.Combine(siteRoot, Scaffolder.TemplatesFolder, folderName);
                if (Directory.Exists(inProject)) return FromFolder(inProject, name);
            }

            string user = Path.Combine(UserTemplatesFolder(), folderName);
            if (Directory.Exists(user)) return FromFolder(user, name);
            if (BuiltInNames().Contains(name, StringComparer.OrdinalIgnoreCase)) return BuiltInTemplate(name);
            throw new StubException($"There is no '{name}' project template. Built in: {string.Join(", ", BuiltInNames())}. " +
                                    $"Add your own as ~/.kscript/templates/{folderName}/, or give --template a folder.");
        }

                private static ProjectConfig TryLoad(string file)
        {
            try { return ProjectConfig.Load(file); }
            catch (ProjectConfigException) { return null; }
        }

        private const string ResourcePrefix = "KScript.Scaffolding.Projects/";
        private static readonly Dictionary<string, ProjectTemplate> builtIns = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The names of the built-in project templates: web (the default) first.</summary>
        public static IReadOnlyList<string> BuiltInNames() =>
            BuiltInResources().Select(r => r.Template).Distinct(StringComparer.Ordinal)
                .OrderBy(n => n == DefaultName ? 0 : 1).ThenBy(n => n, StringComparer.Ordinal).ToList();

        /// <summary>Every embedded project template file: its template's name, its path inside it, and its text.</summary>
        private static IEnumerable<(string Template, string Path, string Resource)> BuiltInResources() =>
            typeof(ProjectTemplate).Assembly.GetManifestResourceNames()
                // MSBuild writes %(RecursiveDir) with \ on Windows.
                .Select(n => (Name: n, Normal: n.Replace('\\', '/')))
                .Where(n => n.Normal.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.Normal.IndexOf('/', ResourcePrefix.Length) > 0)
                .Select(n =>
                {
                    string rest = n.Normal.Substring(ResourcePrefix.Length);
                    int slash = rest.IndexOf('/');
                    // dot-vscode is .vscode: MSBuild globs skip dot folders, so the repo spells them dot-x.
                    string rel = string.Join("/", rest.Substring(slash + 1).Split('/')
                        .Select(s => s.StartsWith("dot-", StringComparison.Ordinal) ? "." + s.Substring(4) : s));
                    return (rest.Substring(0, slash), rel, n.Name);
                });

        private static List<(string Path, string Source)> BuiltInFiles(string name)
        {
            var asm = typeof(ProjectTemplate).Assembly;
            return BuiltInResources()
                .Where(r => string.Equals(r.Template, name, StringComparison.OrdinalIgnoreCase))
                .Select(r =>
                {
                    using var stream = asm.GetManifestResourceStream(r.Resource)!;
                    using var reader = new StreamReader(stream);
                    return (r.Path, reader.ReadToEnd().Replace("\r\n", "\n"));
                })
                .OrderBy(f => f.Path, StringComparer.Ordinal)
                .ToList();
        }

        public static ProjectTemplate BuiltInTemplate(string name = DefaultName)
        {
            lock (builtIns)
            {
                if (builtIns.TryGetValue(name, out var cached)) return cached;
                var files = BuiltInFiles(name);
                if (files.Count == 0) throw new StubException($"There is no built-in '{name}' project template. Built in: {string.Join(", ", BuiltInNames())}.");
                return builtIns[name] = Build(null, files, BuiltInNames().First(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)));
            }
        }

        public static ProjectTemplate FromFolder(string folder, string name = null)
        {
            folder = Path.GetFullPath(folder);
            var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(folder, f).Replace('\\', '/'))
                .Where(rel => !rel.Split('/').Any(s => s == ".git" || s == ".DS_Store"))
                .OrderBy(rel => rel, StringComparer.Ordinal)
                .Select(rel => (rel, File.ReadAllText(Path.Combine(folder, rel)).Replace("\r\n", "\n")))
                .ToList();
            if (files.Count == 0) throw new StubException($"The project template at '{folder}' has no files.");
            return Build(folder, files, name);
        }

        private static ProjectTemplate Build(string folder, List<(string Path, string Source)> files, string name)
        {
            var manifest = files.FirstOrDefault(f => f.Path == ManifestName);
            files = files.Where(f => f.Path != ManifestName).ToList();
            string description = null;
            var options = new List<ProjectTemplateOption>();
            var next = new List<string>();
            var makes = new List<string>();
            var notes = new List<string>();

            if (manifest.Source != null)
            {
                string where = folder == null ? ManifestName : Path.Combine(folder, ManifestName);
                JsonDocument doc;
                try
                {
                    doc = JsonDocument.Parse(manifest.Source, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                }
                catch (JsonException ex)
                {
                    throw new StubException($"{where}: {ex.Message}");
                }
                using (doc)
                {
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) throw new StubException($"{where} should be an object: {{ \"description\": …, \"options\": {{ … }} }}.");
                    if (root.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String) description = d.GetString();
                    if (root.TryGetProperty("options", out var o) && o.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var p in o.EnumerateObject())
                        {
                            string def = null, about = null, question = null;
                            var choices = new List<string>();
                            if (p.Value.ValueKind == JsonValueKind.Object)
                            {
                                if (p.Value.TryGetProperty("default", out var v)) def = Scalar(v);
                                if (p.Value.TryGetProperty("description", out var a) && a.ValueKind == JsonValueKind.String) about = a.GetString();
                                if (p.Value.TryGetProperty("question", out var q) && q.ValueKind == JsonValueKind.String) question = q.GetString();
                                if (p.Value.TryGetProperty("choices", out var ch))
                                {
                                    if (ch.ValueKind != JsonValueKind.Array) throw new StubException($"{where}: {p.Name}.choices should be a list of values, such as [\"sqlite\", \"postgres\"].");
                                    choices.AddRange(ch.EnumerateArray().Select(Scalar).Where(x => x != null));
                                }
                            }
                            else def = Scalar(p.Value);
                            options.Add(new ProjectTemplateOption { Name = p.Name, Default = def, Description = about, Question = question, Choices = choices });
                        }
                    }
                    if (root.TryGetProperty("next", out var n) && n.ValueKind == JsonValueKind.Array)
                        next.AddRange(n.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()));
                    if (root.TryGetProperty("make", out var mk) && mk.ValueKind == JsonValueKind.Array)
                        makes.AddRange(mk.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()));
                    if (root.TryGetProperty("notes", out var nt) && nt.ValueKind == JsonValueKind.Array)
                        notes.AddRange(nt.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()));
                }
            }

            return new ProjectTemplate { Name = name, Folder = folder, Description = description, Options = options, Next = next, Makes = makes, Notes = notes, Files = files };
        }

        private static string Scalar(JsonElement v) => v.ValueKind switch
        {
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => v.GetString(),
            _ => v.GetRawText(),
        };

        // ---- rendering ---------------------------------------------------------------------------------------------

        /// <summary>The files this template makes with these values, by path in the new project.</summary>
        public Dictionary<string, string> Render(IReadOnlyDictionary<string, string> values)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (path, source) in Files)
            {
                var (header, body) = Scaffolder.SplitHeader(source, path);
                if (header.TryGetValue("when", out var when) && !StubTemplate.Holds(when, values)) continue;
                if (header.TryGetValue("unless", out var unless) && StubTemplate.Holds(unless, values)) continue;

                string to = header.TryGetValue("to", out var t) ? StubTemplate.Render(t, values).Trim() : path;
                if (to.EndsWith(StubExtension, StringComparison.OrdinalIgnoreCase)) to = to.Substring(0, to.Length - StubExtension.Length);
                to = to.Replace('\\', '/').TrimStart('/');
                if (to.Length == 0 || to.Split('/').Any(s => s == ".." || s.Length == 0) || Path.IsPathRooted(to))
                    throw new StubException($"{path}: '{to}' is not a path inside the project.");
                string content = StubTemplate.Render(body, values);
                (to, content) = ScriptFormat.Apply(path, to, content, Lookup(values, "format"));
                if (result.ContainsKey(to))
                    throw new StubException($"The project template makes {to} twice. Give one of them a 'when:' so only one is copied.");
                result[to] = content;
            }
            return result;
        }

        /// <summary>Lines of a manifest list (next, make, notes) with these values, leaving out the ones that come out empty.</summary>
        public static IReadOnlyList<string> RenderLines(IEnumerable<string> lines, IReadOnlyDictionary<string, string> values) =>
            lines.Select(line => StubTemplate.Render(line, values).TrimEnd()).Where(line => line.Trim().Length > 0).ToList();

        /// <summary>The "Next:" lines with these values, leaving out the ones that come out empty.</summary>
        public IReadOnlyList<string> RenderNext(IReadOnlyDictionary<string, string> values) =>
            Next.Select(line => StubTemplate.Render(line, values).TrimEnd()).Where(line => line.Trim().Length > 0).ToList();

        private static string Lookup(IReadOnlyDictionary<string, string> values, string name) =>
            values.TryGetValue(name.Trim(), out var v) ? v : null;

        // ---- publishing --------------------------------------------------------------------------------------------

        /// <summary>
        /// Copies the built-in project template into <paramref name="folder"/> so it can be changed. Files already there
        /// are kept unless <paramref name="force"/>. Returns the files written, relative to the folder.
        /// </summary>
        public static IReadOnlyList<string> Publish(string folder, bool force, string name = DefaultName)
        {
            // Every file, the manifest included.
            var all = BuiltInFiles(name);
            if (all.Count == 0) throw new StubException($"There is no built-in '{name}' project template. Built in: {string.Join(", ", BuiltInNames())}.");

            var written = new List<string>();
            foreach (var (rel, source) in all.OrderBy(f => f.Path, StringComparer.Ordinal))
            {
                string full = Path.Combine(folder, rel);
                if (File.Exists(full) && !force) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, source);
                written.Add(rel);
            }
            return written;
        }
    }
}
