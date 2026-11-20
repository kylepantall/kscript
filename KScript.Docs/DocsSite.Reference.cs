using System.Text;

using KScript.Metadata;
using KScript.Runtime;

namespace KScript.Docs
{
    /// <summary>The reference pages, made from the language catalog.</summary>
    public sealed partial class DocsSite
    {
        /// <summary>Value types in the order the reference lists their methods.</summary>
        private static readonly string[] TypeOrder =
        {
            KType.String, KType.Number, KType.Bool, KType.List, KType.Map, KType.Date, KType.Fn, KType.Any, KType.Type,
            KType.File, KType.Config, KType.Logger, KType.Response, KType.Chat, KType.HttpResponse,
        };

        private static readonly Dictionary<string, string> TypeDescriptions = new Dictionary<string, string>
        {
            [KType.String] = "Text. A string that holds a number can use number methods too.",
            [KType.Number] = "Whole and decimal numbers.",
            [KType.Bool] = "yes and no (true and false).",
            [KType.List] = "Lists such as `[1, 2, 3]`. Methods that change a list return a new one.",
            [KType.Map] = "Maps such as `{ name: 'Joan', age: 82 }`.",
            [KType.Date] = "Dates and times, from `@now()`, `@date('2026-11-19')` or `->to_date()`.",
            [KType.Fn] = "Functions: lambdas `fn($x) => $x * 2` and `<fn>` functions.",
            [KType.Any] = "Methods every value has.",
            [KType.Type] = "A `<type>` declared by a script, such as `$Repo`.",
            [KType.File] = "File values, from `@file()`, `@files()` and `@folders()`.",
            [KType.Config] = "`$config`: settings from config files, `.env` files and the environment.",
            [KType.Logger] = "`$log`: structured logging.",
            [KType.Response] = "HTTP responses, from `@get()`, `@post()` and the other KScript.Net requests.",
            [KType.Chat] = "Conversations with a language model, from `@chat()`.",
            [KType.HttpResponse] = "Responses made by controllers and middleware: `@response()`, `@view()`, `@redirect()`.",
        };

        private static readonly string[] BuiltInNames = PackageRegistry.BuiltIn.Select(b => b.Name).ToArray();

        private static string PackageSlug(string package) => "/reference/" + (package.StartsWith("KScript.", StringComparison.Ordinal) ? package.Substring(8) : package).ToLowerInvariant();

        private static string TypeSlug(string type) => "/reference/methods/" + type;

        internal static string ElementAnchor(ElementInfo e) => "el-" + Html.Slug(e.Name);

        internal static string CommandAnchor(CallableInfo c) => "cmd-" + Html.Slug(c.Name);

        internal static string MethodAnchor(CallableInfo m) => "m-" + Html.Slug(m.Name);

        /// <summary>How an element is written: the package roots are KScript.Routes and KScript.Window.</summary>
        internal static string ElementName(ElementInfo e) =>
            e.Example != null && e.Example.Contains("<KScript." + e.Name, StringComparison.Ordinal) ? "KScript." + e.Name : e.Name;

        private IEnumerable<string> PackageNames =>
            Catalog.Packages.Keys.OrderBy(p => p == "KScript.Core" ? 0 : Array.IndexOf(BuiltInNames, p) is int i && i >= 0 ? i : 50).ThenBy(p => p, StringComparer.Ordinal);

        private IEnumerable<string> MethodTypes =>
            Catalog.Methods.Where(m => !m.Planned).Select(m => m.ReceiverType).Distinct()
                .OrderBy(t => Array.IndexOf(TypeOrder, t) is int i && i >= 0 ? i : 50).ThenBy(t => t, StringComparer.Ordinal);

        private static bool CanRun(string package) => package == "KScript.Core" || Playground.Packages.Contains(package);

        private void AddReference()
        {
            var index = new StringBuilder();
            index.Append("<h1>Reference</h1><p class=\"lead\">Every package, element, command, method and variable, as the interpreter and the editor know them. This page is made from the same descriptions that kscript's editor support shows in hover and completion.</p>");
            index.Append("<h2 id=\"packages\">Packages</h2><div class=\"cards\">");
            foreach (string package in PackageNames)
            {
                index.Append($"<a class=\"card\" href=\"{PackageSlug(package)}\"><strong>{Html.H(package)}</strong><span>{Html.Inline(Catalog.Packages[package])}</span></a>");
            }
            index.Append("</div><h2 id=\"methods\">Methods by type</h2><div class=\"cards\">");
            foreach (string type in MethodTypes)
            {
                int count = Catalog.Methods.Count(m => !m.Planned && m.ReceiverType == type);
                index.Append($"<a class=\"card\" href=\"{TypeSlug(type)}\"><strong>{Html.H(type)}</strong><span>{count} method{(count == 1 ? "" : "s")}. {Html.Inline(TypeDescriptions.GetValueOrDefault(type, string.Empty))}</span></a>");
            }
            index.Append("</div><h2 id=\"variables\">Built-in variables</h2><p>Variables that exist without being declared, such as <code>$args</code>: see <a href=\"/reference/variables\">Built-in variables</a>.</p>");
            var indexPage = new DocPage { Path = "/reference", Title = "Reference", NavTitle = "Overview", Section = "Reference", Description = "Every package, element, command, method and variable.", Body = index.ToString() };
            indexPage.Toc.Add((2, "packages", "Packages"));
            indexPage.Toc.Add((2, "methods", "Methods by type"));
            indexPage.Toc.Add((2, "variables", "Built-in variables"));
            Add(indexPage);

            foreach (string package in PackageNames) AddPackagePage(package);
            foreach (string type in MethodTypes) AddTypePage(type);
            AddVariablesPage();
        }

        private void AddPackagePage(string package)
        {
            var elements = Catalog.Elements.Where(e => e.Package == package).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
            var commands = Catalog.Commands.Where(c => c.Package == package).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
            var methods = Catalog.Methods.Where(m => m.Package == package).ToList();
            var variables = Catalog.BuiltInVariables.Where(v => (Catalog.PackageOfVariable(v.Key) ?? "KScript.Core") == package).OrderBy(v => v.Key).ToList();

            string path = PackageSlug(package);
            var page = new DocPage { Path = path, Title = package, Section = "Reference", Description = Catalog.Packages[package], Body = string.Empty };
            var sb = new StringBuilder();
            sb.Append($"<h1>{Html.H(package)}</h1><p class=\"lead\">{Html.Inline(Catalog.Packages[package])}</p>");
            if (package == "KScript.Core")
            {
                sb.Append("<p>KScript.Core is the language itself. Every script and page has it, so there is nothing to import.</p>");
            }
            else
            {
                string root = elements.Select(ElementName).FirstOrDefault(n => n.StartsWith("KScript.", StringComparison.Ordinal));
                sb.Append("<p>Import it at the top of a script:</p>");
                sb.Append(Html.Code($"<import package=\"{package}\" />", "xml"));
                if (root != null) sb.Append($"<p>A file whose root element is <code>&lt;{Html.H(root)}&gt;</code> imports it for you.</p>");
                var brings = Catalog.WithRequired(package).Skip(1).ToList();
                if (brings.Count > 0) sb.Append($"<p>It brings {string.Join(", ", brings.Select(b => $"<a href=\"{PackageSlug(b)}\">{Html.H(b)}</a>"))} with it.</p>");
                if (!CanRun(package) && Playground.Unavailable.TryGetValue(package, out string why))
                    sb.Append($"<p class=\"note\">{Html.H(package)} {Html.H(why)}, so its examples can't run inside the docs. Copy one into a <code>.ks.xml</code> file and run it with <code>kscript</code>.</p>");
            }

            sb.Append("<div class=\"summary\">");
            if (elements.Count > 0) sb.Append($"<a href=\"#elements\">{elements.Count} element{(elements.Count == 1 ? "" : "s")}</a>");
            if (commands.Count > 0) sb.Append($"<a href=\"#commands\">{commands.Count} command{(commands.Count == 1 ? "" : "s")}</a>");
            if (methods.Count > 0) sb.Append($"<a href=\"#methods\">{methods.Count} method{(methods.Count == 1 ? "" : "s")}</a>");
            if (variables.Count > 0) sb.Append($"<a href=\"#variables\">{variables.Count} variable{(variables.Count == 1 ? "" : "s")}</a>");
            sb.Append("</div>");

            if (elements.Count > 0)
            {
                sb.Append("<h2 id=\"elements\">Elements</h2>");
                page.Toc.Add((2, "elements", "Elements"));
                foreach (ElementInfo e in elements)
                {
                    sb.Append(ElementEntry(e, path));
                    page.Toc.Add((3, ElementAnchor(e), "<" + ElementName(e) + ">"));
                }
            }
            if (commands.Count > 0)
            {
                sb.Append("<h2 id=\"commands\">Commands</h2><p>Commands are called with <code>@</code>, in any expression: <code>@name(arguments)</code>.</p>");
                page.Toc.Add((2, "commands", "Commands"));
                foreach (CallableInfo c in commands)
                {
                    sb.Append(CallableEntry(c, path));
                    page.Toc.Add((3, CommandAnchor(c), "@" + c.Name));
                }
            }
            if (methods.Count > 0)
            {
                sb.Append("<h2 id=\"methods\">Methods</h2>");
                page.Toc.Add((2, "methods", "Methods"));
                foreach (var group in methods.GroupBy(m => m.ReceiverType).OrderBy(g => Array.IndexOf(TypeOrder, g.Key) is int i && i >= 0 ? i : 50))
                {
                    sb.Append($"<p>On <a href=\"{TypeSlug(group.Key)}\">{Html.H(group.Key)}</a> values: ");
                    sb.Append(string.Join(", ", group.Select(m => $"<a href=\"{TypeSlug(group.Key)}#{MethodAnchor(m)}\"><code>-&gt;{Html.H(m.Name)}()</code></a>")));
                    sb.Append("</p>");
                }
            }
            if (variables.Count > 0)
            {
                sb.Append("<h2 id=\"variables\">Variables</h2>");
                page.Toc.Add((2, "variables", "Variables"));
                sb.Append(VariablesTable(variables));
            }
            page.Body = sb.ToString();
            Add(page);
        }

        private void AddTypePage(string type)
        {
            var methods = Catalog.Methods.Where(m => !m.Planned && m.ReceiverType == type).ToList();
            string path = TypeSlug(type);
            var sb = new StringBuilder();
            sb.Append($"<h1>{Html.H(type)} methods</h1>");
            if (TypeDescriptions.TryGetValue(type, out string description)) sb.Append($"<p class=\"lead\">{Html.Inline(description)}</p>");
            sb.Append("<p>Call a method with <code>-&gt;</code> on a value: <code>$value-&gt;name(arguments)</code>, or pipe the value in with <code>|&gt;</code>: <code>$value |&gt; name(arguments)</code>.</p>");
            var fields = Catalog.FieldsOf(type);
            if (fields.Count > 0)
            {
                sb.Append("<h2 id=\"fields\">Fields</h2><div class=\"table\"><table><thead><tr><th>Field</th><th>Type</th></tr></thead><tbody>");
                foreach (var f in fields) sb.Append($"<tr><td><code>.{Html.H(f.Key)}</code></td><td>{Html.H(f.Value)}</td></tr>");
                sb.Append("</tbody></table></div>");
            }
            var page = new DocPage { Path = path, Title = type + " methods", NavTitle = type, Section = "Methods by type", Description = description ?? $"Methods of {type} values." };
            if (fields.Count > 0) page.Toc.Add((2, "fields", "Fields"));
            foreach (var group in methods.GroupBy(m => m.Package).OrderBy(g => g.Key == "KScript.Core" ? 0 : 1))
            {
                string id = "from-" + Html.Slug(group.Key);
                sb.Append($"<h2 id=\"{id}\">From {Html.H(group.Key)}</h2>");
                if (group.Key != "KScript.Core") sb.Append($"<p>These need <code>&lt;import package=\"{Html.H(group.Key)}\" /&gt;</code>. See <a href=\"{PackageSlug(group.Key)}\">{Html.H(group.Key)}</a>.</p>");
                page.Toc.Add((2, id, "From " + group.Key));
                foreach (CallableInfo m in group.OrderBy(m => m.Name, StringComparer.Ordinal))
                {
                    sb.Append(CallableEntry(m, path));
                    page.Toc.Add((3, MethodAnchor(m), "->" + m.Name));
                }
            }
            page.Body = sb.ToString();
            Add(page);
        }

        private void AddVariablesPage()
        {
            var sb = new StringBuilder();
            sb.Append("<h1>Built-in variables</h1><p class=\"lead\">Variables every script or page has without declaring them. Some come with a package, and exist once it is imported.</p>");
            sb.Append(VariablesTable(Catalog.BuiltInVariables.OrderBy(v => Catalog.PackageOfVariable(v.Key) ?? string.Empty).ThenBy(v => v.Key).ToList(), withPackage: true));
            var page = new DocPage { Path = "/reference/variables", Title = "Built-in variables", NavTitle = "Variables", Section = "Reference", Description = "Variables that exist without being declared, such as $args.", Body = sb.ToString() };
            Add(page);
        }

        private string VariablesTable(List<KeyValuePair<string, (string Type, string Description)>> variables, bool withPackage = false)
        {
            var sb = new StringBuilder();
            sb.Append("<div class=\"table\"><table><thead><tr><th>Variable</th><th>Type</th>");
            if (withPackage) sb.Append("<th>Package</th>");
            sb.Append("<th>Description</th></tr></thead><tbody>");
            foreach (var v in variables)
            {
                string package = Catalog.PackageOfVariable(v.Key) ?? "KScript.Core";
                string id = "var-" + Html.Slug(v.Key);
                sb.Append($"<tr id=\"{id}\"><td><code>${Html.H(v.Key)}</code></td><td>{TypeLink(v.Value.Type)}</td>");
                if (withPackage) sb.Append($"<td><a href=\"{PackageSlug(package)}\">{Html.H(package)}</a></td>");
                sb.Append($"<td>{Html.Inline(v.Value.Description)}</td></tr>");
                if (withPackage)
                    Search.Add(new SearchEntry { Title = "$" + v.Key, Url = "/reference/variables#" + id, Section = package, Kind = "variable", Text = Shorten(Html.Text(Html.Inline(v.Value.Description))) });
            }
            sb.Append("</tbody></table></div>");
            return sb.ToString();
        }

        private string TypeLink(string type)
        {
            if (string.IsNullOrEmpty(type) || type == KType.Any) return "<span class=\"type\">any</span>";
            bool hasPage = Catalog.Methods.Any(m => !m.Planned && m.ReceiverType == type);
            return hasPage ? $"<a class=\"type\" href=\"{TypeSlug(type)}\">{Html.H(type)}</a>" : $"<span class=\"type\">{Html.H(type)}</span>";
        }

        private string ElementEntry(ElementInfo e, string path)
        {
            string name = ElementName(e);
            string anchor = ElementAnchor(e);
            var sb = new StringBuilder();
            sb.Append($"<section class=\"entry\" id=\"{anchor}\"><h3 class=\"sig\"><a class=\"anchor\" href=\"#{anchor}\">#</a><code>&lt;{Html.H(name)}&gt;</code></h3>");
            sb.Append($"<div class=\"desc\">{Html.Inline(e.Description)}</div>");

            var facts = new List<string>();
            facts.Add(e.Content switch
            {
                ContentKind.Empty => "Self-closing.",
                ContentKind.Statements => "Holds statements.",
                ContentKind.Template => "Holds text with <code>{ }</code> values.",
                ContentKind.Expression => "Its text is one expression.",
                ContentKind.AssignmentStatements => "Its text is assignments separated by <code>;</code>.",
                _ => "Holds HTML and KScript elements.",
            });
            if (e.Context == ElementContext.Page) facts.Add("Pages only.");
            else if (e.Context == ElementContext.Script) facts.Add("Scripts only.");
            if (e.MustBeInside?.Count > 0) facts.Add($"Goes inside {string.Join(" or ", e.MustBeInside.Select(x => $"<code>&lt;{Html.H(x)}&gt;</code>"))}.");
            if (e.MustFollow?.Count > 0) facts.Add($"Follows {string.Join(" or ", e.MustFollow.Select(x => $"<code>&lt;{Html.H(x)}&gt;</code>"))}.");
            if (e.IsLoop) facts.Add("A loop: <code>&lt;break /&gt;</code> and <code>&lt;continue /&gt;</code> work inside.");
            if (e.AllowsExtraAttributes) facts.Add("Other attributes become variables.");
            sb.Append($"<p class=\"facts\">{string.Join(" ", facts)}</p>");

            if (e.Attributes.Count > 0)
            {
                sb.Append("<div class=\"table\"><table class=\"params\"><thead><tr><th>Attribute</th><th>Value</th><th>Description</th></tr></thead><tbody>");
                foreach (AttributeInfo a in e.Attributes)
                {
                    string value = a.Options.Count > 0 ? string.Join(" | ", a.Options.Select(o => $"<code>{Html.H(o)}</code>")) : KindName(a.Kind);
                    sb.Append($"<tr><td><code>{Html.H(a.Name)}</code>{(a.Required ? " <span class=\"req\">required</span>" : "")}</td><td>{value}</td><td>{Html.Inline(a.Description)}</td></tr>");
                }
                sb.Append("</tbody></table></div>");
            }
            if (e.Example != null) sb.Append(Example("<" + e.Name + ">", e.Example, e.Package));
            sb.Append("</section>");

            Search.Add(new SearchEntry { Title = "<" + name + ">", Url = path + "#" + anchor, Section = e.Package, Kind = "element", Text = Shorten(Html.Text(Html.Inline(e.Description))) });
            return sb.ToString();
        }

        internal static string KindName(AttributeKind kind) => kind switch
        {
            AttributeKind.Expression => "expression",
            AttributeKind.ExpressionList => "expressions, comma separated",
            AttributeKind.Template => "text with <code>{ }</code>",
            AttributeKind.DeclareVariable => "a new variable's name",
            AttributeKind.DeclareVariables => "new variable names",
            AttributeKind.VariableReference => "a variable's name",
            AttributeKind.Parameters => "parameters",
            AttributeKind.DeclareFunction => "a function name",
            AttributeKind.FunctionReference => "a function",
            AttributeKind.FunctionReferences => "functions, comma separated",
            AttributeKind.Path => "a file path",
            AttributeKind.Selector => "a CSS selector",
            AttributeKind.Enum => "one of a set of words",
            AttributeKind.Bool => "<code>yes</code> or <code>no</code>",
            AttributeKind.Statements => "statements",
            AttributeKind.TypeName => "a type",
            AttributeKind.Fields => "fields",
            _ => "text",
        };

        private string CallableEntry(CallableInfo c, string path)
        {
            bool method = c.Kind == CallableKind.Method;
            string anchor = method ? MethodAnchor(c) : CommandAnchor(c);
            var sb = new StringBuilder();
            sb.Append($"<section class=\"entry\" id=\"{anchor}\"><h3 class=\"sig\"><a class=\"anchor\" href=\"#{anchor}\">#</a>");
            foreach (Signature s in c.Signatures)
            {
                string receiver = method ? $"<span class=\"recv\">{Html.H(c.ReceiverType)}</span>" : string.Empty;
                string parameters = string.Join(", ", s.Parameters.Select(p => $"<span class=\"param\">{Html.H(p.Label)}</span>"));
                sb.Append($"<code>{receiver}{Html.H(c.Prefix)}<span class=\"name\">{Html.H(c.Name)}</span>({parameters})</code>");
            }
            if (c.ReturnType != null && c.ReturnType != KType.Null) sb.Append($" <span class=\"returns\">→ {TypeLink(c.ReturnType)}</span>");
            if (method && c.Package != "KScript.Core") sb.Append($" <a class=\"badge\" href=\"{PackageSlug(c.Package)}\">{Html.H(c.Package)}</a>");
            sb.Append("</h3>");
            sb.Append($"<div class=\"desc\">{Html.Inline(c.Description)}</div>");

            var parametersList = c.Signatures.SelectMany(s => s.Parameters).GroupBy(p => p.Name).Select(g => g.First()).ToList();
            // Parameters only get a table when there is more to say than the signature shows: a type, the values
            // allowed, or a description.
            bool detailed = parametersList.Any(p => p.Type != null || p.Values.Count > 0 || !string.IsNullOrEmpty(p.Description));
            if (detailed)
            {
                sb.Append("<div class=\"table\"><table class=\"params\"><thead><tr><th>Parameter</th><th>Takes</th></tr></thead><tbody>");
                foreach (ParameterInfo p in parametersList)
                {
                    string takes = p.Values.Count > 0 ? "one of " + string.Join(" | ", p.Values.Select(v => $"<code>{Html.H(v)}</code>"))
                        : p.IsFlag ? $"a flag: <code>{Html.H(p.Name)}</code> or <code>not {Html.H(p.Name)}</code>"
                        : p.Type != null ? Html.H(p.Type) : "any value";
                    if (p.Variadic) takes += ", any number of them";
                    string notes = p.Optional ? " <span class=\"opt\">optional</span>" : p.Variadic ? string.Empty : " <span class=\"req\">required</span>";
                    if (!string.IsNullOrEmpty(p.Description)) takes += ". " + Html.Inline(p.Description);
                    sb.Append($"<tr><td><code>{Html.H(p.Name)}</code>{notes}</td><td>{takes}</td></tr>");
                }
                sb.Append("</tbody></table></div>");
            }
            if (parametersList.Count > 1)
            {
                sb.Append($"<p class=\"hint\">Arguments can be named: <code>{Html.H(c.Prefix + c.Name)}({Html.H(string.Join(", ", parametersList.Skip(1).Take(2).Select(p => p.Name + ": …").Prepend("…")))})</code>.</p>");
            }
            if (c.Example != null) sb.Append(Example(method ? c.ReceiverType + "->" + c.Name : "@" + c.Name, c.Example, c.Package));
            sb.Append("</section>");

            string title = method ? $"{c.ReceiverType}->{c.Name}()" : $"@{c.Name}()";
            Search.Add(new SearchEntry { Title = title, Url = path + "#" + anchor, Section = c.Package, Kind = method ? "method" : "command", Text = Shorten(Html.Text(Html.Inline(c.Description))) });
            return sb.ToString();
        }

        /// <summary>An example from the catalog. An expression is shown echoed, so running it prints its value.</summary>
        internal static string ExampleScript(string example)
        {
            string text = example.Replace("\r\n", "\n").Trim();
            return text.StartsWith("<", StringComparison.Ordinal) ? text : $"<echo>{{ {text} }}</echo>";
        }

        /// <summary>
        /// Examples that need something the docs sandbox doesn't have (a page request, a file or folder of the
        /// reader's), so they are shown but not run. A test checks that each still names a real entry.
        /// </summary>
        internal static readonly IReadOnlyDictionary<string, string> StaticExamples = new Dictionary<string, string>
        {
            ["<import>"] = "imports a script file of yours",
            ["<picker>"] = "browses the folders of your computer",
            ["<stylesheet>"] = "compiles a stylesheet file of yours",
            ["<translations>"] = "reads a folder of translation files",
            ["@locale_path"] = "is for web pages",
            ["@read_markdown"] = "reads a Markdown file of yours",
            ["@stylesheet"] = "compiles a stylesheet file of yours",
        };

        /// <summary>What the Input box starts with for examples that ask questions.</summary>
        internal static readonly IReadOnlyDictionary<string, string> ExampleInputs = new Dictionary<string, string>
        {
            ["<input>"] = "Joan\n42\n",
            ["<menu>"] = "1\n",
            ["<confirm>"] = "y\n",
            ["<pause>"] = "\n",
        };

        private static string Example(string key, string example, string package)
        {
            string code = ExampleScript(example);
            // KScript.Style's command examples are stylesheet text (.ks.css), not scripts.
            bool stylesheet = package == "KScript.Style" && !example.TrimStart().StartsWith("<", StringComparison.Ordinal);
            if (stylesheet) return "<div class=\"example\"><div class=\"example-label\">Example (in a .ks.css file)</div>" + Html.Code(example.Trim(), "css") + "</div>";
            if (CanRun(package) && !code.StartsWith("<KScript.", StringComparison.Ordinal) && !StaticExamples.ContainsKey(key))
            {
                if (package != "KScript.Core" && !code.Contains("<import", StringComparison.Ordinal))
                    code = $"<import package=\"{package}\" />\n" + code;
                return "<div class=\"example\"><div class=\"example-label\">Example</div>" + Html.Try(code, input: ExampleInputs.GetValueOrDefault(key)) + "</div>";
            }
            return "<div class=\"example\"><div class=\"example-label\">Example</div>" + Html.Code(code, "xml") + "</div>";
        }
    }
}
