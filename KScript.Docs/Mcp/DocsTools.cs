using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using KScript.Language.Analysis;
using KScript.Language.Formatting;
using KScript.Metadata;
using KScript.Projects;
using KScript.Scaffolding;
using KScript.Syntax;
using KScript.Syntax.Text;

namespace KScript.Docs.Mcp
{
    /// <summary>What a tool answered: text for the model, and whether it is an error.</summary>
    public sealed record ToolResult(string Text, bool IsError = false);

    /// <summary>One tool of the docs server: its name, what it is for, its input schema, and what it does.</summary>
    public sealed record Tool(string Name, string Title, string Description, JsonObject InputSchema, Func<JsonObject, ToolResult> Run, bool ReadOnly = true);

    /// <summary>
    /// The tools <c>kscript mcp</c> offers: searching and reading the docs, looking up anything in the catalog, the
    /// <c>kscript make</c> templates, and checking, formatting, converting and running code the way kscript does.
    /// </summary>
    public sealed class DocsTools
    {
        private readonly DocsMarkdown docs;

        public DocsTools(DocsMarkdown docs, string format, string workingDirectory)
        {
            this.docs = docs;
            Format = format;
            WorkingDirectory = workingDirectory;
            All = new[]
            {
                new Tool("search_docs", "Search the KScript docs",
                    "Search the KScript guides and reference (packages, elements, commands, methods, built-in variables, kscript commands). " +
                    "Returns the best matches with a uri to pass to read_doc, or a name to pass to lookup.",
                    Schema(("query", "string", "Words to look for, e.g. 'read a json file' or 'replace'.", true), ("limit", "integer", "How many results (default 10, at most 50).", false)),
                    SearchDocs),
                new Tool("read_doc", "Read a KScript docs page",
                    "Read a whole docs page as Markdown: a guide ('language', 'code-format', 'web', 'testing'...), a package ('KScript.IO'), " +
                    "a type's methods ('string methods'), 'reference', 'variables', 'cli', or a kscript://docs uri from search_docs. With no page, lists every page.",
                    Schema(("page", "string", "The page: a guide slug, a package name, a uri, or a path such as /reference/io.", false), FormatParameter),
                    ReadDoc),
                new Tool("lookup", "Look up a KScript name",
                    "Everything the docs know about one name: an element ('<for>'), a command ('@read_text'), a method ('->replace' or 'string->replace'), " +
                    "a built-in variable ('$args') or a package ('KScript.IO'): parameters and their types, return type, what it does, retired v1 names, and an example.",
                    Schema(("name", "string", "The name, e.g. '<for>', '@read_text', 'list->map', '$args', 'KScript.Math' or a bare 'replace'.", true), ("type", "string", "For methods: the type of the value, e.g. string or list.", false), FormatParameter),
                    Lookup),
                new Tool("list_make_commands", "List kscript make templates",
                    "The kscript make templates a project has (built-in ones and the project's _templates/), and the kscript make project templates, with how to run each.",
                    Schema(("folder", "string", "The project folder (default: the folder the server runs in).", false)),
                    ListMakeCommands),
                new Tool("check_code", "Check KScript code",
                    "Check a script or page without running it, the way kscript check and the editor do (with the project's lint rules): syntax errors, unknown names, wrong arguments, style warnings.",
                    Schema(("code", "string", "The code to check (XML or .kscript). Give code or path.", false), ("path", "string", "A script or page to check instead, inside the server's folder.", false), FormatParameter),
                    CheckCode),
                new Tool("format_code", "Format KScript code",
                    "Format scripts (XML or .kscript) and .html pages the way kscript fmt does (with the project's format settings). Returns the formatted code; nothing is written.",
                    Schema(("code", "string", "The code to format. Give code or path.", false), ("path", "string", "A script or page to format instead, inside the server's folder.", false), FormatParameter),
                    FormatCode),
                new Tool("convert_code", "Convert between XML and .kscript",
                    "Convert a script between the XML format (.ks.xml) and the .kscript format, the way kscript convert does.",
                    Schema(("code", "string", "The script to convert.", true), ("to", "string", "kscript or xml (default: the other format).", false)),
                    ConvertCode),
                new Tool("run_code", "Run KScript code in a sandbox",
                    "Run a script in the docs sandbox and return what it printed: KScript.Core, Terminal, Math, IO, Markdown, Style and Data (a database in memory) only, an empty temporary folder, no network, no environment variables, 5 seconds.",
                    Schema(("code", "string", "The script to run.", true), ("input", "string", "What <input> and other prompts read, one answer per line.", false), FormatParameter),
                    RunCode),
            };
        }

        /// <summary>The format examples are shown in unless a call asks for another: kscript or xml.</summary>
        public string Format { get; }

        /// <summary>The folder paths are relative to, and whose kscript.json sets lint and format rules.</summary>
        public string WorkingDirectory { get; }

        public IReadOnlyList<Tool> All { get; }

        private static readonly (string, string, string, bool) FormatParameter =
            ("format", "string", "kscript or xml: the format of the code, or to show examples in (default: the project's).", false);

        private static JsonObject Schema(params (string Name, string Type, string Description, bool Required)[] properties)
        {
            var props = new JsonObject();
            foreach (var p in properties)
            {
                var prop = new JsonObject { ["type"] = p.Type, ["description"] = p.Description };
                if (p.Name == "format" || p.Name == "to") prop["enum"] = new JsonArray("kscript", "xml");
                props[p.Name] = prop;
            }
            var schema = new JsonObject { ["type"] = "object", ["properties"] = props };
            var required = properties.Where(p => p.Required).Select(p => (JsonNode)p.Name).ToArray();
            if (required.Length > 0) schema["required"] = new JsonArray(required);
            return schema;
        }

        internal static string Text(JsonObject args, string name) =>
            args?[name] is JsonValue v && v.TryGetValue(out string s) ? s : null;

        private string FormatOf(JsonObject args) => DocsCommand.NormalizeFormat(Text(args, "format")) ?? Format;

        /// <summary>The format code is written in: the one asked for, else XML for code that starts with &lt;.</summary>
        private static string CodeFormat(JsonObject args, string code) =>
            DocsCommand.NormalizeFormat(Text(args, "format")) ?? (code.TrimStart().StartsWith("<", StringComparison.Ordinal) ? "xml" : "kscript");

        private string FullPath(string path) => Path.GetFullPath(Path.Combine(WorkingDirectory, path));

        /// <summary>True for a script or page inside the server's folder: the only files check_code and format_code read.</summary>
        internal bool IsProjectSource(string fullPath)
        {
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(WorkingDirectory)) + Path.DirectorySeparatorChar;
            StringComparison compare = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!fullPath.StartsWith(root, compare)) return false;
            return KScript.Syntax.Markup.ScriptFiles.IsScript(fullPath)
                || new[] { ".html", ".htm", ".md" }.Any(e => fullPath.EndsWith(e, StringComparison.OrdinalIgnoreCase));
        }

        private ProjectConfig ConfigFor(string folder)
        {
            try { return ProjectConfig.FindAndLoad(folder); }
            catch (ProjectConfigException) { return null; }
        }

        #region Search

        private sealed record Entry(string Title, string Kind, string Section, string Path, string Text, string More = "");

        private List<Entry> index;

        /// <summary>What search looks through: every entry of the site's search, with the text of each guide section.</summary>
        private List<Entry> Index => index ??= BuildIndex();

        private List<Entry> BuildIndex()
        {
            var sections = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (DocPage page in docs.Site.Pages.Where(p => p.Source != null))
            {
                string markdown = docs.Page(page.Path, "kscript");
                // A guide topic's own words, before its first heading, so a name used only there is found too.
                if (page.ParentPath != null)
                    sections[page.Path] = Regex.Match(markdown, @"\A#[^\n]*\n(.*?)(?=^#{2,3} |\z)", RegexOptions.Multiline | RegexOptions.Singleline).Groups[1].Value;
                foreach (Match m in Regex.Matches(markdown, @"^#{2,3} ([^\n]+)\n(.*?)(?=^#{1,3} |\z)", RegexOptions.Multiline | RegexOptions.Singleline))
                {
                    string heading = Regex.Replace(m.Groups[1].Value, @"[`*]|\[([^\]]*)\]\([^)]*\)", "$1").Trim();
                    var toc = page.Toc.FirstOrDefault(t => t.Text == heading);
                    if (toc.Id != null) sections[page.Path + "#" + toc.Id] = m.Groups[2].Value;
                }
            }
            return docs.Site.Search.Select(s => new Entry(s.Title, s.Kind, s.Section, s.Url, s.Kind == "heading" && sections.TryGetValue(s.Url, out string body) ? body : s.Text ?? string.Empty,
                s.Kind == "page" && sections.TryGetValue(s.Url, out string intro) ? intro.ToLowerInvariant() : string.Empty)).ToList();
        }

        private static readonly Regex Word = new Regex(@"[A-Za-z0-9_.$@<>-]+", RegexOptions.Compiled);

        internal ToolResult SearchDocs(JsonObject args)
        {
            string query = Text(args, "query");
            if (string.IsNullOrWhiteSpace(query)) return new ToolResult("Give a query, e.g. { \"query\": \"read a json file\" }.", true);
            int limit = args?["limit"] is JsonValue l && l.TryGetValue(out int n) ? Math.Clamp(n, 1, 50) : 10;

            string[] words = Word.Matches(query.ToLowerInvariant()).Select(m => m.Value.Trim('.', '-', '<', '>')).Where(w => w.Length > 0).Distinct().ToArray();
            var hits = new List<(Entry Entry, int Matched, int Score)>();
            foreach (Entry e in Index)
            {
                string title = e.Title.ToLowerInvariant();
                string name = title.Trim('@', '<', '>', '$', '(', ')', '/').Split("->").Last().TrimEnd('(', ')');
                string text = e.Text.ToLowerInvariant();
                int matched = 0, score = 0;
                foreach (string w in words)
                {
                    string bare = w.Trim('@', '$');
                    if (name == bare) score += 20;
                    if (title.Contains(bare, StringComparison.Ordinal)) score += 6;
                    int count = Regex.Matches(text, Regex.Escape(bare)).Count;
                    score += Math.Min(count, 4);
                    // A topic's own words count a little, so a name used only there is still found.
                    if (count == 0 && e.More.Contains(bare, StringComparison.Ordinal)) { count = 1; score += 1; }
                    if (title.Contains(bare, StringComparison.Ordinal) || count > 0) matched++;
                }
                if (matched == 0) continue;
                if (e.Kind == "page") score += 2;
                // A query that spells out a name word by word ("read a json file") is asking for that name (@read_json),
                // so it beats a guide section that only uses those words.
                if (name.Contains('_') && name.Split('_').All(part => words.Contains(part))) score += 10;
                hits.Add((e, matched, score));
            }
            if (hits.Count == 0) return new ToolResult($"Nothing in the docs matches '{query}'. Try other words, or read_doc with no page to see every page.");

            var sb = new StringBuilder();
            foreach (var h in hits.OrderByDescending(h => h.Matched).ThenByDescending(h => h.Score).ThenBy(h => h.Entry.Title, StringComparer.Ordinal).Take(limit))
            {
                Entry e = h.Entry;
                string summary = DocsSite.Shorten(Regex.Replace(e.Text, @"```.*?```", " ", RegexOptions.Singleline), 160);
                bool name = e.Kind is "element" or "command" or "method" or "variable";
                sb.Append($"- {e.Title} ({e.Kind}, {e.Section}): {DocsMarkdown.UriOf(e.Path)}");
                if (name) sb.Append($" [lookup: {e.Title.Replace("()", string.Empty)}]");
                if (summary.Length > 0) sb.Append("\n  ").Append(summary);
                sb.Append('\n');
            }
            return new ToolResult(sb.ToString());
        }

        #endregion

        #region Reading

        internal ToolResult ReadDoc(JsonObject args)
        {
            string page = Text(args, "page");
            if (string.IsNullOrWhiteSpace(page))
            {
                var sb = new StringBuilder("Every docs page (pass its uri to read_doc):\n\n");
                foreach (var section in docs.Site.Navigation())
                {
                    sb.Append($"{section.Section}:\n");
                    foreach (DocPage p in section.Pages.Where(p => p.Path != "/playground"))
                    {
                        sb.Append($"- {p.Title}: {DocsMarkdown.UriOf(p.Path)}\n");
                        foreach (var topic in p.Topics) sb.Append($"  - {topic.Title}: {DocsMarkdown.UriOf(topic.Path)}\n");
                    }
                }
                return new ToolResult(sb.ToString());
            }
            string path = docs.Resolve(page);
            string markdown = docs.Page(path, FormatOf(args));
            return markdown == null
                ? new ToolResult($"There is no docs page '{page}'. Call read_doc with no page to list them, or search_docs to find one.", true)
                : new ToolResult(markdown);
        }

        internal ToolResult Lookup(JsonObject args)
        {
            string name = Text(args, "name")?.Trim();
            if (string.IsNullOrEmpty(name)) return new ToolResult("Give a name, e.g. { \"name\": \"@read_text\" }.", true);
            string format = FormatOf(args);
            string type = Text(args, "type");
            var catalog = docs.Catalog;
            var found = new List<string>();

            if (name.StartsWith("<", StringComparison.Ordinal))
            {
                string element = Regex.Match(name, @"^</?\s*([A-Za-z0-9_.:-]+)").Groups[1].Value;
                AddElement(element);
            }
            else if (name.StartsWith("@", StringComparison.Ordinal))
            {
                AddCommand(name.Substring(1).TrimEnd('(', ')'));
            }
            else if (name.StartsWith("$", StringComparison.Ordinal))
            {
                AddVariable(name.Substring(1).Split('.')[0]);
            }
            else if (name.Contains("->", StringComparison.Ordinal))
            {
                int arrow = name.IndexOf("->", StringComparison.Ordinal);
                string receiver = name.Substring(0, arrow).Trim();
                AddMethods(name.Substring(arrow + 2).TrimEnd('(', ')').Trim(), receiver.Length > 0 ? receiver : type);
            }
            else
            {
                string bare = name.TrimEnd('(', ')');
                if (catalog.FindPackage(bare) is string package) found.Add(docs.Package(package, format));
                AddCommand(bare);
                AddElement(bare);
                AddMethods(bare, type);
                AddVariable(bare);
                AddPackageModel(bare);
            }

            if (found.Count > 0) return new ToolResult(string.Join("\n---\n\n", found));
            // A name that is only punctuation (<, @, ->) leaves no words to search for.
            ToolResult suggestions = SearchDocs(new JsonObject { ["query"] = name.Trim('<', '>', '/', '@', '$').Replace('_', ' '), ["limit"] = 5 });
            return new ToolResult($"KScript has nothing called '{name}'." + (suggestions.IsError || suggestions.Text.StartsWith("Nothing", StringComparison.Ordinal) ? string.Empty : "\n\nClose matches:\n" + suggestions.Text), true);

            void AddElement(string element)
            {
                if (catalog.GetElement(element) is ElementInfo e) found.Add(docs.Element(e, format));
                else if (catalog.GetRetiredElement(element) is RetiredInfo r) found.Add(DocsMarkdown.Retired($"<{element}>", r));
            }

            void AddCommand(string command)
            {
                if (catalog.GetCommand(command) is CallableInfo c) found.Add(docs.Callable(c, format));
                else if (catalog.GetRetiredCommand(command) is RetiredInfo r) found.Add(DocsMarkdown.Retired($"@{command}", r));
            }

            void AddMethods(string method, string receiver)
            {
                var methods = catalog.GetMethods(method, string.IsNullOrWhiteSpace(receiver) ? KType.Any : receiver.Trim()).ToList();
                if (!string.IsNullOrWhiteSpace(receiver) && methods.Any(m => KType.AcceptsMethod(m.ReceiverType, m.Name, receiver.Trim())))
                    methods = methods.Where(m => KType.AcceptsMethod(m.ReceiverType, m.Name, receiver.Trim())).ToList();
                found.AddRange(methods.Select(m => docs.Callable(m, format)));
            }

            // A model or mixin a package ships as a KScript script, such as KScript.Auth's Ownable.
            void AddPackageModel(string model)
            {
                foreach (KScript.Runtime.PackageSource source in catalog.PackageSources)
                {
                    Match declared = Regex.Match(source.Text, @"^model\s+" + Regex.Escape(model) + @"\b(\((?<options>[^)]*)\))?", RegexOptions.Multiline);
                    if (!declared.Success) continue;
                    bool mixin = Regex.IsMatch(declared.Groups["options"].Value, @"(^|,)\s*mixin\s*(,|$)");
                    string what = mixin ? "mixin" : "model";
                    found.Add($"# {model}\n\nPackage: {source.Package}\n\nA built-in {what}, written in KScript. Import {source.Package} and use it with "
                        + (mixin ? $"`model Note extends {model} {{ ... }}`" : $"`{model}->where(...)`")
                        + $". Every built-in model and mixin: {DocsMarkdown.UriOf("/guide/data/built-in-models-and-mixins")}\n\n```kscript\n{source.Text.TrimEnd()}\n```\n");
                }
            }

            void AddVariable(string variable)
            {
                if (catalog.BuiltInVariables.ContainsKey(variable)) found.Add(docs.Variables(new[] { variable }, "# $" + variable));
            }
        }

        #endregion

        #region kscript make

        internal ToolResult ListMakeCommands(JsonObject args)
        {
            string folder = FullPath(Text(args, "folder") ?? ".");
            if (!Directory.Exists(folder)) return new ToolResult($"There is no folder '{Text(args, "folder")}'.", true);
            ProjectConfig project = ConfigFor(folder);
            string root = project?.SiteRoot ?? folder;

            var sb = new StringBuilder("kscript make <type> <Name> [--key value ...] [--dir <project>] [--force] [--dry-run] creates a file from a template.\n\nTypes");
            sb.Append(project == null ? " (outside a project: the built-in ones):\n" : $" for the project in {project.Directory}:\n");
            foreach (StubInfo t in Scaffolder.ListTemplates(root))
            {
                string source = t.BuiltIn ? "built in" : $"{Scaffolder.TemplatesFolder}/{Path.GetFileName(t.FilePath)}{(t.OverridesBuiltIn ? ", replaces the built-in one" : string.Empty)}";
                sb.Append($"- kscript make {t.Type} <Name>: {t.Description ?? "(no description)"} [{source}]\n");
            }
            sb.Append("\nNew projects (kscript make project <folder> [--template name]):\n");
            foreach (string name in ProjectTemplate.BuiltInNames())
            {
                ProjectTemplate template = ProjectTemplate.BuiltInTemplate(name);
                sb.Append($"- {name}{(name == ProjectTemplate.DefaultName ? " (default)" : string.Empty)}: {template.Description}\n");
                foreach (ProjectTemplateOption o in template.Options)
                    sb.Append($"  --{o.Name}{(o.Default == null ? string.Empty : $" (default {o.Default})")}: {o.Description}\n");
            }
            sb.Append("\nkscript make templates [type...] copies built-in templates into _templates/ to change them. Read the projects guide (read_doc projects) for more.\n");
            return new ToolResult(sb.ToString());
        }

        #endregion

        #region Code

        /// <summary>The code to work on, from code or a file; or why there is none.</summary>
        private bool Source(JsonObject args, out string code, out string path, out ToolResult error)
        {
            code = Text(args, "code");
            path = Text(args, "path");
            error = null;
            if (path != null)
            {
                path = FullPath(path);
                if (!IsProjectSource(path))
                {
                    // A path is for the project's own code: never a way to read other files on the computer.
                    error = new ToolResult($"'{Text(args, "path")}' isn't a script or page in the server's folder. Give a .kscript, .ks.xml, .html or .md file inside {WorkingDirectory}, or pass the code itself.", true);
                    return false;
                }
                if (!File.Exists(path))
                {
                    error = new ToolResult($"There is no file '{Text(args, "path")}'.", true);
                    return false;
                }
                code = File.ReadAllText(path);
                return true;
            }
            if (code == null) error = new ToolResult("Give code, or the path of a file.", true);
            return code != null;
        }

        internal ToolResult CheckCode(JsonObject args)
        {
            if (!Source(args, out string code, out string path, out ToolResult error)) return error;
            int shift = 0;
            if (path == null)
            {
                string format = CodeFormat(args, code);
                path = Path.Combine(WorkingDirectory, format == "kscript" ? "snippet.kscript" : "snippet.ks.xml");
                if (format == "xml" && !Playground.IsWholeScript(code))
                {
                    code = Playground.AsScript(code, "xml");
                    shift = "<kscript>".Length;
                }
            }

            DocumentAnalysis analysis = Analyzer.Analyze(code, path);
            LintRules.Apply(analysis.Diagnostics, ConfigFor(Path.GetDirectoryName(path)), null);
            var sb = new StringBuilder();
            int errors = 0, warnings = 0;
            foreach (Diagnostic d in analysis.Diagnostics.OrderBy(d => d.Span.Start))
            {
                LinePosition at = analysis.Source.GetPosition(d.Span.Start);
                string severity = d.Severity switch
                {
                    DiagnosticSeverity.Error => "error",
                    DiagnosticSeverity.Warning => "warning",
                    DiagnosticSeverity.Information => "info",
                    _ => "hint",
                };
                if (d.Severity == DiagnosticSeverity.Error) errors++;
                if (d.Severity == DiagnosticSeverity.Warning) warnings++;
                int column = at.Line == 0 ? Math.Max(0, at.Character - shift) : at.Character;
                sb.Append($"{at.Line + 1}:{column + 1}: {severity} {d.Code}: {d.Message}\n");
            }
            string summary = errors + warnings == 0 && sb.Length == 0 ? "No problems found." : $"{errors} error{(errors == 1 ? "" : "s")}, {warnings} warning{(warnings == 1 ? "" : "s")}:";
            return new ToolResult(summary + (sb.Length > 0 ? "\n" + sb : "\n"), errors > 0);
        }

        internal ToolResult FormatCode(JsonObject args)
        {
            if (!Source(args, out string code, out string path, out ToolResult error)) return error;
            bool wrapped = false;
            if (path == null)
            {
                string format = CodeFormat(args, code);
                path = Path.Combine(WorkingDirectory, format == "kscript" ? "snippet.kscript" : "snippet.ks.xml");
                if (format == "xml" && !Playground.IsWholeScript(code))
                {
                    code = Playground.AsScript(code, "xml");
                    wrapped = true;
                }
            }
            FormatResult result = Formatter.Format(code, path, FormatOptions.From(ConfigFor(Path.GetDirectoryName(path))));
            if (result.Text == null) return new ToolResult(result.Error.TrimEnd('.') + ". The code was left as it is.", true);
            return new ToolResult(wrapped ? Playground.Unwrap(result.Text) : result.Text);
        }

        internal ToolResult ConvertCode(JsonObject args)
        {
            string code = Text(args, "code");
            if (code == null) return new ToolResult("Give the code to convert.", true);
            string from = CodeFormat(args, code);
            string to = DocsCommand.NormalizeFormat(Text(args, "to")) ?? (from == "xml" ? "kscript" : "xml");
            if (to == from && DocsCommand.NormalizeFormat(Text(args, "format")) == null)
                return new ToolResult($"The code is already {(to == "xml" ? "XML" : ".kscript")}.");
            string converted = Playground.Convert(code, to);
            return converted == null
                ? new ToolResult($"This code can't be converted to {(to == "xml" ? "XML" : ".kscript")} until it parses. Run check_code to see the problem.", true)
                : new ToolResult(converted);
        }

        internal ToolResult RunCode(JsonObject args)
        {
            string code = Text(args, "code");
            if (code == null) return new ToolResult("Give the code to run.", true);
            PlaygroundResult result = Playground.Run(code, CodeFormat(args, code), Text(args, "input"));
            var sb = new StringBuilder($"Exit code {result.ExitCode} after {result.Milliseconds} ms.\n");
            sb.Append(result.Output.Length > 0 ? "\nOutput:\n" + result.Output.TrimEnd('\n') + "\n" : "\nIt printed nothing.\n");
            if (result.Truncated) sb.Append("(The output was cut short.)\n");
            if (result.Error != null) sb.Append("\nError:\n" + result.Error + "\n");
            return new ToolResult(sb.ToString(), result.Error != null);
        }

        #endregion
    }
}
