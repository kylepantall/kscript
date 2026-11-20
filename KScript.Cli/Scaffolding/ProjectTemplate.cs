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
        public string Description { get; init; }
        public IReadOnlyList<ProjectTemplateOption> Options { get; init; } = Array.Empty<ProjectTemplateOption>();
        /// <summary>Lines printed under "Next:" once the project is made (templates themselves).</summary>
        public IReadOnlyList<string> Next { get; init; } = Array.Empty<string>();
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

        /// <summary>
        /// The template for a new project in <paramref name="projectDir"/>: the --template folder if given; else
        /// _templates/project in the project around it (for projects made inside a project, such as a monorepo);
        /// else the user's templates/project folder; else the built-in template.
        /// </summary>
        public static ProjectTemplate Find(string projectDir, string templateOption)
        {
            if (!string.IsNullOrWhiteSpace(templateOption))
            {
                string folder = Path.GetFullPath(templateOption);
                if (File.Exists(Path.Combine(folder, ManifestName)) || Directory.Exists(folder)) return FromFolder(folder);
                throw new StubException($"--template: there is no template folder at '{templateOption}'.");
            }

            string around = ProjectConfig.Find(Path.GetDirectoryName(Path.GetFullPath(projectDir)));
            if (around != null)
            {
                string siteRoot = TryLoad(around)?.SiteRoot ?? Path.GetDirectoryName(around);
                string inProject = Path.Combine(siteRoot, Scaffolder.TemplatesFolder, FolderName);
                if (Directory.Exists(inProject)) return FromFolder(inProject);
            }

            string user = Path.Combine(UserTemplatesFolder(), FolderName);
            if (Directory.Exists(user)) return FromFolder(user);
            return BuiltInTemplate();
        }

        private static ProjectConfig TryLoad(string file)
        {
            try { return ProjectConfig.Load(file); }
            catch (ProjectConfigException) { return null; }
        }

        private static ProjectTemplate builtIn;

        public static ProjectTemplate BuiltInTemplate()
        {
            if (builtIn != null) return builtIn;
            const string prefix = "KScript.Scaffolding.Project/";
            var asm = typeof(ProjectTemplate).Assembly;
            var files = asm.GetManifestResourceNames()
                // MSBuild writes %(RecursiveDir) with \ on Windows.
                .Where(n => n.Replace('\\', '/').StartsWith(prefix, StringComparison.Ordinal))
                .Select(n =>
                {
                    using var stream = asm.GetManifestResourceStream(n)!;
                    using var reader = new StreamReader(stream);
                    // dot-vscode is .vscode: MSBuild globs skip dot folders, so the repo spells them dot-x.
                    string rel = string.Join("/", n.Replace('\\', '/').Substring(prefix.Length).Split('/')
                        .Select(s => s.StartsWith("dot-", StringComparison.Ordinal) ? "." + s.Substring(4) : s));
                    return (rel, reader.ReadToEnd().Replace("\r\n", "\n"));
                })
                .OrderBy(f => f.rel, StringComparer.Ordinal)
                .ToList();
            return builtIn = Build(null, files);
        }

        public static ProjectTemplate FromFolder(string folder)
        {
            folder = Path.GetFullPath(folder);
            var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(folder, f).Replace('\\', '/'))
                .Where(rel => !rel.Split('/').Any(s => s == ".git" || s == ".DS_Store"))
                .OrderBy(rel => rel, StringComparer.Ordinal)
                .Select(rel => (rel, File.ReadAllText(Path.Combine(folder, rel)).Replace("\r\n", "\n")))
                .ToList();
            if (files.Count == 0) throw new StubException($"The project template at '{folder}' has no files.");
            return Build(folder, files);
        }

        private static ProjectTemplate Build(string folder, List<(string Path, string Source)> files)
        {
            var manifest = files.FirstOrDefault(f => f.Path == ManifestName);
            files = files.Where(f => f.Path != ManifestName).ToList();
            string description = null;
            var options = new List<ProjectTemplateOption>();
            var next = new List<string>();

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
                            string def = null, about = null;
                            if (p.Value.ValueKind == JsonValueKind.Object)
                            {
                                if (p.Value.TryGetProperty("default", out var v)) def = Scalar(v);
                                if (p.Value.TryGetProperty("description", out var a) && a.ValueKind == JsonValueKind.String) about = a.GetString();
                            }
                            else def = Scalar(p.Value);
                            options.Add(new ProjectTemplateOption { Name = p.Name, Default = def, Description = about });
                        }
                    }
                    if (root.TryGetProperty("next", out var n) && n.ValueKind == JsonValueKind.Array)
                        next.AddRange(n.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()));
                }
            }

            return new ProjectTemplate { Folder = folder, Description = description, Options = options, Next = next, Files = files };
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
                if (header.TryGetValue("when", out var when) && !StubTemplate.IsSet(Lookup(values, when))) continue;
                if (header.TryGetValue("unless", out var unless) && StubTemplate.IsSet(Lookup(values, unless))) continue;

                string to = header.TryGetValue("to", out var t) ? StubTemplate.Render(t, values).Trim() : path;
                if (to.EndsWith(StubExtension, StringComparison.OrdinalIgnoreCase)) to = to.Substring(0, to.Length - StubExtension.Length);
                to = to.Replace('\\', '/').TrimStart('/');
                if (to.Length == 0 || to.Split('/').Any(s => s == ".." || s.Length == 0) || Path.IsPathRooted(to))
                    throw new StubException($"{path}: '{to}' is not a path inside the project.");
                if (result.ContainsKey(to))
                    throw new StubException($"The project template makes {to} twice. Give one of them a 'when:' so only one is copied.");
                result[to] = StubTemplate.Render(body, values);
            }
            return result;
        }

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
        public static IReadOnlyList<string> Publish(string folder, bool force)
        {
            var template = BuiltInTemplate();
            var all = template.Files.ToList();
            // The manifest comes back from the embedded resources, so it is published too.
            var asm = typeof(ProjectTemplate).Assembly;
            using (var stream = asm.GetManifestResourceStream("KScript.Scaffolding.Project/" + ManifestName))
            {
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    all.Add((ManifestName, reader.ReadToEnd().Replace("\r\n", "\n")));
                }
            }

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
