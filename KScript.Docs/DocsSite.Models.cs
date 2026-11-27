using System.Text;

using KScript.Syntax.Markup;
using KScript.Metadata;
using KScript.Runtime;

namespace KScript.Docs
{
    /// <summary>
    /// The models page: every option a model takes and every model, mixin and other declaration that a package ships
    /// as a KScript script (KScript.Auth's Ownable). It is read from the options' descriptions and the packages' own
    /// scripts, so a new option or mixin shows up here, in the A to Z and in search without anyone writing it.
    /// </summary>
    public sealed partial class DocsSite
    {
        internal const string ModelsPath = "/reference/models";

        /// <summary>A declaration in a package's own KScript script.</summary>
        internal sealed record PackageDeclaration(string Package, string Kind, string Name, MarkupElement Element)
        {
            public bool IsMixin => Kind == "model" && Element.GetAttributeValue("mixin") is string m && m.Trim() is "yes" or "true";

            /// <summary>What it is, in words: mixin, model, type, function.</summary>
            public string What => IsMixin ? "mixin" : Kind == "fn" ? "function" : Kind;

            public string Anchor => "decl-" + Html.Slug(Name);
        }

        /// <summary>Every model, mixin, type, enum, error, function and constant the packages ship as KScript.</summary>
        internal IEnumerable<PackageDeclaration> PackageDeclarations()
        {
            foreach (PackageSource source in Catalog.PackageSources.OrderBy(s => s.Package, StringComparer.Ordinal))
            {
                MarkupDocument doc = CodeParser.Parse(source.Text);
                foreach (MarkupElement e in doc.RootElements.FirstOrDefault()?.ChildElements ?? Enumerable.Empty<MarkupElement>())
                {
                    if (e.Name is "model" or "type" or "enum" or "error" or "fn" or "const" && e.GetAttributeValue("name") is string name)
                        yield return new PackageDeclaration(source.Package, e.Name, name.Trim(), e);
                }
            }
        }

        private static string Summary(MarkupElement e) => DocComment.Parse(e.Documentation)?.Summary?.Trim() ?? string.Empty;

        private void AddModelsPage()
        {
            ElementInfo model = Catalog.GetElement("model");
            var declarations = PackageDeclarations().ToList();
            var sb = new StringBuilder();
            var md = new StringBuilder("# Models: options and built-in mixins\n\nEvery option a model takes, and every model and mixin the packages ship.\n");
            sb.Append("<h1>Models: options and built-in mixins</h1><p class=\"lead\">Every option a <code>model</code> takes, such as <code>soft_deletes</code> and <code>mixin</code>, and every model and mixin that comes with a package, such as <code>Ownable</code>. How models work is in <a href=\"/guide/data\">Databases: KScript.Data</a>.</p>");
            var page = new DocPage { Path = ModelsPath, Title = "Models: options and built-in mixins", NavTitle = "Models and mixins", Section = "Reference", Description = "Every option a model takes (soft_deletes, timestamps, mixin, ...) and every built-in model and mixin, such as Ownable." };

            sb.Append("<h2 id=\"options\">Options</h2><p>Options go in brackets after the model's name. An option that is on by itself is written as a bare word: <code>model Task(soft_deletes, unique: [title]) { ... }</code>.</p>");
            page.Toc.Add((2, "options", "Options"));
            md.Append("\n## Options\n\n| Option | What it does |\n| --- | --- |\n");
            sb.Append("<div class=\"table\"><table><thead><tr><th>Option</th><th>What it does</th></tr></thead><tbody>");
            foreach (AttributeInfo a in model.Attributes.Where(a => a.Name is not ("name" or "description")))
            {
                string id = "option-" + Html.Slug(a.Name);
                sb.Append($"<tr id=\"{id}\"><td><code>{Html.H(a.Name)}</code></td><td>{Html.Inline(a.Description)}</td></tr>");
                md.Append($"| `{a.Name}` | {a.Description} |\n");
                Search.Add(new SearchEntry { Title = a.Name, CodeTitle = a.Name, Url = ModelsPath + "#" + id, Section = "Models", Kind = "option of model", Text = Shorten(Html.Text(Html.Inline(a.Description))) });
            }
            sb.Append("</tbody></table></div>");

            sb.Append("<h2 id=\"built-in\">Built-in models and mixins</h2><p>These come with a package, written in KScript. Import the package, then extend a mixin to give a model its fields and methods: <code>model Note extends Ownable { ... }</code>. Everything every model gets, such as <code>-&gt;where()</code> and <code>-&gt;save()</code>, is under <a href=\"" + TypeSlug("model") + "\">model methods</a> and <a href=\"" + TypeSlug("record") + "\">record methods</a>.</p>");
            page.Toc.Add((2, "built-in", "Built-in models and mixins"));
            md.Append("\n## Built-in models and mixins\n");
            foreach (PackageDeclaration d in declarations)
            {
                string summary = Summary(d.Element);
                sb.Append($"<h3 id=\"{d.Anchor}\"><code>{Html.H(d.Name)}</code> <span class=\"muted\">{d.What} in <a href=\"{PackageSlug(d.Package)}\">{Html.H(d.Package)}</a></span></h3>");
                page.Toc.Add((3, d.Anchor, d.Name));
                md.Append($"\n### {d.Name}\n\nA {d.What} in {d.Package}.\n\n{summary}\n");
                if (summary.Length > 0) sb.Append($"<p>{Html.Inline(summary)}</p>");
                Search.Add(new SearchEntry { Title = d.Name, CodeTitle = d.Name, Url = ModelsPath + "#" + d.Anchor, Section = d.Package, Kind = d.What, Text = Shorten(Html.Text(Html.Inline(summary))) });

                if (d.Kind == "model")
                {
                    string use = d.IsMixin ? $"import KScript.Data\nimport {d.Package}\n\nmodel Note extends {d.Name} {{\n  text: string\n}}" : $"import KScript.Data\nimport {d.Package}\n\nrows = {d.Name}->all()";
                    sb.Append(Html.Code(use, "kscript"));
                    md.Append($"\n```kscript\n{use}\n```\n");
                    string options = string.Join(", ", d.Element.Attributes.Where(a => a.Name is not ("name" or "description")).Select(a => a.ValueText.Trim() is "yes" or "true" ? a.Name : $"{a.Name}: {a.ValueText.Trim()}"));
                    if (options.Length > 0) sb.Append($"<p>Options: <code>{Html.H(options)}</code>.</p>");

                    var fields = d.Element.ChildElements.Where(c => c.Name == "field").ToList();
                    if (fields.Count > 0)
                    {
                        sb.Append("<div class=\"table\"><table><thead><tr><th>Field</th><th>Type</th><th>What it holds</th></tr></thead><tbody>");
                        md.Append("\n| Field | Type | What it holds |\n| --- | --- | --- |\n");
                        foreach (MarkupElement f in fields)
                        {
                            string doc = Summary(f);
                            sb.Append($"<tr><td><code>{Html.H(f.GetAttributeValue("name"))}</code></td><td><code>{Html.H(f.GetAttributeValue("type") ?? "any")}</code></td><td>{Html.Inline(doc)}</td></tr>");
                            md.Append($"| `{f.GetAttributeValue("name")}` | `{f.GetAttributeValue("type") ?? "any"}` | {doc} |\n");
                        }
                        sb.Append("</tbody></table></div>");
                    }

                    var methods = d.Element.ChildElements.Where(c => c.Name == "fn").ToList();
                    if (methods.Count > 0)
                    {
                        sb.Append("<div class=\"table\"><table><thead><tr><th>Method</th><th>What it does</th></tr></thead><tbody>");
                        md.Append("\n| Method | What it does |\n| --- | --- |\n");
                        foreach (MarkupElement fn in methods)
                        {
                            // A scope's first parameter is the query; callers don't pass it.
                            string[] parameters = (fn.GetAttributeValue("params") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                            bool scope = parameters.FirstOrDefault()?.Split(':')[0].Trim() == "query";
                            string call = $"{(scope ? (d.IsMixin ? "Note" : d.Name) : "$record")}->{fn.GetAttributeValue("name")}({string.Join(", ", parameters.Skip(scope ? 1 : 0))})";
                            string doc = (scope ? "A scope. " : string.Empty) + Summary(fn);
                            sb.Append($"<tr><td><code>{Html.H(call)}</code></td><td>{Html.Inline(doc)}</td></tr>");
                            md.Append($"| `{call}` | {doc} |\n");
                        }
                        sb.Append("</tbody></table></div>");
                    }
                }
            }

            page.Body = sb.ToString();
            page.Markdown = md.ToString();
            Add(page);
        }
    }
}
