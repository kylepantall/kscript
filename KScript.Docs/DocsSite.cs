using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using KScript.Language.Catalog;

namespace KScript.Docs
{
    /// <summary>One page of the docs site.</summary>
    public sealed class DocPage
    {
        /// <summary>The page's URL path, such as <c>/guide/language</c> (the home page is <c>/</c>).</summary>
        public string Path { get; init; }
        public string Title { get; init; }

        /// <summary>The sidebar section the page is in, such as Language or Reference.</summary>
        public string Section { get; init; }

        /// <summary>One sentence for search results and the page's description.</summary>
        public string Description { get; init; }

        /// <summary>The page's content, as HTML.</summary>
        public string Body { get; set; }

        /// <summary>The page's h2 and h3 headings, for "On this page".</summary>
        public List<(int Level, string Id, string Text)> Toc { get; } = new List<(int, string, string)>();

        /// <summary>Shorter text for the sidebar, when the title is long.</summary>
        public string NavTitle { get; init; }

        /// <summary>The file in docs/ the page was made from, or null for generated pages.</summary>
        public string Source { get; init; }
    }

    /// <summary>One thing search can find: a page, a heading or a reference entry.</summary>
    public sealed class SearchEntry
    {
        public string Title { get; init; }
        public string Url { get; init; }
        public string Section { get; init; }

        /// <summary>page, heading, element, command, method, variable or cli.</summary>
        public string Kind { get; init; }
        public string Text { get; init; }
    }

    /// <summary>
    /// The docs website: guides from docs/*.md (built into kscript) and a reference generated from the language
    /// catalog, the same one the editor uses, so every package, element, command, method and variable is described
    /// exactly as the interpreter knows it.
    /// </summary>
    public sealed partial class DocsSite
    {
        public const string Repository = "https://github.com/kylepantall/kscript";

        /// <summary>A guide in docs/: its URL slug, file, sidebar section and title.</summary>
        public sealed record Guide(string Slug, string File, string Section, string NavTitle, string FenceLanguage = "text");

        /// <summary>The guides, in sidebar order.</summary>
        public static readonly IReadOnlyList<Guide> Guides = new[]
        {
            new Guide("getting-started", "getting-started.md", "Start here", "Getting started", "xml"),
            new Guide("install", "install.md", "Start here", "Install and editors"),
            new Guide("language", "language.md", "Language", "The language", "xml"),
            new Guide("code-format", "code-format.md", "Language", "The .kscript format", "kscript"),
            new Guide("web", "web.md", "Build", "Web pages"),
            new Guide("http", "http.md", "Build", "Routes and APIs"),
            new Guide("desktop", "desktop.md", "Build", "Desktop apps"),
            new Guide("stylesheets", "stylesheets.md", "Build", "Stylesheets"),
            new Guide("testing", "testing.md", "Build", "Testing"),
            new Guide("projects", "projects.md", "Tools", "Projects and kscript.json"),
            new Guide("formatting", "formatting.md", "Tools", "Formatting"),
            new Guide("standards", "standards.md", "Tools", "Lint standards"),
            new Guide("roadmap", "roadmap.md", "More", "Roadmap"),
        };

        /// <summary>
        /// Files in docs/ that are for people working on KScript itself, not for people using it, so the site leaves
        /// them out (links to them go to GitHub).
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> NotGuides = new Dictionary<string, string>
        {
            ["contributing.md"] = "building KScript from source and its CI, for contributors",
            ["language-server.md"] = "how the language server is built, for contributors",
        };

        private static readonly string[] SectionOrder = { "Start here", "Language", "Build", "Tools", "Reference", "Methods by type", "More" };

        public List<DocPage> Pages { get; } = new List<DocPage>();
        public List<SearchEntry> Search { get; } = new List<SearchEntry>();
        public LanguageCatalog Catalog { get; }
        public string Version { get; }

        private DocsSite(LanguageCatalog catalog, string version)
        {
            Catalog = catalog;
            Version = version;
        }

        /// <summary>Builds every page. <paramref name="readGuide"/> reads a docs/ file (by default, the copy built into kscript).</summary>
        public static DocsSite Build(LanguageCatalog catalog = null, string version = null, Func<string, string> readGuide = null)
        {
            var site = new DocsSite(catalog ?? LanguageCatalog.Default, version ?? KScript.Updates.SelfUpdater.CurrentVersion);
            readGuide ??= ReadEmbeddedGuide;

            site.AddHome();
            foreach (Guide guide in Guides)
            {
                string markdown = readGuide(guide.File);
                if (markdown != null) site.AddGuide(guide, markdown);
            }
            site.AddReference();
            site.AddCli();
            site.AddPlayground();
            site.AddNotFound();
            return site;
        }

        public DocPage Find(string path)
        {
            path = "/" + (path ?? string.Empty).Trim('/');
            return Pages.FirstOrDefault(p => p.Path == path);
        }

        #region Embedded files

        public static string ReadEmbeddedGuide(string file) => ReadResource("KScript.Docs.Guides." + file);

        public static string ReadAsset(string file) => ReadResource("KScript.Docs.Assets." + file);

        public static IEnumerable<string> GuideFiles =>
            typeof(DocsSite).Assembly.GetManifestResourceNames().Where(n => n.StartsWith("KScript.Docs.Guides.", StringComparison.Ordinal)).Select(n => n.Substring(20));

        private static string ReadResource(string name)
        {
            using Stream stream = typeof(DocsSite).Assembly.GetManifestResourceStream(name);
            if (stream == null) return null;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        #endregion

        #region Guides

        private static readonly Regex Fence = new Regex(@"^```([^\n]*)\n(.*?)^```[ \t]*$", RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex Heading = new Regex(@"<h([1-3]) id=""([^""]+)"">(.*?)</h\1>", RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex Href = new Regex(@"href=""([^""#:]*)(#[^""]*)?""", RegexOptions.Compiled);

        /// <summary>The URL of a link in a guide: other guides stay on the site; files in the repository go to GitHub.</summary>
        internal static string LinkTarget(string target, string anchor)
        {
            if (target.Length == 0) return anchor;
            string file = target.Replace("docs/", string.Empty);
            Guide guide = Guides.FirstOrDefault(g => string.Equals(g.File, file, StringComparison.OrdinalIgnoreCase));
            if (guide != null) return "/guide/" + guide.Slug + anchor;
            if (target.StartsWith("/", StringComparison.Ordinal)) return target + anchor;
            string path = target.StartsWith("../", StringComparison.Ordinal) ? target.Substring(3) : "docs/" + target;
            bool isFile = System.IO.Path.HasExtension(path.TrimEnd('/'));
            return $"{Repository}/{(isFile ? "blob" : "tree")}/master/{path.TrimEnd('/')}{anchor}";
        }

        internal void AddGuide(Guide guide, string markdown)
        {
            var blocks = new List<string>();
            string withPlaceholders = Fence.Replace(markdown.Replace("\r\n", "\n"), m =>
            {
                string[] info = m.Groups[1].Value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string language = info.Length > 0 ? info[0] : guide.FenceLanguage;
                if (language == "try") language = guide.FenceLanguage;
                bool runnable = info.Contains("try") && (language == "xml" || language == "kscript");
                string code = m.Groups[2].Value;
                blocks.Add(runnable ? Html.Try(code, language) : Html.Code(code, language));
                return $"\n<div data-block=\"{blocks.Count - 1}\"></div>\n";
            });

            string html = Markdig.Markdown.ToHtml(withPlaceholders, Html.Pipeline);
            html = Regex.Replace(html, @"<div data-block=""(\d+)""></div>", m => blocks[int.Parse(m.Groups[1].Value)]);
            html = Href.Replace(html, m => $"href=\"{Html.H(LinkTarget(m.Groups[1].Value, m.Groups[2].Value))}\"");
            html = Regex.Replace(html, "<table>", "<div class=\"table\"><table>").Replace("</table>", "</table></div>");

            string title = guide.NavTitle;
            Match h1 = Heading.Match(html);
            if (h1.Success && h1.Groups[1].Value == "1")
            {
                title = Html.Text(h1.Groups[3].Value);
            }

            string description = Html.Text(Regex.Match(html, "<p>(.*?)</p>", RegexOptions.Singleline).Groups[1].Value);
            var page = new DocPage
            {
                Path = "/guide/" + guide.Slug,
                Title = title,
                NavTitle = guide.NavTitle,
                Section = guide.Section,
                Description = Shorten(description),
                Body = html,
                Source = guide.File,
            };
            AddHeadings(page);
            Add(page);
        }

        private void AddHeadings(DocPage page)
        {
            foreach (Match m in Heading.Matches(page.Body))
            {
                int level = int.Parse(m.Groups[1].Value);
                if (level < 2) continue;
                string text = Html.Text(m.Groups[3].Value);
                page.Toc.Add((level, m.Groups[2].Value, text));
                Search.Add(new SearchEntry { Title = text, Url = page.Path + "#" + m.Groups[2].Value, Section = page.Title, Kind = "heading", Text = string.Empty });
            }
        }

        private void Add(DocPage page)
        {
            Pages.Add(page);
            if (page.Path != "/404")
                Search.Add(new SearchEntry { Title = page.Title, Url = page.Path, Section = page.Section, Kind = "page", Text = page.Description });
        }

        internal static string Shorten(string text, int max = 180)
        {
            text = Regex.Replace(text ?? string.Empty, @"\s+", " ").Trim();
            if (text.Length <= max) return text;
            int cut = text.LastIndexOf(' ', max);
            return text.Substring(0, cut > 40 ? cut : max).TrimEnd(',', '.', ';', ':') + "…";
        }

        #endregion

        #region Navigation

        /// <summary>The sidebar: sections in order, each with its pages.</summary>
        public IEnumerable<(string Section, List<DocPage> Pages)> Navigation() =>
            Pages.Where(p => p.Section != null)
                 .GroupBy(p => p.Section)
                 .OrderBy(g => Array.IndexOf(SectionOrder, g.Key) is int i && i >= 0 ? i : 99)
                 .Select(g => (g.Key, g.ToList()));

        #endregion

        #region Output

        /// <summary>The search index the page script loads.</summary>
        public string SearchJson() => JsonSerializer.Serialize(Search.Select(s => new { t = s.Title, u = s.Url, s = s.Section, k = s.Kind, d = s.Text }));

        /// <summary>
        /// Writes the site as a folder that kscript serve can serve: the rendered pages in _pages (never served
        /// directly), the assets, and a routes.ks.xml that answers each URL with its page.
        /// </summary>
        public void WriteTo(string folder)
        {
            Directory.CreateDirectory(folder);
            foreach (DocPage page in Pages)
            {
                string file = System.IO.Path.Combine(folder, "_pages", PageFile(page.Path));
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file));
                File.WriteAllText(file, Render(page));
            }
            string assets = System.IO.Path.Combine(folder, "assets");
            Directory.CreateDirectory(assets);
            foreach (string asset in new[] { "docs.css", "docs.js", "favicon.svg" })
                File.WriteAllText(System.IO.Path.Combine(assets, asset), ReadAsset(asset));
            File.WriteAllText(System.IO.Path.Combine(assets, "search.json"), SearchJson());
            File.WriteAllText(System.IO.Path.Combine(folder, "routes.ks.xml"), RoutesFile);
        }

        /// <summary>The file a page is written to inside _pages: /guide/language is guide/language.html.</summary>
        public static string PageFile(string path) => (path == "/" ? "index" : path.Trim('/')) + ".html";

        /// <summary>The docs site's routes: kscript serve and KScript.Http answer each page URL with its file.</summary>
        public const string RoutesFile = @"<KScript.Routes>
  <import package=""KScript.IO"" />

  <!-- Every page is a rendered file in _pages. The folder starts with _, so it is never served on its own. -->
  <fn name=""page"" params=""path = 'index'"">
    <let name=""file"" value=""'_pages/' + $path->trim_end('/') + '.html'"" />
    <if test=""not @is_file($file)"">
      <return value=""@response(@read_text('_pages/404.html'), status: 404)"" />
    </if>
    <return value=""@read_text($file)"" />
  </fn>

  <Get path=""/"" to=""page"" />
  <!-- Paths with a dot are files (assets/docs.css), which kscript serve sends as they are. -->
  <Get path=""/{path*}"" to=""page"" where=""path: '[A-Za-z0-9/_-]+'"" />
</KScript.Routes>
";

        #endregion
    }
}
