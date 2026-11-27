using System.Text;

namespace KScript.Docs
{
    /// <summary>
    /// The contents page: every guide with every topic and sub-heading in it, made from the guides' own headings, so a
    /// topic such as "Built-in mixins" can be found without knowing which guide it is in.
    /// </summary>
    public sealed partial class DocsSite
    {
        private void AddContents()
        {
            var sb = new StringBuilder("<h1>Contents</h1><p class=\"lead\">Every guide and every topic in it. Each topic has its own page; the names under it link to the place in the guide. For every built-in name, see the <a href=\"/reference/index\">A to Z</a>.</p>");
            var md = new StringBuilder("# Contents\n\nEvery guide and every topic in it.\n");
            var page = new DocPage { Path = "/guide", Title = "Contents", NavTitle = "Contents", Section = "Start here", Description = "Every guide and every topic in it, from the guides' own headings." };

            var guides = Pages.Where(p => p.Source != null && p.ParentPath == null).ToList();
            foreach (var section in guides.GroupBy(g => g.Section))
            {
                string sectionId = "contents-" + Html.Slug(section.Key);
                sb.Append($"<h2 id=\"{sectionId}\">{Html.H(section.Key)}</h2>");
                page.Toc.Add((2, sectionId, section.Key));
                md.Append($"\n## {section.Key}\n");
                foreach (DocPage guide in section)
                {
                    sb.Append($"<h3 id=\"contents-guide-{guide.Path["/guide/".Length..]}\"><a href=\"{guide.Path}\">{Html.H(guide.NavTitle ?? guide.Title)}</a></h3>");
                    md.Append($"\n### [{guide.NavTitle ?? guide.Title}]({guide.Path})\n\n");
                    // Every guide starts its headings with a ## (a test keeps it that way), so a ### always has a topic to go under.
                    sb.Append("<ul class=\"contents\">");
                    bool open = false;
                    foreach (var (level, id, text) in guide.Toc)
                    {
                        DocPage topic = guide.Topics.FirstOrDefault(t => t.Path == guide.Path + "/" + id);
                        if (level == 2)
                        {
                            if (open) sb.Append("</ul></li>");
                            sb.Append($"<li><a href=\"{topic?.Path ?? guide.Path + "#" + id}\">{Html.H(text)}</a><ul>");
                            md.Append($"- [{text}]({topic?.Path ?? guide.Path + "#" + id})\n");
                            open = true;
                        }
                        else
                        {
                            sb.Append($"<li><a href=\"{guide.Path}#{id}\">{Html.H(text)}</a></li>");
                            md.Append($"  - [{text}]({guide.Path}#{id})\n");
                        }
                    }

                    if (open) sb.Append("</ul></li>");
                    sb.Append("</ul>");
                }
            }

            page.Body = sb.ToString().Replace("<ul></ul>", string.Empty);
            page.Markdown = md.ToString();
            Add(page);

            // Show it in the sidebar straight after the home page.
            Pages.Remove(page);
            Pages.Insert(Pages.FindIndex(p => p.Path == "/") + 1, page);
        }
    }
}
