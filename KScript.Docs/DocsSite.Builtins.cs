using System.Text;
using System.Text.Json.Nodes;

using KScript.Cli;
using KScript.Metadata;
using KScript.Runtime;

namespace KScript.Docs
{
    /// <summary>
    /// The reference pages for what ships besides packages: the built-in types, the error types, every kscript.json
    /// setting, and an A to Z index of all of it. Each is made from the code that defines the thing (the native type
    /// registry, the error type tree, the kscript.json schema the editor validates with), so a new one can't be left
    /// out of the docs.
    /// </summary>
    public sealed partial class DocsSite
    {
        /// <summary>The types that aren't native types: what each holds.</summary>
        internal static readonly IReadOnlyDictionary<string, string> BasicTypeDescriptions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["string"] = "Text. Quoted with ' (as written) or \" (with `$name` and `{ }` filled in).",
            ["int"] = "A whole number: `42`, `-7`. A number with a fraction isn't an int.",
            ["float"] = "A number that may have a fraction: `0.5`, `3.14`.",
            ["number"] = "Any number, whole or not.",
            ["bool"] = "`true` or `false`.",
            ["list"] = "A list of values: `[1, 2, 3]`. `T[]` is a list of T, such as `string[]`.",
            ["map"] = "Names and values: `{ name: 'Joan', age: 82 }`.",
            ["fn"] = "A function: a lambda `fn(x) => $x * 2`, or a declared function passed as `@name`.",
            ["date"] = "A date and time: `@now()`, `@today()`, `'2026-11-19'->to_date()`.",
            ["markdown"] = "A Markdown document, from a text block tagged `markdown`. See [Documents](/guide/documents).",
            ["latex"] = "A LaTeX document, from a text block tagged `latex`. See [Documents](/guide/documents).",
            ["formatted_file"] = "Source code from a text block tagged with a file type such as `ps1`, with `.extension` and `.text`.",
            ["document"] = "A document built with KScript.Document, ready to save as PDF, Word, HTML or Markdown.",
            ["any"] = "Any value. The type a field or parameter has when none is given.",
        };

        private void AddBuiltinPages()
        {
            AddTypesPage();
            AddErrorTypesPage();
            AddSettingsPage();
            AddIndexPage();
        }

        #region Types

        private void AddTypesPage()
        {
            var sb = new StringBuilder();
            var md = new StringBuilder("# Built-in types\n\nEvery type a field, parameter or variable can be declared as without importing anything.\n");
            sb.Append("<h1>Built-in types</h1><p class=\"lead\">Every type a field, parameter or variable can be declared as, without importing anything: <code>age: int</code>, <code>email: email</code>, <code>tags: string[]</code>. Add <code>?</code> to allow null and <code>[]</code> for a list.</p>");
            sb.Append("<p>Rules go in brackets after the type: <code>string(min: 1, max: 200)</code>, <code>int(min: 0)</code>, <code>string(pattern: /^[a-z]+$/)</code>, <code>string(one_of: [draft, live])</code>, <code>handle(not: [@admin])</code>. Every type takes <code>min</code>, <code>max</code>, <code>pattern</code>, <code>one_of</code> and <code>not</code>; the options column lists what a type takes as well. More in <a href=\"/guide/types\">Types and validation</a>.</p>");
            var page = new DocPage { Path = "/reference/types", Title = "Built-in types", NavTitle = "Types", Section = "Reference", Description = "Every built-in type: string, int, date, email, url, money, duration and the rest." };

            var basic = Values.DeclarableTypes.Where(t => NativeTypes.Get(t) == null).ToList();
            sb.Append("<h2 id=\"values\">Values</h2>");
            page.Toc.Add((2, "values", "Values"));
            md.Append("\n## Values\n\n| Type | What it holds |\n| --- | --- |\n");
            sb.Append("<div class=\"table\"><table><thead><tr><th>Type</th><th>What it holds</th></tr></thead><tbody>");
            foreach (string type in basic)
            {
                string description = BasicTypeDescriptions.GetValueOrDefault(type, string.Empty);
                sb.Append($"<tr id=\"type-{Html.Slug(type)}\"><td>{TypeLink(type == KType.Any ? null : type).Replace(">any<", ">any<")}</td><td>{Html.Inline(description)}</td></tr>");
                md.Append($"| `{type}` | {description} |\n");
                Search.Add(new SearchEntry { Title = type, Url = "/reference/types#type-" + Html.Slug(type), Section = "Types", Kind = "type", Text = Shorten(Html.Text(Html.Inline(description))) });
            }
            sb.Append("</tbody></table></div>");

            sb.Append("<h2 id=\"native\">Native types</h2><p>Values that are text underneath, checked and tidied when they are made: an <code>email</code> is lower-cased and must have an @, a <code>url</code> must have a scheme and host. Where the type is expected you can write the value without quotes (<code>timeout: 30s</code>, <code>country: GB</code>), and each has fields and methods of its own.</p>");
            page.Toc.Add((2, "native", "Native types"));
            md.Append("\n## Native types\n\n| Type | What it holds | Example | Options |\n| --- | --- | --- | --- |\n");
            sb.Append("<div class=\"table\"><table><thead><tr><th>Type</th><th>What it holds</th><th>Example</th><th>Options</th></tr></thead><tbody>");
            foreach (NativeTypeInfo t in NativeTypes.All.OrderBy(t => t.Package == null ? 0 : 1).ThenBy(t => t.Name, StringComparer.Ordinal))
            {
                string options = t.Options.Count == 0 ? string.Empty : string.Join(", ", t.Options.Select(o => $"<code>{Html.H(o)}</code>"));
                string from = t.Package != null ? $" <span class=\"muted\">({Html.H(t.Package)})</span>" : string.Empty;
                sb.Append($"<tr id=\"type-{Html.Slug(t.Name)}\"><td>{TypeLink(t.Name)}{from}</td><td>{Html.Inline(t.Description)}</td><td><code>{Html.H(t.Example)}</code></td><td>{options}</td></tr>");
                md.Append($"| `{t.Name}` | {t.Description} | `{t.Example}` | {string.Join(", ", t.Options.Select(o => $"`{o}`"))} |\n");
                Search.Add(new SearchEntry { Title = t.Name, Url = "/reference/types#type-" + Html.Slug(t.Name), Section = "Types", Kind = "type", Text = Shorten(Html.Text(Html.Inline(t.Description))) });
            }
            sb.Append("</tbody></table></div>");
            page.Body = sb.ToString();
            page.Markdown = md.ToString();
            Add(page);
        }

        #endregion

        #region Error types

        private void AddErrorTypesPage()
        {
            var sb = new StringBuilder();
            var md = new StringBuilder("# Error types\n\nThe errors KScript throws, and the ones to throw or extend yourself.\n\n| Error type | Extends | HTTP status | What it means |\n| --- | --- | --- | --- |\n");
            sb.Append("<h1>Error types</h1><p class=\"lead\">The errors KScript throws, and the ones to throw or extend yourself. <code>catch NotFound</code> catches a <code>NotFound</code> and every type that extends it, and <code>catch Error</code> catches everything.</p>");
            sb.Append("<p>Declare your own with <code>error PersonNotFound extends NotFound { id: int }</code> and throw it with <code>throw PersonNotFound(id: 42)</code>. A type with an HTTP status answers with that status when a route doesn't catch it. More in <a href=\"/guide/language#errors\">The language</a>.</p>");
            var page = new DocPage { Path = "/reference/errors", Title = "Error types", NavTitle = "Error types", Section = "Reference", Description = "Every built-in error type, what it extends and its HTTP status." };
            sb.Append("<div class=\"table\"><table><thead><tr><th>Error type</th><th>Extends</th><th>HTTP status</th><th>What it means</th></tr></thead><tbody>");
            foreach (ErrorType e in ErrorTreeOrder())
            {
                string id = "error-" + Html.Slug(e.Name);
                string parent = e.Parent == null ? string.Empty : $"<a href=\"#error-{Html.Slug(e.Parent.Name)}\"><code>{Html.H(e.Parent.Name)}</code></a>";
                sb.Append($"<tr id=\"{id}\"><td><code>{Html.H(e.Name)}</code></td><td>{parent}</td><td>{e.Status?.ToString() ?? string.Empty}</td><td>{Html.Inline(e.Description)}</td></tr>");
                md.Append($"| `{e.Name}` | {(e.Parent == null ? "" : "`" + e.Parent.Name + "`")} | {e.Status?.ToString() ?? ""} | {e.Description} |\n");
                Search.Add(new SearchEntry { Title = e.Name, Url = "/reference/errors#" + id, Section = "Error types", Kind = "error", Text = Shorten(e.Description) });
            }
            sb.Append("</tbody></table></div>");
            page.Body = sb.ToString();
            page.Markdown = md.ToString();
            Add(page);
        }

        /// <summary>The built-in error types, each followed by the ones that extend it.</summary>
        internal static IEnumerable<ErrorType> ErrorTreeOrder()
        {
            var all = ErrorTypes.BuiltIn.Values.Distinct().ToList();
            IEnumerable<ErrorType> Under(ErrorType parent) =>
                all.Where(e => e.Parent == parent).OrderBy(e => e.Name, StringComparer.Ordinal).SelectMany(e => new[] { e }.Concat(Under(e)));
            return Under(null);
        }

        #endregion

        #region Settings

        /// <summary>The kscript.json schema the editor checks the file with, built into kscript.</summary>
        internal static JsonObject SettingsSchema => (JsonObject)JsonNode.Parse(ReadResource("KScript.Docs.Schemas.kscript.schema.json"));

        /// <summary>Every setting in kscript.json, as a dotted path, with its schema.</summary>
        internal static IEnumerable<(string Path, JsonObject Schema)> Settings()
        {
            JsonObject root = SettingsSchema;
            var definitions = root["definitions"] as JsonObject;
            IEnumerable<(string, JsonObject)> Walk(JsonObject node, string prefix, int depth)
            {
                if (node["properties"] is not JsonObject properties) yield break;
                foreach (var (name, value) in properties)
                {
                    if (name == "$schema" || value is not JsonObject property) continue;
                    string path = prefix + name;
                    yield return (path, property);
                    if (depth < 3)
                        foreach (var child in Walk(property, path + ".", depth + 1)) yield return child;
                }
            }
            return Walk(root, string.Empty, 0);
        }

        private static string SettingType(JsonObject schema)
        {
            if (schema["$ref"] is JsonValue reference) return reference.ToString().Split('/').Last() == "typeRules" ? "rules" : "object";
            if (schema["enum"] is JsonArray values) return string.Join(" | ", values.Select(v => v?.ToJsonString().Trim('"') ?? "null"));
            if (schema["type"] is JsonArray types) return string.Join(" | ", types.Select(t => t.ToString()));
            if (schema["type"] is JsonValue type) return type.ToString() == "integer" ? "number" : type.ToString();
            if (schema["anyOf"] is JsonArray any || schema["oneOf"] is JsonArray one && (any = one) != null)
                return string.Join(" | ", any.OfType<JsonObject>().Select(SettingType).Where(t => t.Length > 0).Distinct());
            return string.Empty;
        }

        private void AddSettingsPage()
        {
            var sb = new StringBuilder();
            var md = new StringBuilder("# kscript.json settings\n\nEvery setting a project's kscript.json can have.\n");
            sb.Append("<h1>kscript.json settings</h1><p class=\"lead\">Every setting a project's <code>kscript.json</code> can have. The editor checks the file against the same list, so it completes and explains each one as you type.</p>");
            sb.Append("<p>A dotted name is a setting inside another: <code>serve.port</code> is <code>{ \"serve\": { \"port\": 8080 } }</code>. More in <a href=\"/guide/projects\">Projects and kscript.json</a>.</p>");
            var page = new DocPage { Path = "/reference/settings", Title = "kscript.json settings", NavTitle = "kscript.json settings", Section = "Reference", Description = "Every setting in kscript.json, what it does and its default." };
            var settings = Settings().ToList();
            // Settings with settings inside get a section each; the plain ones share the first.
            bool Section(string top) => settings.Any(s => s.Path.StartsWith(top + ".", StringComparison.Ordinal));
            foreach (var group in settings.GroupBy(s => s.Path.Split('.')[0] is string top && Section(top) ? top : string.Empty).OrderBy(g => g.Key.Length == 0 ? 0 : 1))
            {
                string heading = group.Key.Length == 0 ? "Project" : group.Key;
                string id = "setting-" + (group.Key.Length == 0 ? "project" : Html.Slug(group.Key) + "-section");
                sb.Append(group.Key.Length == 0 ? $"<h2 id=\"{id}\">Project</h2>" : $"<h2 id=\"{id}\"><code>{Html.H(group.Key)}</code></h2>");
                page.Toc.Add((2, id, heading));
                md.Append($"\n## {heading}\n\n| Setting | Type | Default | What it does |\n| --- | --- | --- | --- |\n");
                sb.Append("<div class=\"table\"><table class=\"params\"><thead><tr><th>Setting</th><th>Type</th><th>Default</th><th>What it does</th></tr></thead><tbody>");
                foreach (var (path, schema) in group)
                {
                    string rowId = "setting-" + Html.Slug(path.Replace('.', '-'));
                    string type = SettingType(schema).Replace(" | ", " or ");
                    string fallback = schema["default"]?.ToJsonString() ?? string.Empty;
                    string description = schema["description"]?.GetValue<string>() ?? string.Empty;
                    sb.Append($"<tr id=\"{rowId}\"><td><code>{Html.H(path)}</code></td><td><span class=\"type\">{Html.H(type)}</span></td><td>{(fallback.Length > 0 ? $"<code>{Html.H(fallback)}</code>" : string.Empty)}</td><td>{Html.Inline(description)}</td></tr>");
                    md.Append($"| `{path}` | {type} | {(fallback.Length > 0 ? "`" + fallback + "`" : "")} | {description} |\n");
                    Search.Add(new SearchEntry { Title = path, Url = "/reference/settings#" + rowId, Section = "kscript.json", Kind = "setting", Text = Shorten(description) });
                }
                sb.Append("</tbody></table></div>");
            }
            page.Body = sb.ToString();
            page.Markdown = md.ToString();
            Add(page);
        }

        #endregion

        #region A to Z

        /// <summary>The declarations whose options are listed in the index on their own, such as model's soft_deletes.</summary>
        private static readonly string[] DeclarationElements = { "model", "type", "enum", "error", "job", "fn", "const", "extend", "component", "policy" };

        /// <summary>One entry of the A to Z index.</summary>
        internal sealed record IndexEntry(string Name, string Kind, string Url, string Description);

        /// <summary>Everything built in, one entry each: packages, statements and their options, commands, methods, variables, types, error types, settings and CLI commands.</summary>
        internal IEnumerable<IndexEntry> IndexEntries()
        {
            foreach (string package in PackageNames)
                yield return new IndexEntry(package, "package", PackageSlug(package), Catalog.Packages[package]);
            foreach (ElementInfo e in Catalog.Elements.Where(e => !e.Planned))
            {
                string url = PackageSlug(e.Package) + "#" + ElementAnchor(e);
                string kind = e.Package == "KScript.Core" ? "statement" : "statement in " + e.Package;
                // let, for, case and the rest are written another way in .kscript: the index lists them by name.
                yield return CodeForms.TryGetValue(e.Name, out string form)
                    ? new IndexEntry(e.Name, $"{kind}, written {form}", url, e.Description)
                    : new IndexEntry(CodeName(e), kind, url, e.Description);
                if (DeclarationElements.Contains(e.Name))
                {
                    foreach (var a in e.Attributes.Where(a => a.Name is not ("name" or "description")))
                        yield return new IndexEntry(a.Name, $"option of {CodeName(e)}", e.Name == "model" ? ModelsPath + "#option-" + Html.Slug(a.Name) : url, a.Description);
                }
            }
            foreach (PackageDeclaration d in PackageDeclarations())
                yield return new IndexEntry(d.Name, $"{d.What} in {d.Package}", ModelsPath + "#" + d.Anchor, Summary(d.Element));
            foreach (IndexEntry d in DirectiveIndexEntries().Concat(EditorIndexEntries()))
                yield return d;
            foreach (CallableInfo c in Catalog.Commands.Where(c => !c.Planned))
                yield return new IndexEntry("@" + c.Name, "command in " + c.Package, PackageSlug(c.Package) + "#" + CommandAnchor(c), c.Description);
            foreach (string type in MethodTypes)
            {
                foreach (CallableInfo m in ReferenceMethods(type).Where(m => m.ReceiverType == type))
                    yield return new IndexEntry("->" + m.Name, $"method of {type}", TypeSlug(type) + "#" + MethodAnchor(m), m.Description);
            }
            foreach (var v in Catalog.BuiltInVariables)
                yield return new IndexEntry("$" + v.Key, "variable", "/reference/variables#var-" + Html.Slug(v.Key), v.Value.Description);
            foreach (string type in Values.DeclarableTypes)
                yield return new IndexEntry(type, "type", "/reference/types#type-" + Html.Slug(type), NativeTypes.Get(type)?.Description ?? BasicTypeDescriptions.GetValueOrDefault(type, string.Empty));
            foreach (ErrorType e in ErrorTreeOrder())
                yield return new IndexEntry(e.Name, "error type", "/reference/errors#error-" + Html.Slug(e.Name), e.Description);
            foreach (var (path, schema) in Settings())
                yield return new IndexEntry(path, "kscript.json setting", "/reference/settings#setting-" + Html.Slug(path.Replace('.', '-')), schema["description"]?.GetValue<string>() ?? string.Empty);
            foreach (CliCommand c in CliReference.Commands)
                yield return new IndexEntry(c.Name == "run" ? "kscript <script>" : c.Name == "version" ? "kscript --version" : "kscript " + c.Name, "command line", "/cli#cli-" + c.Name, c.Summary);
        }

        /// <summary>What an entry sorts by: its name without @, $, -> or kscript, in lower case.</summary>
        private static string SortKey(string name) =>
            (name.StartsWith("kscript ", StringComparison.Ordinal) ? name.Substring(8) : name).TrimStart('@', '$', '-', '>', '<', '.').ToLowerInvariant();

        private void AddIndexPage()
        {
            var entries = IndexEntries().OrderBy(e => SortKey(e.Name), StringComparer.Ordinal).ThenBy(e => e.Kind, StringComparer.Ordinal).ToList();
            var sb = new StringBuilder();
            var md = new StringBuilder("# A to Z\n\nEverything built into KScript, in one list.\n");
            sb.Append("<h1>A to Z</h1><p class=\"lead\">Everything built into KScript in one list: statements and their options, commands, methods, variables, types, error types, kscript.json settings and command line commands. Use your browser's find, or the search box, to look one up.</p>");
            var page = new DocPage { Path = "/reference/index", Title = "A to Z", NavTitle = "A to Z", Section = "Reference", Description = "Every built-in name in one list, from ->abs to zip." };
            var letters = entries.GroupBy(e => char.IsLetter(SortKey(e.Name).FirstOrDefault()) ? char.ToUpperInvariant(SortKey(e.Name)[0]) : '#').ToList();
            sb.Append("<p class=\"letters\">").Append(string.Join(" ", letters.Select(g => $"<a href=\"#az-{(g.Key == '#' ? "other" : g.Key.ToString().ToLowerInvariant())}\">{g.Key}</a>"))).Append("</p>");
            foreach (var letter in letters)
            {
                string id = "az-" + (letter.Key == '#' ? "other" : letter.Key.ToString().ToLowerInvariant());
                sb.Append($"<h2 id=\"{id}\">{letter.Key}</h2>");
                page.Toc.Add((2, id, letter.Key.ToString()));
                md.Append($"\n## {letter.Key}\n\n");
                sb.Append("<div class=\"table\"><table class=\"index\"><tbody>");
                foreach (IndexEntry e in letter)
                {
                    sb.Append($"<tr><td><a href=\"{Html.H(e.Url)}\"><code>{Html.H(e.Name)}</code></a></td><td class=\"muted\">{Html.H(e.Kind)}</td><td>{Html.H(Shorten(Html.Text(Html.Inline(e.Description)), 110))}</td></tr>");
                    md.Append($"- [`{e.Name}`]({e.Url}) ({e.Kind}): {Shorten(e.Description, 110)}\n");
                }
                sb.Append("</tbody></table></div>");
            }
            page.Body = sb.ToString();
            page.Markdown = md.ToString();
            Add(page);
        }

        #endregion
    }
}
