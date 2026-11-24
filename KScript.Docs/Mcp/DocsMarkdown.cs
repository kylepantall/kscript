using System.Text;
using System.Text.RegularExpressions;

using KScript.Language.Catalog;
using KScript.Metadata;
using KScript.Runtime;

namespace KScript.Docs.Mcp
{
    /// <summary>
    /// The docs as Markdown, for language models: the same guides and catalog the docs site is built from, with every
    /// XML or .kscript example shown in one format (the project's, or the one asked for).
    /// </summary>
    public sealed class DocsMarkdown
    {
        /// <summary>The URI scheme of the docs resources: kscript://docs/guide/language is the site's /guide/language.</summary>
        public const string UriPrefix = "kscript://docs";

        private readonly Func<string, string> readGuide;

        public DocsMarkdown(DocsSite site, Func<string, string> readGuide = null)
        {
            Site = site;
            this.readGuide = readGuide ?? DocsSite.ReadEmbeddedGuide;
        }

        public DocsSite Site { get; }

        public LanguageCatalog Catalog => Site.Catalog;

        /// <summary>The URI of a docs page or entry: /guide/language#loops is kscript://docs/guide/language#loops.</summary>
        public static string UriOf(string path) => UriPrefix + (path == "/" ? string.Empty : path);

        /// <summary>
        /// The site path a reader means: a URI (kscript://docs/reference/io), a site path (/reference/io), a guide's
        /// slug (language), a package (KScript.IO or IO), a type's methods (string methods) or "cli". Null if nothing fits.
        /// </summary>
        public string Resolve(string what)
        {
            if (string.IsNullOrWhiteSpace(what)) return null;
            string w = what.Trim();
            if (w.StartsWith(UriPrefix, StringComparison.OrdinalIgnoreCase)) w = w.Substring(UriPrefix.Length);
            int hash = w.IndexOf('#');
            if (hash >= 0) w = w.Substring(0, hash);
            w = "/" + w.Trim('/');
            if (Site.Find(w) is DocPage page && page.Path != "/404" && page.Path != "/playground") return page.Path;

            string bare = w.Trim('/');
            if (DocsSite.Guides.FirstOrDefault(g => string.Equals(g.Slug, bare, StringComparison.OrdinalIgnoreCase) || string.Equals(g.File, bare, StringComparison.OrdinalIgnoreCase)) is DocsSite.Guide guide)
                return "/guide/" + guide.Slug;
            if (Catalog.FindPackage(bare) is string package) return PackagePath(package);
            string type = Regex.Replace(bare, @"\s*methods?$", string.Empty, RegexOptions.IgnoreCase);
            if (Catalog.MemberTypes.FirstOrDefault(t => string.Equals(t, type, StringComparison.OrdinalIgnoreCase)) is string known)
                return "/reference/methods/" + known;
            return null;
        }

        /// <summary>A page as Markdown, by its site path; null for a path that isn't a page.</summary>
        public string Page(string path, string format)
        {
            if (path == null) return null;
            if (path.StartsWith("/guide/", StringComparison.Ordinal))
            {
                DocPage topic = Site.Find(path);
                DocsSite.Guide guide = DocsSite.Guides.FirstOrDefault(g => "/guide/" + g.Slug == (topic?.ParentPath ?? path));
                string markdown = topic?.TopicMarkdown ?? (guide == null ? null : readGuide(guide.File));
                return markdown == null ? null : Guide(markdown, guide.FenceLanguage, format);
            }
            if (path == "/") return Home();
            if (path == "/cli") return Cli();
            if (path == "/cli/make") return MakeCommands();
            if (path == "/reference") return ReferenceIndex(format);
            if (path == "/reference/variables") return Variables(Catalog.BuiltInVariables.Keys);
            if (path.StartsWith("/reference/methods/", StringComparison.Ordinal))
            {
                string type = path.Substring("/reference/methods/".Length);
                if (!Catalog.MemberTypes.Contains(type, StringComparer.Ordinal)) return null;
                var methods = Site.ReferenceMethods(type).OrderBy(m => m.Name, StringComparer.Ordinal).ToList();
                var fields = Catalog.FieldsOf(type);
                var sb = new StringBuilder($"# {type} fields and methods\n\nA field is a plain fact, read with a dot and no brackets (`$value.name`). A method takes arguments or does work: call it with `->` (`$value->name(arguments)`), or pipe the value in with `|>`.\n");
                if (fields.Count > 0)
                {
                    sb.Append("\n## Fields\n\n");
                    foreach (var f in fields.Values.OrderBy(f => f.Name, StringComparer.Ordinal)) sb.Append($"- `.{f.Name}` ({f.Type ?? "any"}): {f.Description}\n");
                }
                foreach (CallableInfo m in methods.Where(m => m.ReceiverType == type)) sb.Append('\n').Append(Callable(m, format, level: 2));
                foreach (var group in methods.Where(m => m.ReceiverType != type).GroupBy(m => m.ReceiverType).OrderBy(g => g.Key, StringComparer.Ordinal))
                {
                    sb.Append($"\n## Methods from {group.Key}\n\n");
                    foreach (CallableInfo m in group)
                        sb.Append($"- [`{m.Labels.First()}`]({UriOf(DocsSite.TypeSlug(group.Key))}#{DocsSite.MethodAnchor(m)}){(m.Deprecated != null ? " (deprecated)" : string.Empty)}\n");
                }
                return sb.ToString();
            }
            string package = Catalog.Packages.Keys.FirstOrDefault(p => PackagePath(p) == path);
            return package == null ? null : Package(package, format);
        }

        /// <summary>Every page: its path, title and description, for resources/list.</summary>
        public IEnumerable<DocPage> Pages => Site.Pages.Where(p => p.Path != "/404" && p.Path != "/playground");

        #region Guides

        /// <summary>A guide's Markdown, with each script example in the reader's format where it converts.</summary>
        internal static string Guide(string markdown, string fenceLanguage, string format) =>
            DocsSite.Fence.Replace(markdown.Replace("\r\n", "\n"), m =>
            {
                string[] info = m.Groups[2].Value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string language = info.Length == 0 || info[0] == "try" ? fenceLanguage : info[0];
                string fence = m.Groups[1].Value;
                return fence + Code(m.Groups[3].Value, language, format, out _) + fence;
            });

        /// <summary>
        /// A script in the format asked for, as a fence's info string and body ("kscript\n...\n"). Code that isn't a
        /// script, or that doesn't convert, stays as it is.
        /// </summary>
        internal static string Code(string code, string language, string format, out string shown)
        {
            shown = language;
            code = code.Replace("\r\n", "\n").TrimEnd('\n') + "\n";
            if ((language == "xml" || language == "kscript") && language != format)
            {
                string converted = Playground.Convert(code, format);
                if (converted != null)
                {
                    shown = format;
                    code = converted;
                }
            }
            return shown + "\n" + code;
        }

        #endregion

        #region Reference

        internal static string PackagePath(string package) =>
            "/reference/" + (package.StartsWith("KScript.", StringComparison.Ordinal) ? package.Substring(8) : package).ToLowerInvariant();

        /// <summary>How a script imports a package, in the given format.</summary>
        internal static string ImportLine(string package, string format) =>
            format == "xml" ? $"<import package=\"{package}\" />" : $"import {package}";

        private string Home()
        {
            var sb = new StringBuilder("# KScript docs\n\n");
            sb.Append($"KScript {Site.Version}. A small scripting language for automating your computer, building web pages and APIs, and making desktop apps. Scripts are XML (`.ks.xml`) or the brace-style `.kscript` format.\n\n## Guides\n\n");
            foreach (DocPage g in Site.Pages.Where(p => p.Source != null && p.ParentPath == null))
                sb.Append($"- [{g.Title}]({UriOf(g.Path)}): {g.Description}\n");
            sb.Append($"\n## Reference\n\n- [Reference]({UriOf("/reference")}): every package, and methods by type\n- [Built-in variables]({UriOf("/reference/variables")})\n- [Command line]({UriOf("/cli")})\n");
            return sb.ToString();
        }

        private string ReferenceIndex(string format)
        {
            var sb = new StringBuilder("# Reference\n\n## Packages\n\n");
            foreach (var p in Catalog.Packages.OrderBy(p => p.Key == "KScript.Core" ? 0 : 1).ThenBy(p => p.Key, StringComparer.Ordinal))
                sb.Append($"- [{p.Key}]({UriOf(PackagePath(p.Key))}): {p.Value}\n");
            sb.Append("\n## Methods by type\n\n");
            foreach (string type in Catalog.MemberTypes.OrderBy(t => t, StringComparer.Ordinal))
                sb.Append($"- [{type}]({UriOf("/reference/methods/" + type)}): {Catalog.FieldsOf(type).Count} fields, {Site.ReferenceMethods(type).Count()} methods\n");
            sb.Append($"\n`{ImportLine("KScript.IO", format)}` imports a package; KScript.Core is always there.\n");
            return sb.ToString();
        }

        /// <summary>A package: what it is, how to import it, and all of its elements, commands, methods and variables.</summary>
        public string Package(string package, string format)
        {
            var sb = new StringBuilder($"# {package}\n\n{Catalog.Packages[package]}\n\n");
            sb.Append(package == "KScript.Core" ? "KScript.Core is the language itself: every script has it, so there is nothing to import.\n" : $"Import it at the top of a script: `{ImportLine(package, format)}`\n");
            var brings = Catalog.WithRequired(package).Skip(1).ToList();
            if (brings.Count > 0) sb.Append($"It brings {string.Join(", ", brings)} with it.\n");

            var elements = Catalog.Elements.Where(e => e.Package == package).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
            var commands = Catalog.Commands.Where(c => c.Package == package).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
            var methods = Catalog.Methods.Where(m => m.Package == package).OrderBy(m => m.ReceiverType, StringComparer.Ordinal).ThenBy(m => m.Name, StringComparer.Ordinal).ToList();
            var variables = Catalog.BuiltInVariables.Keys.Where(v => (Catalog.PackageOfVariable(v) ?? "KScript.Core") == package).ToList();

            if (elements.Count > 0)
            {
                sb.Append("\n## Elements\n");
                foreach (ElementInfo e in elements) sb.Append('\n').Append(Element(e, format, level: 3));
            }
            if (commands.Count > 0)
            {
                sb.Append("\n## Commands\n\nCommands are called with `@` in any expression: `@name(arguments)`.\n");
                foreach (CallableInfo c in commands) sb.Append('\n').Append(Callable(c, format, level: 3));
            }
            if (methods.Count > 0)
            {
                sb.Append("\n## Methods\n");
                foreach (CallableInfo m in methods) sb.Append('\n').Append(Callable(m, format, level: 3));
            }
            if (variables.Count > 0) sb.Append('\n').Append(Variables(variables, heading: "## Variables"));
            return sb.ToString();
        }

        /// <summary>A table of built-in variables.</summary>
        internal string Variables(IEnumerable<string> names, string heading = "# Built-in variables")
        {
            var sb = new StringBuilder(heading + "\n\n| Variable | Type | Package | Description |\n|---|---|---|---|\n");
            foreach (string name in names.OrderBy(n => n, StringComparer.Ordinal))
            {
                var v = Catalog.BuiltInVariables[name];
                sb.Append($"| `${name}` | {v.Type ?? "any"} | {Catalog.PackageOfVariable(name) ?? "KScript.Core"} | {Cell(v.Description)} |\n");
            }
            return sb.ToString();
        }

        private static string Cell(string text) => Regex.Replace(text ?? string.Empty, @"\s+", " ").Replace("|", "\\|").Trim();

        /// <summary>One element: its attributes, where it goes, and an example.</summary>
        public string Element(ElementInfo e, string format, int level = 1)
        {
            string name = DocsSite.ElementName(e);
            void AppendOptions(StringBuilder target, IEnumerable<AttributeInfo> options)
            {
                var attributes = options.ToList();
                if (attributes.Count == 0) return;
                target.Append(format == "xml" ? "\n| Attribute | Value | Description |\n" : "\n| Option | Value | Description |\n");
                target.Append("|---|---|---|\n");
                foreach (AttributeInfo a in attributes)
                {
                    string value = a.Options.Count > 0 ? string.Join(" \\| ", a.Options.Select(o => $"`{o}`")) : Regex.Replace(DocsSite.KindName(a.Kind), "</?code>", "`");
                    target.Append($"| `{a.Name}`{(a.Required ? " (required)" : string.Empty)}{(a.Deprecated != null ? " (deprecated)" : string.Empty)} | {value} | {Cell(Deprecated(a.Deprecated).Trim() + " " + a.Description)} |\n");
                }
            }
            if (format != "xml" && CodeStatementHelp.For(e.Name) is string codeHelp)
            {
                var code = new StringBuilder($"{new string('#', level)} {e.Name}\n\nPackage: {e.Package}\n{Deprecated(e.Deprecated)}\n{codeHelp}\n{Agent(e.Agent)}");
                AppendOptions(code, CodeStatementHelp.Options(e));
                if (e.Example != null) code.Append(Example(e.Example, format));
                return code.ToString();
            }
            var sb = new StringBuilder($"{new string('#', level)} <{name}>{(e.Deprecated != null ? " (deprecated)" : string.Empty)}\n\n");
            sb.Append($"Package: {e.Package ?? "planned (not in the interpreter yet)"}\n{Deprecated(e.Deprecated)}\n{e.Description}\n{Agent(e.Agent)}");
            var facts = new List<string>
            {
                e.Content switch
                {
                    ContentKind.Empty => "Self-closing.",
                    ContentKind.Statements => "Holds statements.",
                    ContentKind.Template => "Holds text with `{ }` values.",
                    ContentKind.Expression => "Its text is one expression.",
                    ContentKind.AssignmentStatements => "Its text is assignments separated by `;`.",
                    _ => "Holds HTML and KScript elements.",
                },
            };
            if (e.Context == ElementContext.Page) facts.Add("Pages only.");
            else if (e.Context == ElementContext.Script) facts.Add("Scripts only.");
            if (e.MustBeInside?.Count > 0) facts.Add($"Goes inside {string.Join(" or ", e.MustBeInside.Select(x => $"<{x}>"))}.");
            if (e.MustFollow?.Count > 0) facts.Add($"Follows {string.Join(" or ", e.MustFollow.Select(x => $"<{x}>"))}.");
            if (e.IsLoop) facts.Add("A loop: <break /> and <continue /> work inside.");
            if (e.AllowsExtraAttributes) facts.Add("Other attributes become variables.");
            sb.Append('\n').Append(string.Join(" ", facts)).Append('\n');
            AppendOptions(sb, e.Attributes);
            if (e.Example != null) sb.Append(Example(e.Example, format));
            return sb.ToString();
        }

        /// <summary>One command or method: its signatures, parameters, return type and an example.</summary>
        public string Callable(CallableInfo c, string format, int level = 1)
        {
            bool method = c.Kind == CallableKind.Method;
            var sb = new StringBuilder();
            foreach (Signature s in c.Signatures.DefaultIfEmpty(new Signature()))
            {
                string returns = c.ReturnType != null && c.ReturnType != KType.Null ? " → " + c.ReturnType : string.Empty;
                sb.Append($"{new string('#', level)} {(method ? c.ReceiverType : string.Empty)}{c.Prefix}{c.Name}({string.Join(", ", s.Parameters.Select(p => p.Label))}){returns}{(c.Deprecated != null ? " (deprecated)" : string.Empty)}\n");
            }
            sb.Append($"\nPackage: {c.Package ?? "planned (not in the interpreter yet)"}");
            if (c.Package != null && c.Package != "KScript.Core") sb.Append($" (`{ImportLine(c.Package, format)}`)");
            sb.Append($"\n{Deprecated(c.Deprecated)}\n{c.Description}\n{Agent(c.Agent)}");

            var parameters = c.Signatures.SelectMany(s => s.Parameters).GroupBy(p => p.Name).Select(g => g.First()).ToList();
            // Parameters get a list when there is more to say than the signature shows, as on the site.
            if (parameters.Any(p => p.Type != null || p.Fields.Count > 0 || p.Values.Count > 0 || !string.IsNullOrEmpty(p.Description) || p.Deprecated != null))
            {
                sb.Append("\nParameters:\n");
                foreach (ParameterInfo p in parameters)
                {
                    string takes = p.Values.Count > 0 ? "one of " + string.Join(" | ", p.Values)
                        : p.IsFlag ? $"a flag: `{p.Name}` or `not {p.Name}`"
                        : p.Type ?? "any value";
                    if (p.Fields.Count > 0 && p.Type != null) takes = TypeReference(p.Type);
                    string kind = (p.Variadic ? "any number" : p.Optional ? "optional" : "required") + (p.Deprecated != null ? ", deprecated" : string.Empty);
                    sb.Append($"- `{p.Name}` ({kind}): {takes}{(string.IsNullOrEmpty(p.Description) ? string.Empty : ". " + p.Description)}{(p.Deprecated != null ? ". " + Deprecated(p.Deprecated).Trim() : string.Empty)}\n");
                }
            }
            foreach (var parameter in parameters.Where(p => p.Fields.Count > 0))
            {
                sb.Append($"\n**`{parameter.Name}` fields**\n\n");
                WriteMapFields(sb, parameter, string.Empty);
            }
            if (parameters.Any(p => p.Fields.Count > 0) && c.ReturnType != null)
                sb.Append($"\nReturns {TypeReference(c.ReturnType)}.\n");
            if (parameters.Count > 1)
            {
                sb.Append($"\nArguments can be named: `{c.Prefix}{c.Name}(…, {string.Join(", ", parameters.Skip(1).Take(2).Select(p => p.Name + ": …"))})`.\n");
            }
            if (c.Example != null) sb.Append(Example(c.Example, format));
            return sb.ToString();
        }

        /// <summary>Link a type to its methods and fields when the reference has a page for it.</summary>
        private string TypeReference(string type) => Catalog.MemberTypes.Contains(type)
            ? $"[`{type}`]({UriOf(DocsSite.TypeSlug(type))})" : $"`{type ?? "any"}`";

        private void WriteMapFields(StringBuilder sb, ParameterInfo owner, string indent)
        {
            foreach (var field in owner.Fields)
            {
                sb.Append($"{indent}- `{field.Name}` ({TypeReference(field.Type)}, {(field.Optional ? "optional" : "required")}). {field.Description}");
                if (field.DefaultValue != null) sb.Append($" Default: `{field.DefaultValue}`.");
                if (field.Example != null) sb.Append($" Example: `{field.Example}`.");
                sb.Append('\n');
                if (field.Fields.Count > 0) WriteMapFields(sb, field, indent + "  ");
            }
        }

        /// <summary>A catalog example (XML, or an XML expression shown echoed) in the reader's format.</summary>
        internal static string Example(string example, string format)
        {
            string code = DocsSite.ExampleScript(example);
            return "\nExample:\n\n```" + Code(code, "xml", format, out _) + "```\n";
        }

        /// <summary>The package's notes for coding agents (prefer, avoid, pitfall), as a list, or nothing.</summary>
        internal static string Agent(AgentNotes notes)
        {
            if (notes == null) return string.Empty;
            var lines = new List<string>();
            if (notes.Prefer != null) lines.Add("- Prefer: " + notes.Prefer);
            if (notes.Avoid != null) lines.Add("- Avoid: " + notes.Avoid);
            if (notes.Pitfall != null) lines.Add("- Pitfall: " + notes.Pitfall);
            return lines.Count == 0 ? string.Empty : "\n" + string.Join("\n", lines) + "\n";
        }

        /// <summary>"**Deprecated.** Why. Use x instead." on a line of its own, or nothing when it isn't deprecated.</summary>
        internal static string Deprecated(Deprecation d)
        {
            if (d == null) return string.Empty;
            string text = "**Deprecated.**";
            if (d.Message.Length > 0) text += " " + d.Message.TrimEnd('.') + ".";
            if (d.Replacement != null) text += $" Use `{d.Replacement}` instead.";
            return "\n" + text + "\n";
        }

        /// <summary>A retired v1 name, with what to write instead.</summary>
        internal static string Retired(string written, RetiredInfo r) => $"# {written} ({(r.Renamed ? "renamed" : "retired")})\n\n{r.Message(written)}\n";

        #endregion

        #region Command line

        /// <summary>The kscript make command directory: every type it makes, with its options and examples.</summary>
        private static string MakeCommands()
        {
            var sb = new StringBuilder("# kscript make commands\n\n`kscript make <type> <name>` makes a file where the project keeps that kind of file. `kscript make --list` prints this list, and `kscript make <type> --help` prints one entry. `kscript make templates` copies the templates to the project's `_templates` folder to change them or add types.\n");
            foreach (var (category, types) in KScript.Scaffolding.MakeCommand.Grouped(KScript.Scaffolding.Scaffolder.BuiltInTemplates()))
            {
                sb.Append($"\n## {category}\n");
                foreach (KScript.Scaffolding.StubInfo t in types)
                {
                    sb.Append($"\n### kscript make {t.Type}\n\n```sh\nkscript make {t.Type} {t.Usage ?? "<name>"}\n```\n");
                    if (t.Description != null) sb.Append('\n').Append(t.Description).Append('\n');
                    foreach (KScript.Scaffolding.StubOption o in t.Options) sb.Append($"- `{o.Name}`: {o.Description}\n");
                    if (t.Examples.Count > 0) sb.Append($"\nExample:\n\n```sh\n{string.Join("\n", t.Examples)}\n```\n");
                }
            }
            return sb.ToString();
        }

        private static string Cli()
        {
            var sb = new StringBuilder("# Command line\n\nEverything the `kscript` command does. `kscript --help` prints the same list.\n");
            foreach (CliCommand c in CliReference.Commands)
            {
                sb.Append($"\n## {(c.Name == "run" ? "Running a script" : c.Name == "version" ? "--version and --help" : "kscript " + c.Name)}\n\n```sh\n{c.Usage}\n```\n\n{c.Summary}\n");
                foreach (CliOption o in c.Options) sb.Append($"- `{o.Name}`: {o.Description}\n");
                if (c.Example != null) sb.Append($"\nExample:\n\n```sh\n{c.Example}\n```\n");
            }
            return sb.ToString();
        }

        #endregion
    }
}
