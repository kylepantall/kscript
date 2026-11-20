using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace KScript.Scaffolding
{
    /// <summary>A template <c>kscript make</c> can use: built in, or a file in the project's _templates folder.</summary>
    public sealed class StubInfo
    {
        public string Type { get; init; }
        public string Description { get; init; }
        /// <summary>The template file under _templates, or null for a built-in template.</summary>
        public string FilePath { get; init; }
        public bool BuiltIn => FilePath == null;
        public bool OverridesBuiltIn { get; init; }
        internal string FileName { get; init; }
        internal string Source { get; init; }
    }

    public sealed class MakeRequest
    {
        public string Type { get; set; }
        public string Name { get; set; }
        /// <summary>The project folder: files are created here, and templates are read from its _templates folder.</summary>
        public string Root { get; set; } = ".";
        /// <summary>Values from --key value / --key=value / --flag on the command line.</summary>
        public Dictionary<string, string> Options { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public bool Force { get; set; }
        public bool DryRun { get; set; }
        /// <summary>Where to write the file, relative to the project folder, instead of the template's <c>to:</c>.</summary>
        public string To { get; set; }
    }

    public sealed class MakeResult
    {
        /// <summary>The created file, relative to the project folder, with / separators.</summary>
        public string RelativePath { get; init; }
        public string FullPath { get; init; }
        public string Content { get; init; }
        public string Hint { get; init; }
        public StubInfo Template { get; init; }
        public bool Overwrote { get; init; }
    }

    /// <summary>
    /// <c>kscript make &lt;type&gt; &lt;Name&gt;</c>: creates files from templates. Each template is a file named after its type,
    /// e.g. _templates/component.html, with an optional header saying where the file goes:
    /// <code>
    /// --- make
    /// description: A reusable piece of a page
    /// to: _components/{{ kebab }}.html
    /// hint: Use it in a page: &lt;{{ kebab }} /&gt;
    /// ---
    /// </code>
    /// A file in the project's _templates folder replaces the built-in template of the same type, and any other file there
    /// adds a new type. See <see cref="StubTemplate"/> for the placeholder syntax.
    /// </summary>
    public static class Scaffolder
    {
        public const string TemplatesFolder = "_templates";

        private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["md"] = "markdown",
            ["c"] = "component",
        };

        /// <summary>Every template available in a project, built-in ones first.</summary>
        public static IReadOnlyList<StubInfo> ListTemplates(string root)
        {
            var builtIns = BuiltInTemplates();
            var project = ProjectTemplates(root);
            var result = new List<StubInfo>();

            foreach (var b in builtIns)
            {
                var over = project.FirstOrDefault(p => string.Equals(p.Type, b.Type, StringComparison.OrdinalIgnoreCase));
                if (over == null) result.Add(b);
                else result.Add(new StubInfo { Type = over.Type, Description = over.Description ?? b.Description, FilePath = over.FilePath, FileName = over.FileName, Source = over.Source, OverridesBuiltIn = true });
            }
            result.AddRange(project.Where(p => !builtIns.Any(b => string.Equals(b.Type, p.Type, StringComparison.OrdinalIgnoreCase))).OrderBy(p => p.Type, StringComparer.OrdinalIgnoreCase));
            return result;
        }

        public static StubInfo FindTemplate(string root, string type)
        {
            if (string.IsNullOrWhiteSpace(type)) throw new StubException("Say what to make, e.g. kscript make component Hero.");
            string wanted = Aliases.TryGetValue(type, out var alias) ? alias : type;
            var all = ListTemplates(root);
            var found = all.FirstOrDefault(t => string.Equals(t.Type, wanted, StringComparison.OrdinalIgnoreCase));
            if (found != null) return found;

            throw new StubException($"There is no '{type}' template. Available: {string.Join(", ", all.Select(t => t.Type))}. " +
                                    $"Add your own as {TemplatesFolder}/{type}.html (or any extension).");
        }

        public static MakeResult Make(MakeRequest request)
        {
            string root = Path.GetFullPath(string.IsNullOrEmpty(request.Root) ? "." : request.Root);
            var template = FindTemplate(root, request.Type);

            string rawName = (request.Name ?? "").Trim().Replace('\\', '/').Trim('/');
            if (rawName.Length == 0)
                throw new StubException($"Give the {template.Type} a name, e.g. kscript make {template.Type} {Example(template.Type)}.");
            if (rawName.Split('/').Any(s => s == ".." || s == "." || s.Length == 0) || Path.IsPathRooted(rawName) || rawName.Contains(':'))
                throw new StubException($"'{request.Name}' is not a valid name. Use letters, digits, - and _, with / for folders (e.g. blog/first-post).");

            var values = Values(template, rawName, request.Options, KScript.Projects.ProjectConfig.FindAndLoad(root));
            var (header, body) = SplitHeader(template.Source, template.FileName);

            if (string.Equals(template.Type, "component", StringComparison.OrdinalIgnoreCase))
                CheckComponentName(values["kebab"], request.Name);
            else if (values["kebab"].Length == 0)
                throw new StubException($"'{request.Name}' has no letters or digits to make a file name from.");

            // base: project makes `to` relative to the project folder (where kscript.json is) instead of the site root,
            // for files that aren't part of the site, such as tests.
            if (header.TryGetValue("base", out var baseDir) && string.Equals(baseDir.Trim(), "project", StringComparison.OrdinalIgnoreCase)
                && KScript.Projects.ProjectConfig.FindAndLoad(root) is KScript.Projects.ProjectConfig project)
            {
                root = project.Directory;
            }

            string to = request.To != null ? request.To : header.TryGetValue("to", out var t)
                ? StubTemplate.Render(t, values).Trim()
                : values["dir"] + values["kebab"] + Path.GetExtension(template.FileName);
            to = to.Replace('\\', '/').TrimStart('/');

            string content = StubTemplate.Render(body, values);
            (to, content) = ScriptFormat.Apply(template.FileName, to, content, values["format"]);

            string full = Path.GetFullPath(Path.Combine(root, to));
            string rootWithSlash = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
            if (!full.StartsWith(rootWithSlash, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
                throw new StubException($"The {template.Type} template would write outside the project folder ({to}).");

            bool exists = File.Exists(full);
            if (exists && !request.Force)
                throw new StubException($"{to} already exists. Use --force to replace it.");

            string hint = header.TryGetValue("hint", out var h) ? StubTemplate.Render(h, values).Trim() : null;

            if (!request.DryRun)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, content);
            }

            return new MakeResult { RelativePath = to, FullPath = full, Content = content, Hint = hint, Template = template, Overwrote = exists };
        }

        public const string DefaultLayout = "_layout.html";

        /// <summary>
        /// Creates _layout.html from the layout template when a page or Markdown page that uses it was just made and the
        /// project has none yet. Returns null when nothing was needed.
        /// </summary>
        public static MakeResult MakeDefaultLayoutIfMissing(string root, MakeResult page, IReadOnlyDictionary<string, string> options, bool dryRun)
        {
            string type = page.Template.Type;
            if (!string.Equals(type, "page", StringComparison.OrdinalIgnoreCase) && !string.Equals(type, "markdown", StringComparison.OrdinalIgnoreCase)) return null;
            if (options != null && options.ContainsKey("layout")) return null;
            if (!page.Content.Contains(DefaultLayout, StringComparison.Ordinal)) return null;
            root = Path.GetFullPath(string.IsNullOrEmpty(root) ? "." : root);
            if (File.Exists(Path.Combine(root, DefaultLayout))) return null;
            if (!ListTemplates(root).Any(t => string.Equals(t.Type, "layout", StringComparison.OrdinalIgnoreCase))) return null;

            var layoutOptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (options != null && options.TryGetValue("lang", out var lang)) layoutOptions["lang"] = lang;
            return Make(new MakeRequest { Type = "layout", Name = "layout", Root = root, Options = layoutOptions, DryRun = dryRun, To = DefaultLayout });
        }

        /// <summary>Copies the built-in templates into the project's _templates folder so they can be changed.</summary>
        public static IReadOnlyList<string> PublishTemplates(string root, bool force, IEnumerable<string> only = null)
        {
            root = Path.GetFullPath(root);
            var wanted = only?.Select(o => Aliases.TryGetValue(o, out var a) ? a : o).ToList();
            var builtIns = BuiltInTemplates().Where(b => wanted == null || wanted.Count == 0 || wanted.Contains(b.Type, StringComparer.OrdinalIgnoreCase)).ToList();
            if (wanted != null)
            {
                var unknown = wanted.Where(w => !builtIns.Any(b => string.Equals(b.Type, w, StringComparison.OrdinalIgnoreCase))).ToList();
                if (unknown.Count > 0)
                    throw new StubException($"There is no built-in '{unknown[0]}' template. Built in: {string.Join(", ", BuiltInTemplates().Select(b => b.Type))}.");
            }

            string folder = Path.Combine(root, TemplatesFolder);
            Directory.CreateDirectory(folder);
            var written = new List<string>();
            foreach (var b in builtIns)
            {
                // A project template of the same type with another extension still counts as the same type.
                var existing = Directory.EnumerateFiles(folder).FirstOrDefault(f => string.Equals(Path.GetFileNameWithoutExtension(f), b.Type, StringComparison.OrdinalIgnoreCase));
                if (existing != null && !force) continue;
                if (existing != null && !string.Equals(Path.GetFileName(existing), b.FileName, StringComparison.OrdinalIgnoreCase)) File.Delete(existing);
                File.WriteAllText(Path.Combine(folder, b.FileName), b.Source);
                written.Add($"{TemplatesFolder}/{b.FileName}");
            }
            return written;
        }

        // ---- values ----------------------------------------------------------------------------------------------

        private static Dictionary<string, string> Values(StubInfo template, string rawName, Dictionary<string, string> options, KScript.Projects.ProjectConfig project)
        {
            var segments = rawName.Split('/');
            string name = segments[^1];
            string dir = segments.Length > 1 ? string.Join("/", segments.Take(segments.Length - 1)) + "/" : "";
            bool route = name.Length > 2 && name[0] == '[' && name[^1] == ']';
            string kebab = StubNames.Kebab(name);

            // A page called [id] is titled after its folder: people/[id] is "People".
            string titleSource = route ? (segments.Length > 1 ? segments[^2] : "Page") : name;
            if (!route && string.Equals(name, "index", StringComparison.OrdinalIgnoreCase))
                titleSource = segments.Length > 1 ? segments[^2] : "Home";

            string url = (dir + kebab);
            if (url == "index" || url.EndsWith("/index", StringComparison.Ordinal)) url = url.Substring(0, url.Length - "index".Length).TrimEnd('/');

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["type"] = template.Type,
                ["input"] = rawName,
                ["name"] = name,
                ["dir"] = dir,
                ["kebab"] = kebab,
                ["snake"] = StubNames.Snake(name),
                ["pascal"] = StubNames.Pascal(name),
                ["camel"] = StubNames.Camel(name),
                ["title"] = StubNames.Title(titleSource),
                ["path"] = dir + kebab,
                ["url"] = url,
                // Route segments such as [id] in the name, comma separated: people/[team]/[id] gives "team,id".
                ["params"] = string.Join(",", segments.Where(s => s.Length > 2 && s[0] == '[' && s[^1] == ']').Select(s => s.Substring(1, s.Length - 2))),
            };

            foreach (var kv in options ?? new Dictionary<string, string>())
                values[kv.Key.Replace('-', '_')] = kv.Value;

            ScriptFormat.AddValues(values, ScriptFormat.Resolve(options, project));
            return values;
        }

        private static void CheckComponentName(string kebab, string given)
        {
            if (kebab.Length == 0)
                throw new StubException($"'{given}' has no letters or digits to make a component name from.");
            if (!char.IsLetter(kebab[0]))
                throw new StubException($"Component names must start with a letter, so <{kebab}> will not work. Try e.g. {StubNames.Pascal("x-" + kebab)}.");
            if (!Pages.PageElementNames.CanBeComponent(kebab))
                throw new StubException($"<{kebab}> is already an HTML or KScript element, so a component with that name would never be used. " +
                                        $"Pick another name, e.g. {StubNames.Pascal("site-" + kebab)} for <site-{kebab}>.");
        }

        internal static string Example(string type) => type.ToLowerInvariant() switch
        {
            "component" => "Hero",
            "page" => "about",
            "markdown" => "blog/first-post",
            "layout" => "main",
            "script" => "tidy-downloads",
            "test" => "checkout",
            _ => "Example",
        };

        // ---- template files --------------------------------------------------------------------------------------

        private static List<StubInfo> builtInCache;

        public static IReadOnlyList<StubInfo> BuiltInTemplates()
        {
            if (builtInCache != null) return builtInCache;
            var asm = typeof(Scaffolder).Assembly;
            const string prefix = "KScript.Scaffolding.Templates.";
            string[] order = { "component", "page", "markdown", "layout", "script" };

            var list = asm.GetManifestResourceNames()
                .Where(n => n.StartsWith(prefix, StringComparison.Ordinal))
                .Select(n =>
                {
                    string file = n.Substring(prefix.Length);
                    using var stream = asm.GetManifestResourceStream(n)!;
                    using var reader = new StreamReader(stream);
                    string source = reader.ReadToEnd().Replace("\r\n", "\n");
                    return Info(Path.GetFileNameWithoutExtension(file), file, source, null);
                })
                .OrderBy(t => Array.IndexOf(order, t.Type) is int i && i >= 0 ? i : int.MaxValue)
                .ThenBy(t => t.Type, StringComparer.Ordinal)
                .ToList();
            return builtInCache = list;
        }

        private static List<StubInfo> ProjectTemplates(string root)
        {
            string folder = Path.Combine(Path.GetFullPath(root), TemplatesFolder);
            if (!Directory.Exists(folder)) return new List<StubInfo>();

            var files = Directory.EnumerateFiles(folder)
                .Where(f => !Path.GetFileName(f).StartsWith('.'))
                .ToList();

            var duplicates = files.GroupBy(f => Path.GetFileNameWithoutExtension(f), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
            if (duplicates != null)
                throw new StubException($"{TemplatesFolder} has more than one '{duplicates.Key}' template ({string.Join(", ", duplicates.Select(Path.GetFileName))}). Keep one.");

            return files.Select(f => Info(Path.GetFileNameWithoutExtension(f), Path.GetFileName(f), File.ReadAllText(f).Replace("\r\n", "\n"), f)).ToList();
        }

        private static StubInfo Info(string type, string fileName, string source, string path)
        {
            var (header, _) = SplitHeader(source, fileName);
            return new StubInfo
            {
                Type = type,
                FileName = fileName,
                Source = source,
                FilePath = path,
                Description = header.TryGetValue("description", out var d) ? d : null,
            };
        }

        /// <summary>Splits the optional "--- make" header (key: value lines up to a closing ---) from the template body.</summary>
        internal static (Dictionary<string, string> Header, string Body) SplitHeader(string source, string fileName)
        {
            var header = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string text = source.TrimStart('﻿');
            int firstEnd = text.IndexOf('\n');
            string firstLine = (firstEnd < 0 ? text : text.Substring(0, firstEnd)).Trim();
            if (!string.Equals(firstLine.Replace(" ", ""), "---make", StringComparison.OrdinalIgnoreCase))
                return (header, text);

            int pos = firstEnd + 1;
            while (pos < text.Length)
            {
                int end = text.IndexOf('\n', pos);
                string line = (end < 0 ? text.Substring(pos) : text.Substring(pos, end - pos)).TrimEnd('\r');
                pos = end < 0 ? text.Length : end + 1;

                if (line.Trim() == "---") return (header, text.Substring(pos));
                if (line.Trim().Length == 0 || line.TrimStart().StartsWith('#')) continue;

                int colon = line.IndexOf(':');
                if (colon <= 0) throw new StubException($"{fileName}: header lines look like 'to: path', but found '{line.Trim()}'.");
                header[line.Substring(0, colon).Trim()] = line.Substring(colon + 1).Trim();
            }
            throw new StubException($"{fileName}: the '--- make' header is never closed with a '---' line.");
        }
    }
}
