using System.Text;

using KScript.Runtime;

namespace KScript.Docs
{
    /// <summary>
    /// The environment variables page: every variable kscript reads, from <see cref="EnvironmentVariables.All"/>, with
    /// what it does, its default and a line that sets it. A test fails when kscript reads a KSCRIPT_ variable that
    /// isn't on the list, so the page can't fall behind.
    /// </summary>
    public sealed partial class DocsSite
    {
        internal const string EnvironmentPath = "/reference/environment";

        internal static string EnvironmentAnchor(EnvironmentVariables.Variable v) => "env-" + Html.Slug(v.Name.ToLowerInvariant().Replace('_', '-'));

        private void AddEnvironmentPage()
        {
            const string title = "Environment variables";
            const string lead = "Every environment variable kscript reads. Most projects need none of them: settings belong in kscript.json and the config files. These are for what changes from one computer, container or run to the next, such as the environment a server runs in or where it keeps its data.";
            var sb = new StringBuilder($"<h1>{title}</h1><p class=\"lead\">{Html.Inline(lead)}</p>");
            var md = new StringBuilder($"# {title}\n\n{lead}\n");
            var page = new DocPage { Path = EnvironmentPath, Title = title, NavTitle = "Environment variables", Section = "Reference", Description = "Every environment variable kscript reads: KSCRIPT_ENV, KSCRIPT_SECRET, KSCRIPT_STORAGE_ROOT and the rest, with defaults and examples." };

            const string intro = "Set a variable for one command by putting it in front, or export it in your shell, CI settings or Dockerfile. Values from a project's `.env` files become environment variables too, unless one is already set.";
            const string example = "KSCRIPT_ENV=staging kscript serve\nexport KSCRIPT_DEBUG=1";
            sb.Append($"<p>{Html.Inline(intro)}</p>").Append(Html.Code(example, "sh"));
            md.Append($"\n{intro}\n\n```sh\n{example}\n```\n");

            foreach (string area in EnvironmentVariables.Areas)
            {
                string areaId = Html.Slug(area);
                sb.Append($"<h2 id=\"{areaId}\">{Html.H(area)}</h2>");
                md.Append($"\n## {area}\n");
                page.Toc.Add((2, areaId, area));
                foreach (EnvironmentVariables.Variable v in EnvironmentVariables.All.Where(v => v.Area == area))
                {
                    string id = EnvironmentAnchor(v);
                    string more = v.Guide == null ? string.Empty : $" More in [{GuideTitle(v.Guide)}]({v.Guide}).";
                    sb.Append($"<h3 id=\"{id}\"><code>{Html.H(v.Name)}</code></h3><p>{Html.Inline(v.Description + more)}</p><p>{Html.Inline("When it isn't set: " + v.Default + ".")}</p>");
                    sb.Append(Html.Code(v.Example, "sh"));
                    md.Append($"\n### `{v.Name}`\n\n{v.Description}{more}\n\nWhen it isn't set: {v.Default}.\n\n```sh\n{v.Example}\n```\n");
                    page.Toc.Add((3, id, v.Name));
                    Search.Add(new SearchEntry { Title = v.Name, CodeTitle = v.Name, Url = EnvironmentPath + "#" + id, Section = title, Kind = "environment variable", Text = Shorten(Html.Text(Html.Inline(v.Description))) });
                }
            }

            page.Body = sb.ToString();
            page.Markdown = md.ToString();
            Add(page);
        }

        /// <summary>The sidebar title of a guide or reference page, for "More in ..." links.</summary>
        private static string GuideTitle(string path)
        {
            string slug = path.StartsWith("/guide/", StringComparison.Ordinal) ? path.Substring(7) : null;
            Guide guide = Guides.FirstOrDefault(g => g.Slug == slug);
            return guide?.NavTitle ?? (path == EditorPath ? "VS Code" : path);
        }

        /// <summary>The A to Z entries for the environment variables page.</summary>
        internal IEnumerable<IndexEntry> EnvironmentIndexEntries() =>
            EnvironmentVariables.All.Select(v => new IndexEntry(v.Name, "environment variable", EnvironmentPath + "#" + EnvironmentAnchor(v), v.Description));
    }
}
