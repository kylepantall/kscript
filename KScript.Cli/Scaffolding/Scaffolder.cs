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
        /// <summary>Where the command directory lists it, such as "Routes and APIs" (the header's <c>category:</c>).</summary>
        public string Category { get; init; }
        /// <summary>What follows <c>kscript make &lt;type&gt;</c>, such as <c>&lt;name&gt; [--resource]</c> (the header's <c>usage:</c>).</summary>
        public string Usage { get; init; }
        /// <summary>The options the template reads, from header lines such as <c>--resource: what it does</c>.</summary>
        public IReadOnlyList<StubOption> Options { get; init; } = Array.Empty<StubOption>();
        /// <summary>Example commands, from the header's <c>example:</c> lines.</summary>
        public IReadOnlyList<string> Examples { get; init; } = Array.Empty<string>();
        internal string FileName { get; init; }
        internal string Source { get; init; }
    }

    /// <summary>An option a template reads: <c>--fields</c> and what it does.</summary>
    public sealed record StubOption(string Name, string Description);

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
        /// <summary>Only render the template (for templates that add to another file, such as route): nothing is written.</summary>
        internal bool Rendering { get; set; }
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
            ["stylesheet"] = "css",
            ["style"] = "css",
            ["mw"] = "middleware",
            ["log-provider"] = "logger",
            ["store"] = "storage",
            ["lib"] = "module",
            ["constants"] = "const",
            ["fn"] = "module",
            ["function"] = "module",
            ["functions"] = "module",
            ["struct"] = "type",
            ["record"] = "type",
            ["shape"] = "type",
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
            if (header.ContainsKey("adds-to") && !request.Rendering)
                throw new StubException($"kscript make {template.Type} adds to a file rather than making one; run it as kscript make {template.Type} {Example(template.Type)}.");
            ApplyDefaults(header, values);

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
            to = System.Text.RegularExpressions.Regex.Replace(to.Replace('\\', '/').TrimStart('/'), @"(^|/)(\./)+", "$1");

            // {{ up }} leads from the new file's folder back to the project's: ../ for controllers/people.kscript.
            values["up"] = string.Concat(Enumerable.Repeat("../", to.Count(ch => ch == '/')));
            string content = StubTemplate.Render(body, values);
            // A template whose sections end with a blank line leaves one at the end; files end with one newline.
            if (content.EndsWith("\n\n", StringComparison.Ordinal)) content = content.TrimEnd('\n') + "\n";
            (to, content) = ScriptFormat.Apply(template.FileName, to, content, values["format"]);

            string full = Path.GetFullPath(Path.Combine(root, to));
            string rootWithSlash = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
            if (!full.StartsWith(rootWithSlash, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
                throw new StubException($"The {template.Type} template would write outside the project folder ({to}).");

            bool exists = File.Exists(full);
            if (exists && !request.Force && !request.Rendering)
                throw new StubException($"{to} already exists. Use --force to replace it.");

            string hint = header.TryGetValue("hint", out var h) ? StubTemplate.Render(h, values).Trim() : null;

            if (!request.DryRun && !request.Rendering)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, content);
            }

            return new MakeResult { RelativePath = to, FullPath = full, Content = content, Hint = hint, Template = template, Overwrote = exists };
        }

        /// <summary>
        /// The header's <c>defaults: actions=index, fields=name: string</c> fills in options that weren't given
        /// (separated by ;). <c>--resource</c> without <c>--actions</c> means the five resource actions.
        /// </summary>
        private static void ApplyDefaults(Dictionary<string, string> header, Dictionary<string, string> values)
        {
            if (StubTemplate.IsSet(values.GetValueOrDefault("resource")) && !values.ContainsKey("actions"))
                values["actions"] = "index, show, store, update, destroy";
            if (!header.TryGetValue("defaults", out var defaults)) return;
            foreach (string pair in defaults.Split(';'))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                string key = pair.Substring(0, eq).Trim();
                if (!values.ContainsKey(key)) values[key] = pair.Substring(eq + 1).Trim();
            }
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
                var existing = Directory.EnumerateFiles(folder).FirstOrDefault(f => string.Equals(TypeOf(f), b.Type, StringComparison.OrdinalIgnoreCase));
                if (existing != null && !force) continue;
                if (existing != null && !string.Equals(Path.GetFileName(existing), b.FileName, StringComparison.OrdinalIgnoreCase)) File.Delete(existing);
                File.WriteAllText(Path.Combine(folder, b.FileName), b.Source);
                written.Add($"{TemplatesFolder}/{b.FileName}");
            }
            return written;
        }

        // ---- values ----------------------------------------------------------------------------------------------

        internal static Dictionary<string, string> Values(StubInfo template, string rawName, Dictionary<string, string> options, KScript.Projects.ProjectConfig project)
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
            // {{ paths.controllers }} and the rest: the recommended folders, or kscript.json's "paths" (see ProjectConfig.DefaultPaths).
            foreach (string folder in KScript.Projects.ProjectConfig.DefaultPaths.Keys)
                values["paths." + folder] = Folder(project?.PathFor(folder) ?? KScript.Projects.ProjectConfig.DefaultPaths[folder]);
            values["paths.config"] = ConfigFolder(project);
            if (!values.ContainsKey("views")) values["views"] = values["paths.views"];
            return values;
        }

        private static string Folder(string path)
        {
            string folder = path.Replace('\\', '/').Trim().Trim('/');
            return folder.Length == 0 ? "." : folder;
        }

        /// <summary>
        /// Where settings files go: config.root if kscript.json sets it (that's where $config reads them), else
        /// paths.config if set, else config/ when the folder is there, else the project folder, where $config looks
        /// by default.
        /// </summary>
        private static string ConfigFolder(KScript.Projects.ProjectConfig project)
        {
            if (project == null) return ".";
            if (project.Config.Root != null)
                return Folder(Path.GetRelativePath(project.SiteRoot, Path.GetFullPath(Path.Combine(project.Directory, project.Config.Root))));
            if (project.Paths.TryGetValue("config", out var set)) return Folder(set);
            return System.IO.Directory.Exists(Path.Combine(project.SiteRoot, "config")) ? "config" : ".";
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
            "route" => "users",
            "controller" => "people",
            "request" => "NewPerson",
            "middleware" => "secure-headers",
            "view" => "people/show",
            "partial" => "footer",
            "css" => "site",
            "module" => "pricing",
            "type" => "Repo",
            "enum" => "Size",
            "const" => "limits",
            "storage" => "people",
            "cache" => "api-cache",
            "job" => "send-reminders",
            "window" => "settings",
            "config" => "staging",
            "env" => "base",
            "locale" => "fr",
            "logger" => "alerts",
            _ => "Example",
        };

        // ---- template files --------------------------------------------------------------------------------------

        /// <summary>
        /// The generator for each kind of declaration a script can export (Interpreter.Declarations): a test fails
        /// if the language gains one without a generator here.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> DeclarationGenerators = new Dictionary<string, string>
        {
            ["fn"] = "module",
            ["const"] = "const",
            ["enum"] = "enum",
            ["type"] = "type",
        };

        /// <summary>The command directory's sections, in order, and the built-in types in each.</summary>
        public static readonly IReadOnlyList<(string Name, string[] Types)> Categories = new[]
        {
            ("Routes and APIs", new[] { "route", "controller", "request", "middleware", "view" }),
            ("Pages and styles", new[] { "page", "markdown", "layout", "component", "partial", "css" }),
            ("Scripts and data", new[] { "script", "module", "type", "enum", "const", "storage", "cache", "job" }),
            ("Desktop apps", new[] { "window" }),
            ("Settings", new[] { "config", "env", "locale", "logger" }),
            ("Tests", new[] { "test" }),
        };

        private static List<StubInfo> builtInCache;

        public static IReadOnlyList<StubInfo> BuiltInTemplates()
        {
            if (builtInCache != null) return builtInCache;
            var asm = typeof(Scaffolder).Assembly;
            const string prefix = "KScript.Scaffolding.Templates.";
            string[] order = Categories.SelectMany(c => c.Types).ToArray();

            var list = asm.GetManifestResourceNames()
                .Where(n => n.StartsWith(prefix, StringComparison.Ordinal))
                .Select(n =>
                {
                    string file = n.Substring(prefix.Length);
                    using var stream = asm.GetManifestResourceStream(n)!;
                    using var reader = new StreamReader(stream);
                    string source = reader.ReadToEnd().Replace("\r\n", "\n");
                    return Info(TypeOf(file), file, source, null);
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

            var duplicates = files.GroupBy(f => TypeOf(f), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
            if (duplicates != null)
                throw new StubException($"{TemplatesFolder} has more than one '{duplicates.Key}' template ({string.Join(", ", duplicates.Select(Path.GetFileName))}). Keep one.");

            return files.Select(f => Info(TypeOf(f), Path.GetFileName(f), File.ReadAllText(f).Replace("\r\n", "\n"), f)).ToList();
        }

        /// <summary>A template's type is its file name up to the first dot: css.ks.css makes css.</summary>
        internal static string TypeOf(string file)
        {
            string name = Path.GetFileName(file);
            int dot = name.IndexOf('.');
            return dot > 0 ? name.Substring(0, dot) : name;
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
                Category = header.TryGetValue("category", out var c) ? c : null,
                Usage = header.TryGetValue("usage", out var u) ? u : null,
                Options = header.Where(kv => kv.Key.StartsWith("--", StringComparison.Ordinal)).Select(kv => new StubOption(kv.Key, kv.Value)).ToList(),
                Examples = header.TryGetValue("example", out var e) ? e.Split('\n') : Array.Empty<string>(),
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
                string key = line.Substring(0, colon).Trim(), value = line.Substring(colon + 1).Trim();
                // A template can give several examples, one per line.
                header[key] = string.Equals(key, "example", StringComparison.OrdinalIgnoreCase) && header.TryGetValue(key, out var before) ? before + "\n" + value : value;
            }
            throw new StubException($"{fileName}: the '--- make' header is never closed with a '---' line.");
        }
    }
}
