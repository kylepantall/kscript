using System.Text;

namespace KScript.Docs
{
    /// <summary>The home, command line, playground and not-found pages, and the layout every page uses.</summary>
    public sealed partial class DocsSite
    {
        internal const string HelloExample = "<let name=\"name\" value=\"'World'\" />\n<echo>Hello, $name!</echo>\n\n<let name=\"people\" value=\"[{ name: 'Joan', age: 82 }, { name: 'Sam', age: 35 }]\" />\n<for each=\"p\" in=\"$people->sort_by(fn($p) => $p.age)\">\n  <echo>{ $p.name } is { $p.age }</echo>\n</for>\n";

        private void AddHome()
        {
            var sb = new StringBuilder();
            sb.Append("<div class=\"hero\"><h1>KScript</h1><p class=\"lead\">A small scripting language for automating your computer, building web pages and APIs, and making desktop apps. Scripts are XML or the brace-style <code>.kscript</code> format, and both run on Windows, macOS and Linux.</p>");
            sb.Append("<p class=\"hero-links\"><a class=\"button primary\" href=\"/guide/getting-started\">Get started</a><a class=\"button\" href=\"/reference\">Reference</a><a class=\"button\" href=\"/playground\">Playground</a></p></div>");
            sb.Append("<h2 id=\"try-it\">Try it</h2><p>Every example marked <em>Run</em> can be edited and run right here. It runs on your computer, in a sandbox: its own empty folder, no network, and a time limit. Switch between XML and <code>.kscript</code> to see the same script in both formats.</p>");
            sb.Append(Html.Try(HelloExample));
            sb.Append("<h2 id=\"guides\">Guides</h2><div class=\"cards\">");
            // FillHomeCards adds the cards once every guide is in.
            sb.Append("</div>");
            var home = new DocPage { Path = "/", Title = "KScript docs", NavTitle = "Home", Section = "Start here", Description = "Guides, reference and runnable examples for KScript.", Body = sb.ToString() };
            home.Toc.Add((2, "try-it", "Try it"));
            home.Toc.Add((2, "guides", "Guides"));
            Add(home);
        }

        private static string Card(DocPage page) =>
            $"<a class=\"card\" href=\"{page.Path}\"><strong>{Html.H(page.Title)}</strong><span>{Html.H(Shorten(page.Description, 120))}</span></a>";

        /// <summary>The guide cards on the home page are filled in once every guide is added.</summary>
        private void FillHomeCards()
        {
            DocPage home = Find("/");
            var cards = new StringBuilder();
            foreach (DocPage guide in Pages.Where(p => p.Source != null)) cards.Append(Card(guide));
            home.Body = home.Body.Replace("<h2 id=\"guides\">Guides</h2><div class=\"cards\"></div>", $"<h2 id=\"guides\">Guides</h2><div class=\"cards\">{cards}</div>");
        }

        private void AddCli()
        {
            FillHomeCards();
            var sb = new StringBuilder();
            sb.Append("<h1>Command line</h1><p class=\"lead\">Everything the <code>kscript</code> command does. <code>kscript --help</code> prints the same list.</p>");
            var page = new DocPage { Path = "/cli", Title = "Command line", Section = "Tools", Description = "Every kscript command and its options." };
            sb.Append("<div class=\"table\"><table><thead><tr><th>Command</th><th>What it does</th></tr></thead><tbody>");
            foreach (CliCommand c in CliReference.Commands)
                sb.Append($"<tr><td><a href=\"#cli-{c.Name}\"><code>{Html.H(c.Name == "run" ? "kscript <script>" : c.Name == "version" ? "kscript --version" : "kscript " + c.Name)}</code></a></td><td>{Html.H(c.Summary)}</td></tr>");
            sb.Append("</tbody></table></div>");
            foreach (CliCommand c in CliReference.Commands)
            {
                string id = "cli-" + c.Name;
                string title = c.Name == "run" ? "Running a script" : c.Name == "version" ? "--version and --help" : "kscript " + c.Name;
                sb.Append($"<section class=\"entry\" id=\"{id}\"><h2><a class=\"anchor\" href=\"#{id}\">#</a>{Html.H(title)}</h2>");
                sb.Append(Html.Code(c.Usage, "sh"));
                sb.Append($"<p>{Html.H(c.Summary)}</p>");
                if (c.Options.Count > 0)
                {
                    sb.Append("<div class=\"table\"><table class=\"params\"><thead><tr><th>Option</th><th>Description</th></tr></thead><tbody>");
                    foreach (CliOption o in c.Options) sb.Append($"<tr><td><code>{Html.H(o.Name)}</code></td><td>{Html.H(o.Description)}</td></tr>");
                    sb.Append("</tbody></table></div>");
                }
                if (c.Example != null)
                {
                    sb.Append("<div class=\"example\"><div class=\"example-label\">Example</div>").Append(Html.Code(c.Example, "sh")).Append("</div>");
                }
                if (c.Guide != null && Guides.FirstOrDefault(g => g.Slug == c.Guide) is Guide guide)
                    sb.Append($"<p class=\"hint\">More in <a href=\"/guide/{guide.Slug}\">{Html.H(guide.NavTitle)}</a>.</p>");
                sb.Append("</section>");
                page.Toc.Add((2, id, title));
                Search.Add(new SearchEntry { Title = c.Name == "run" ? "kscript <script>" : "kscript " + c.Name, Url = "/cli#" + id, Section = "Command line", Kind = "cli", Text = c.Summary });
            }
            page.Body = sb.ToString();
            Add(page);
        }

        private void AddPlayground()
        {
            var sb = new StringBuilder();
            sb.Append("<h1>Playground</h1><p class=\"lead\">Write any KScript and run it. Press <kbd>Ctrl</kbd>+<kbd>Enter</kbd> (<kbd>⌘</kbd>+<kbd>Enter</kbd> on a Mac) to run.</p>");
            sb.Append($"<p>Scripts here can use KScript.Core, {string.Join(", ", Playground.Packages.Take(Playground.Packages.Count - 1))} and {Playground.Packages.Last()}. Each run has its own empty folder to read and write files in, which is deleted afterwards, and stops after {Playground.DefaultTimeout.TotalSeconds:0} seconds. Anything you type into <em>Input</em> is what <code>&lt;input&gt;</code> and the other prompts read.</p>");
            sb.Append("<div class=\"playground\">").Append(Html.Try(HelloExample, title: "playground")).Append("</div>");
            Add(new DocPage { Path = "/playground", Title = "Playground", Section = "Start here", Description = "Write and run KScript in the browser.", Body = sb.ToString() });
        }

        private void AddNotFound()
        {
            Pages.Add(new DocPage
            {
                Path = "/404",
                Title = "Page not found",
                Body = "<h1>Page not found</h1><p class=\"lead\">There's no page here. Try the search above, or start from the <a href=\"/\">home page</a> or the <a href=\"/reference\">reference</a>.</p>",
            });
        }

        /// <summary>A whole HTML page: the header with search and the theme switch, the sidebar, the page, and "On this page".</summary>
        public string Render(DocPage page)
        {
            var sb = new StringBuilder();
            string title = page.Path == "/" ? "KScript docs" : page.Title + " · KScript docs";
            string format = Format == "xml" ? "xml" : "kscript";
            sb.Append($"<!doctype html><html lang=\"en\" data-format=\"{format}\" data-default-format=\"{format}\"><head><meta charset=\"utf-8\">");
            sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
            sb.Append($"<title>{Html.H(title)}</title>");
            if (!string.IsNullOrEmpty(page.Description)) sb.Append($"<meta name=\"description\" content=\"{Html.H(page.Description)}\">");
            // Set the theme before the page draws, so dark mode doesn't flash white.
            // ...and the reader's example format, so a page doesn't switch from one format to the other as it loads.
            sb.Append("<script>try{var d=document.documentElement,t=localStorage.getItem('kscript-docs-theme'),f=sessionStorage.getItem('kscript-docs-format');if(t)d.dataset.theme=t;if(f)d.dataset.format=f;}catch(e){}</script>");
            sb.Append("<link rel=\"stylesheet\" href=\"/assets/docs.css\"><link rel=\"icon\" href=\"/assets/favicon.svg\" type=\"image/svg+xml\"></head><body>");
            sb.Append("<a class=\"skip\" href=\"#content\">Skip to content</a>");
            sb.Append("<header class=\"top\"><button type=\"button\" class=\"icon menu-toggle\" aria-label=\"Menu\" aria-expanded=\"false\"><svg viewBox=\"0 0 24 24\" width=\"20\" height=\"20\"><path d=\"M3 6h18M3 12h18M3 18h18\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\"/></svg></button>");
            sb.Append($"<a class=\"brand\" href=\"/\"><img src=\"/assets/favicon.svg\" alt=\"\" width=\"24\" height=\"24\"><span>KScript</span> <span class=\"brand-docs\">docs</span></a><span class=\"version\">{Html.H(Version)}</span>");
            sb.Append("<div class=\"search\" role=\"search\"><input type=\"search\" id=\"search\" placeholder=\"Search\" aria-label=\"Search the docs\" autocomplete=\"off\" spellcheck=\"false\"><kbd class=\"search-key\">/</kbd><div class=\"results\" id=\"search-results\" role=\"listbox\" hidden></div></div>");
            sb.Append("<div class=\"fmt-switch fmt-tabs\" role=\"group\" aria-label=\"Show examples as\" title=\"Show examples as .kscript or XML\"><button type=\"button\" data-format=\"kscript\">.kscript</button><button type=\"button\" data-format=\"xml\">XML</button></div>");
            sb.Append("<button type=\"button\" class=\"icon theme-toggle\" aria-label=\"Switch between light and dark\" title=\"Light or dark\"><svg class=\"sun\" viewBox=\"0 0 24 24\" width=\"20\" height=\"20\"><circle cx=\"12\" cy=\"12\" r=\"4\" fill=\"currentColor\"/><path d=\"M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\"/></svg><svg class=\"moon\" viewBox=\"0 0 24 24\" width=\"20\" height=\"20\"><path d=\"M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z\" fill=\"currentColor\"/></svg></button></header>");

            sb.Append("<div class=\"layout\"><nav class=\"sidebar\" aria-label=\"Docs\">");
            foreach (var (section, pages) in Navigation())
            {
                bool open = pages.Any(p => p.Path == page.Path) || section != "Methods by type";
                sb.Append($"<details class=\"nav-section\"{(open ? " open" : "")}><summary>{Html.H(section)}</summary><ul>");
                foreach (DocPage p in pages)
                    sb.Append($"<li><a href=\"{p.Path}\"{(p.Path == page.Path ? " aria-current=\"page\"" : "")}>{Html.H(p.NavTitle ?? p.Title)}</a></li>");
                sb.Append("</ul></details>");
            }
            sb.Append("</nav>");

            sb.Append($"<main class=\"content\" id=\"content\"><article class=\"doc\">{page.Body}</article>");
            var order = Navigation().SelectMany(n => n.Pages).ToList();
            int at = order.FindIndex(p => p.Path == page.Path);
            if (at >= 0)
            {
                sb.Append("<nav class=\"pager\" aria-label=\"Previous and next\">");
                if (at > 0) sb.Append($"<a class=\"prev\" href=\"{order[at - 1].Path}\"><span>Previous</span>{Html.H(order[at - 1].Title)}</a>");
                if (at < order.Count - 1) sb.Append($"<a class=\"next\" href=\"{order[at + 1].Path}\"><span>Next</span>{Html.H(order[at + 1].Title)}</a>");
                sb.Append("</nav>");
            }
            if (page.Source != null) sb.Append($"<p class=\"edit\"><a href=\"{Repository}/blob/master/docs/{page.Source}\">Edit this page on GitHub</a></p>");
            sb.Append("</main>");

            sb.Append("<aside class=\"toc\" aria-label=\"On this page\">");
            if (page.Toc.Count > 1)
            {
                sb.Append("<div class=\"toc-title\">On this page</div><ul>");
                foreach (var (level, id, text) in page.Toc)
                    sb.Append($"<li class=\"l{level}\"><a href=\"#{Html.H(id)}\">{Html.H(text)}</a></li>");
                sb.Append("</ul>");
            }
            sb.Append("</aside></div>");
            sb.Append("<script src=\"/assets/docs.js\"></script></body></html>");
            return sb.ToString();
        }
    }
}
