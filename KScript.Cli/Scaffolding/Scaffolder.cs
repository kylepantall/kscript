using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using KScript.Projects;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

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
        internal MakeResult PlannedModel { get; set; }
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
    /// e.g. _templates/component.kscript, with an optional header saying where the file goes:
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
            ["extend"] = "extension",
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

            var projectConfig = ProjectConfig.FindAndLoad(root);
            var values = Values(template, rawName, request.Options, projectConfig);
            var (header, body) = SplitHeader(template.Source, template.FileName);
            if (header.ContainsKey("adds-to") && !request.Rendering)
                throw new StubException($"kscript make {template.Type} adds to a file rather than making one; run it as kscript make {template.Type} {Example(template.Type)}.");
            ApplyDefaults(header, values);
            CheckOptions(header, values, request.Options, root);

            if (string.Equals(template.Type, "component", StringComparison.OrdinalIgnoreCase))
            {
                CheckComponentName(values["kebab"], request.Name);
                if (MarkupParser.IsCodePath(template.FileName))
                {
                    PrepareComponent(root, projectConfig, request, values);
                }
            }
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
            (to, content) = ScriptFormat.Apply(template.FileName, to, content, values[ScriptFormat.Option], global::KScript.Language.Formatting.FormatOptions.From(KScript.Projects.ProjectConfig.FindAndLoad(root)));

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

        /// <summary>Plans both files before writing a model and its component.</summary>
        public static IReadOnlyList<MakeResult> MakeAll(MakeRequest request)
        {
            if (FindTemplate(request.Root, request.Type).Type != "model"
                || !StubTemplate.IsSet(request.Options.GetValueOrDefault("component")))
                return new[] { Make(request) };

            var model = Make(new MakeRequest
            {
                Type = request.Type, Name = request.Name, Root = request.Root, To = request.To,
                Options = request.Options, Force = request.Force, DryRun = true,
            });
            var component = Make(new MakeRequest
            {
                Type = "component", Name = request.Name, Root = request.Root, Force = request.Force, DryRun = true, PlannedModel = model,
            });
            if (string.Equals(model.FullPath, component.FullPath, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
                throw new StubException("The model and component templates point to the same file. Set separate output paths.");
            if (!request.DryRun)
            {
                var files = new[] { model, component };
                var previous = files.Select(file => file.Overwrote ? File.ReadAllBytes(file.FullPath) : null).ToArray();
                int written = 0;
                try
                {
                    foreach (var file in files)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(file.FullPath)!);
                        using var stream = new FileStream(file.FullPath, file.Overwrote ? FileMode.Create : FileMode.CreateNew);
                        written++;
                        using var writer = new StreamWriter(stream);
                        writer.Write(file.Content);
                    }
                }
                catch
                {
                    for (int i = written - 1; i >= 0; i--)
                    {
                        if (previous[i] == null) File.Delete(files[i].FullPath);
                        else File.WriteAllBytes(files[i].FullPath, previous[i]);
                    }
                    throw;
                }
            }
            return new[] { model, component };
        }

        private static void PrepareComponent(string root, ProjectConfig project, MakeRequest request, Dictionary<string, string> values)
        {
            static bool Identifier(string name) => !string.IsNullOrEmpty(name) && Syntax.Lexer.IsIdentStart(name[0]) && name.All(Syntax.Lexer.IsIdentChar);
            static bool Set(string value) => StubTemplate.IsSet(value);
            static string[] Names(string value) => (value ?? "").Trim().TrimStart('[').TrimEnd(']')
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            string model = values.GetValueOrDefault("model");
            string update = values.GetValueOrDefault("model_update");
            string fields = values.GetValueOrDefault("fields");
            string delay = values.GetValueOrDefault("autosave");
            if (Set(model) && (!Identifier(model) || model == "true"))
                throw new StubException("--model needs the name of an existing model, such as Note.");
            if (Set(update) && (!Identifier(update) || update == "true"))
                throw new StubException("--model-update needs a binding name, such as edit_note.");
            if (Set(delay) && (!Set(update) || (!string.Equals(delay, "true", StringComparison.OrdinalIgnoreCase)
                && (!Runtime.Duration.TrySeconds(delay, out double seconds) || seconds < 0 || seconds * 1000 > int.MaxValue))))
                throw new StubException("Use --autosave with --model-update. Give a delay such as 500ms, or use the flag by itself.");

            if (Set(update))
            {
                var binding = FindModelBinding(root, project, update);
                if (binding != null)
                {
                    string boundModel = binding.GetAttributeValue("model")?.Trim();
                    if (Set(model) && !string.Equals(model, boundModel, StringComparison.OrdinalIgnoreCase))
                        throw new StubException($"ModelUpdate '{update}' uses {boundModel}. Use --model {boundModel} or omit --model.");
                    model = boundModel;
                    string boundFields = binding.GetAttributeValue("fields");
                    if (!Set(fields)) fields = boundFields;
                    else if (Names(fields).Except(Names(boundFields), StringComparer.Ordinal).FirstOrDefault() is string invalidField)
                        throw new StubException($"ModelUpdate '{update}' does not allow editing '{invalidField}'. Choose one of {string.Join(", ", Names(boundFields))}.");
                }
                if (!Set(fields) || fields == "true" || Names(fields).Length == 0)
                    throw new StubException($"Provide --fields title,body or declare the fields on the '{update}' ModelUpdate route.");
            }
            string[] selectedFields = Set(fields) ? Names(fields) : Array.Empty<string>();
            if (fields == "true" || selectedFields.Any(field => !Identifier(field)) || (selectedFields.Length > 0 && !Set(model) && !Set(update)))
                throw new StubException("Use --fields title,body with --model or --model-update.");
            values["fields"] = string.Join(",", selectedFields.Distinct(StringComparer.Ordinal));
            values["model_key"] = "id";
            values["component_suffix"] = "";
            values["record_input"] = Set(model) || Set(update) || request.PlannedModel != null ? "true" : "false";
            var props = Names(values.GetValueOrDefault("props"));
            if (props.Any(prop => prop == "true" || !Identifier(StubNames.Var(prop)))
                || props.Select(StubNames.Var).Distinct(StringComparer.Ordinal).Count() != props.Length
                || (Set(values["record_input"]) && props.Any(prop => StubNames.Var(prop) == "record")))
                throw new StubException("Use distinct tag attributes with --props, e.g. title,sub-title. A model-bound component already has record.");
            values["props"] = string.Join(",", props);

            if (!Set(model) && request.PlannedModel == null) return;

            string modelsDir = Path.Combine(root, values["paths.models"]);
            var planned = request.PlannedModel;
            IEnumerable<string> candidates = project?.ModelFiles() ?? Enumerable.Empty<string>();
            if (Directory.Exists(modelsDir) && (project == null || project.Directory != root))
                candidates = candidates.Concat(Directory.EnumerateFiles(modelsDir, "*", SearchOption.AllDirectories).Where(ScriptFiles.IsScript));
            string file = planned?.FullPath ?? ProjectConfig.ModelFileFor(candidates, model);
            if (file == null)
                throw new StubException($"Model '{model}' was not found. Create it with kscript make model {model} --component.");
            string source = planned?.Content ?? File.ReadAllText(file);
            var declaration = ReadScript(source, file).DescendantElements().FirstOrDefault(e => (e.Name is "model" or "Model" or "KScript.Model")
                && (planned != null || string.Equals(e.GetAttributeValue("name")?.Trim(), model, StringComparison.OrdinalIgnoreCase)));
            if (declaration == null)
                throw new StubException($"No model named '{model ?? values["pascal"]}' is declared in {file}.");
            string modelName = declaration.GetAttributeValue("name").Trim();
            values["model_type"] = modelName;
            if (values["pascal"] == modelName) values["component_suffix"] = ComponentScript.Contract;
            values["model_key"] = declaration.GetAttributeValue("key")?.Trim() ?? "id";
            string componentDir = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(root,
                request.To ?? Path.Combine(values["paths.components"], values["kebab"] + ".kscript"))))!;
            values["model_path"] = Path.GetRelativePath(componentDir, file).Replace('\\', '/').Replace("\"", "\\\"");
        }

        private static MarkupDocument ReadScript(string source, string path) =>
            MarkupParser.Parse(new SourceText(source), DocumentKind.Script, path);

        private static MarkupElement FindModelBinding(string root, ProjectConfig project, string binding)
        {
            var routes = project?.Http.Routes?.Select(r => Path.Combine(project.Directory, r.File))
                ?? HttpSettings.DefaultRouteNames.SelectMany(name => ScriptFiles.Extensions.Select(ext => Path.Combine(root, name + ext)));
            foreach (string file in routes.Where(File.Exists))
            {
                var match = ReadScript(File.ReadAllText(file), file).DescendantElements()
                    .FirstOrDefault(e => (e.Name is "ModelUpdate" or "KScript.ModelUpdate") && e.GetAttributeValue("name")?.Trim() == binding);
                if (match != null) return match;
            }
            return null;
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

        /// <summary>
        /// The header's <c>checks: status=status; tries=int; level=info|warn|error</c> says what an option's value must
        /// be, so a wrong one is refused before anything is written instead of making a file that fails kscript check.
        /// Kinds: int, number, status (an HTTP status), duration, durations, fields (name: type, …), names,
        /// assignments (name = value, …), file (a file in the project), locale, or the choices separated by |.
        /// <c>name=</c> checks the name itself. Only what was given on the command line is checked.
        /// </summary>
        internal static void CheckOptions(Dictionary<string, string> header, Dictionary<string, string> values, IReadOnlyDictionary<string, string> given, string root)
        {
            if (!header.TryGetValue("checks", out var checks)) return;
            foreach (string pair in checks.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                string key = pair.Substring(0, eq).Trim(), kind = pair.Substring(eq + 1).Trim();
                bool isName = key == "name";
                if (!isName && (given == null || !given.Keys.Any(k => string.Equals(k.Replace('-', '_'), key, StringComparison.OrdinalIgnoreCase)))) continue;
                string value = (values.GetValueOrDefault(key) ?? "").Trim();
                string option = isName ? "The name" : "--" + key.Replace('_', '-');
                if (!isName && (value.Length == 0 || value == "true") && kind != "file")
                {
                    if (kind is "fields" or "names" or "assignments" or "durations" && value.Length == 0) continue;
                    throw new StubException($"{option} needs a value.");
                }
                string problem = OptionProblem(kind, value, root);
                if (problem != null) throw new StubException($"{option} should be {problem}, not '{value}'.");
            }
        }

        /// <summary>What a value of this kind should have been, or null when it is one.</summary>
        private static string OptionProblem(string kind, string value, string root)
        {
            static string[] Items(string list) => list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var invariant = System.Globalization.CultureInfo.InvariantCulture;
            switch (kind)
            {
                case "int":
                    return int.TryParse(value, System.Globalization.NumberStyles.None, invariant, out _) ? null : "a whole number";
                case "number":
                    return double.TryParse(value, System.Globalization.NumberStyles.Float, invariant, out double n) && !double.IsNaN(n) && !double.IsInfinity(n) ? null : "a number";
                case "status":
                    return int.TryParse(value, System.Globalization.NumberStyles.None, invariant, out int status) && status >= 100 && status <= 599 ? null : "an HTTP status from 100 to 599, such as 404";
                case "duration":
                    return Runtime.Duration.TrySeconds(value, out double seconds) && seconds >= 0 ? null : "a length of time such as 30s or 5m";
                case "durations":
                    return Items(value).All(d => Runtime.Duration.TrySeconds(d, out double each) && each >= 0) ? null : "lengths of time such as \"10s, 1m, 5m\"";
                case "fields":
                {
                    var names = new HashSet<string>(StringComparer.Ordinal);
                    foreach (string field in Items(value))
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(field, @"\A([A-Za-z_]\w*)\??\s*:\s*\S");
                        if (!match.Success) return $"fields written as name: type, such as \"name: string, age?: int\" ('{field}' is not one)";
                        if (!names.Add(match.Groups[1].Value)) return $"fields with different names ('{match.Groups[1].Value}' is there twice)";
                    }
                    return null;
                }
                case "names":
                {
                    var names = new HashSet<string>(StringComparer.Ordinal);
                    foreach (string name in Items(value))
                    {
                        string snake = StubNames.Snake(name);
                        if (snake.Length == 0 || !(char.IsLetter(snake[0]) || snake[0] == '_')) return $"names that start with a letter, comma separated ('{name}' does not)";
                        if (!names.Add(snake)) return $"different names ('{name}' is there twice)";
                    }
                    return null;
                }
                case "assignments":
                    return Items(value).All(a => System.Text.RegularExpressions.Regex.IsMatch(a, @"\A[A-Za-z_]\w*\s*=\s*\S")) ? null : "constants written as name = value, such as \"max_items = 10, page_size = 25\"";
                case "file":
                    return value.Length > 0 && value != "true" && File.Exists(Path.Combine(root, value)) ? null : "a file in the project (there is none at that path)";
                case "message":
                    // The shell has already swapped "{$id}" for "{}" when the message was in double quotes.
                    if (System.Text.RegularExpressions.Regex.IsMatch(value, @"\{\s*\}")) return "a message whose {…} name a field, such as 'No order {$id}' (in single quotes, so the shell leaves $id alone)";
                    return value.Contains('"') ? "a message without double quotes" : null;
                case "locale":
                    return System.Text.RegularExpressions.Regex.IsMatch(value, @"\A[A-Za-z]{2,3}([-_][A-Za-z0-9]{2,8})*\z") ? null : "a locale such as fr or en-GB";
                default:
                {
                    string[] choices = kind.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    return choices.Contains(value, StringComparer.OrdinalIgnoreCase) ? null : ProjectScaffolder.Either(choices);
                }
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

        /// <summary>The time {{ timestamp }} gives. Tests set it.</summary>
        internal static Func<DateTime> Clock { get; set; } = Now;

        internal static DateTime Now() => DateTime.Now;

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
                // What a policy calls the record it is asked about: its model's name, or other for a User (the first parameter is user).
                ["record"] = StubNames.Snake(name) == "user" ? "other" : StubNames.Snake(name),
                // Migrations are named after when they were made, so they sort in order: 2026_11_22_104500_add_bio.
                ["timestamp"] = Clock().ToString("yyyy_MM_dd_HHmmss", System.Globalization.CultureInfo.InvariantCulture),
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
            // Settings files (config.kscript, kscript.<env>.json) sit beside kscript.json.
            values["paths.project"] = project?.FilePath == null ? "." : Folder(Path.GetRelativePath(project.SiteRoot, project.Directory));
            // Extensions go in the first folder kscript.json's "extensions" lists (not a glob), or extensions/.
            values["paths.extensions"] = Folder(project?.Extensions?.Select(e => e.Trim().Replace('\\', '/').TrimEnd('/'))
                .FirstOrDefault(e => e.Length > 0 && e.IndexOfAny(new[] { '*', '?' }) < 0 && !KScript.Projects.ProjectConfig.IsExtensionFileName(e))
                ?? KScript.Projects.ProjectConfig.DefaultExtensionsFolder);
            // Native types go in the first folder kscript.json's "natives" lists, or natives/.
            values["paths.natives"] = Folder(project?.Natives?.Select(e => e.Trim().Replace('\\', '/').TrimEnd('/'))
                .FirstOrDefault(e => e.Length > 0 && e.IndexOfAny(new[] { '*', '?' }) < 0 && !KScript.Projects.ProjectConfig.IsExtensionFileName(e))
                ?? KScript.Projects.ProjectConfig.DefaultNativesFolder);
            if (!values.ContainsKey("views")) values["views"] = values["paths.views"];
            return values;
        }

        private static string Folder(string path)
        {
            string folder = path.Replace('\\', '/').Trim().Trim('/');
            return folder.Length == 0 ? "." : folder;
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
            "model" => "User",
            "migration" => "add_bio_to_users",
            "seeder" => "users",
            "native" => "Sku",
            "enum" => "Size",
            "const" => "limits",
            "storage" => "people",
            "cache" => "api-cache",
            "job" => "SendReminders",
            "window" => "settings",
            "config" => "staging",
            "env" => "base",
            "locale" => "fr",
            "logger" => "alerts",
            "error" => "PaymentFailed",
            "document" => "annual-report",
            "extension" => "date",
            "policy" => "BlogPost",
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
            ["model"] = "model",
            ["error"] = "error",
            ["job"] = "job",
        };

        /// <summary>The command directory's sections, in order, and the built-in types in each.</summary>
        public static readonly IReadOnlyList<(string Name, string[] Types)> Categories = new[]
        {
            ("Routes and APIs", new[] { "route", "controller", "request", "middleware", "view" }),
            ("Pages and styles", new[] { "page", "markdown", "layout", "component", "partial", "css" }),
            ("Scripts and data", new[] { "script", "module", "type", "native", "model", "migration", "seeder", "enum", "error", "const", "storage", "cache", "job", "extension", "policy" }),
            ("Desktop apps", new[] { "window" }),
            ("Documents", new[] { "document" }),
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
