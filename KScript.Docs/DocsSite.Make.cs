using System.Text;

using KScript.Scaffolding;

namespace KScript.Docs
{
    /// <summary>The kscript make command directory: every type of file it makes, with its options and examples.</summary>
    public sealed partial class DocsSite
    {
        private void AddMakeCommands()
        {
            var page = new DocPage
            {
                Path = "/cli/make",
                Title = "kscript make commands",
                NavTitle = "Make commands",
                Section = "Tools",
                Description = "Every file kscript make generates (routes, controllers, requests, views, stylesheets, types and more) with its options and an example.",
            };
            var sb = new StringBuilder();
            sb.Append("<h1>kscript make commands</h1><p class=\"lead\"><code>kscript make &lt;type&gt; &lt;name&gt;</code> makes a file where the project keeps that kind of file, and makes any folder it needs. Scripts are written in the project's format (<code>.kscript</code> unless <code>make.syntax</code> in kscript.json says <code>xml</code>). <code>kscript make --list</code> prints this list, and <code>kscript make &lt;type&gt; --help</code> prints one entry.</p>");
            sb.Append("<p>Every type comes from a template. <code>kscript make templates</code> copies them to the project's <code>_templates</code> folder, where you can change what they make or add types of your own. Add <code>--dry-run</code> to any command to see what it would make, and <code>--force</code> to replace a file. In a new project, <code>make route name=users</code> runs <code>kscript make route users</code> (see <a href=\"/guide/projects\">Projects</a>).</p>");

            var groups = MakeCommand.Grouped(Scaffolder.BuiltInTemplates());
            sb.Append("<div class=\"table\"><table><thead><tr><th>Command</th><th>What it makes</th></tr></thead><tbody>");
            foreach (var (_, types) in groups)
                foreach (StubInfo t in types)
                    sb.Append($"<tr><td><a href=\"#make-{t.Type}\"><code>kscript make {Html.H(t.Type)}</code></a></td><td>{Html.H(t.Description ?? "")}</td></tr>");
            sb.Append("</tbody></table></div>");

            foreach (var (category, types) in groups)
            {
                string groupId = "make-" + Slug(category);
                sb.Append($"<h2 id=\"{groupId}\"><a class=\"anchor\" href=\"#{groupId}\">#</a>{Html.H(category)}</h2>");
                page.Toc.Add((2, groupId, category));
                foreach (StubInfo t in types)
                {
                    string id = "make-" + t.Type;
                    sb.Append($"<section class=\"entry\" id=\"{id}\"><h3><a class=\"anchor\" href=\"#{id}\">#</a>kscript make {Html.H(t.Type)}</h3>");
                    sb.Append(Html.Code($"kscript make {t.Type} {t.Usage ?? "<name>"}", "sh"));
                    if (t.Description != null) sb.Append($"<p>{Html.H(t.Description)}</p>");
                    if (t.Options.Count > 0)
                    {
                        sb.Append("<div class=\"table\"><table class=\"params\"><thead><tr><th>Option</th><th>Description</th></tr></thead><tbody>");
                        foreach (StubOption o in t.Options) sb.Append($"<tr><td><code>{Html.H(o.Name)}</code></td><td>{Html.H(o.Description)}</td></tr>");
                        sb.Append("</tbody></table></div>");
                    }
                    if (t.Examples.Count > 0)
                        sb.Append("<div class=\"example\"><div class=\"example-label\">Example</div>").Append(Html.Code(string.Join("\n", t.Examples), "sh")).Append("</div>");
                    sb.Append("</section>");
                    page.Toc.Add((3, id, t.Type));
                    Search.Add(new SearchEntry { Title = "kscript make " + t.Type, Url = "/cli/make#" + id, Section = "Make commands", Kind = "cli", Text = t.Description ?? "" });
                }
            }
            page.Body = sb.ToString();
            Add(page);
        }

        private static string Slug(string text) =>
            System.Text.RegularExpressions.Regex.Replace(text.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
    }
}
