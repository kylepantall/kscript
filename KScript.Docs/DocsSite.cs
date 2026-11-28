using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using KScript.Language.Catalog;
using Markdig.Renderers.Html;
using Markdig.Syntax;

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

        /// <summary>What a table-of-contents entry says in the .kscript format, by id, when it differs (<c>break</c> for <c>&lt;break&gt;</c>).</summary>
        public Dictionary<string, string> TocCode { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Shorter text for the sidebar, when the title is long.</summary>
        public string NavTitle { get; init; }

        /// <summary>The file in docs/ the page was made from, or null for generated pages.</summary>
        public string Source { get; init; }

        internal string ParentPath { get; init; }
        internal string TopicMarkdown { get; init; }

        /// <summary>The page as Markdown, for pages made from code that the MCP server serves as they are.</summary>
        internal string Markdown { get; set; }
        internal List<DocPage> Topics { get; } = new();
    }

    /// <summary>One thing search can find: a page, a heading or a reference entry.</summary>
    public sealed class SearchEntry
    {
        public string Title { get; init; }

        /// <summary>The title in the .kscript format, when it differs: <c>break</c> for <c>&lt;break&gt;</c>.</summary>
        public string CodeTitle { get; init; }

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
            new Guide("types", "types.md", "Language", "Types and validation", "kscript"),
            new Guide("web", "web.md", "Build", "Web pages"),
            new Guide("components", "components.md", "Build", "Components"),
            new Guide("http", "http.md", "Build", "Routes and APIs"),
            new Guide("desktop", "desktop.md", "Build", "Desktop apps"),
            new Guide("compile", "compile.md", "Build", "Programs: kscript compile"),
            new Guide("stylesheets", "stylesheets.md", "Build", "Stylesheets"),
            new Guide("storage", "storage.md", "Build", "App folders: KScript.Storage", "kscript"),
            new Guide("cache", "cache.md", "Build", "Caching: KScript.Cache", "kscript"),
            new Guide("data", "data.md", "Build", "Databases: KScript.Data", "kscript"),
            new Guide("auth", "auth.md", "Build", "Sign-in: KScript.Auth", "kscript"),
            new Guide("queues", "queues.md", "Build", "Queues and jobs: KScript.Queue", "kscript"),
            new Guide("documents", "documents.md", "Build", "Documents: KScript.Document", "kscript"),
            new Guide("testing", "testing.md", "Build", "Testing"),
            new Guide("projects", "projects.md", "Tools", "Projects and kscript.json"),
            new Guide("formatting", "formatting.md", "Tools", "Formatting"),
            new Guide("standards", "standards.md", "Tools", "Lint standards"),
            new Guide("errors", "errors.md", "Tools", "Error codes", "kscript"),
            new Guide("mcp", "mcp.md", "Tools", "AI assistants (MCP)"),
            new Guide("agents", "agents.md", "Tools", "Coding agents", "kscript"),
            new Guide("upgrading", "upgrading.md", "More", "What changed", "kscript"),
            new Guide("roadmap", "roadmap.md", "More", "Roadmap"),
            new Guide("builtin-api-gaps", "builtin-api-gaps.md", "More", "Built-in API gaps"),
        };

        /// <summary>
        /// Files in docs/ that are for people working on KScript itself, not for people using it, so the site leaves
        /// them out (links to them go to GitHub).
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> NotGuides = new Dictionary<string, string>
        {
            ["contributing.md"] = "building KScript from source and its CI, for contributors",
            ["language-server.md"] = "how the language server is built, for contributors",
            ["language-test-plan.md"] = "coverage priorities and test matrices, for contributors",
        };

        private static readonly string[] SectionOrder = { ProjectSection, "Start here", "Language", "Build", "Tools", "Reference", "Methods by type", "More" };

        public List<DocPage> Pages { get; } = new List<DocPage>();
        public List<SearchEntry> Search { get; } = new List<SearchEntry>();
        public LanguageCatalog Catalog { get; }
        public string Version { get; }

        /// <summary>
        /// The format examples show in until the reader picks the other: "kscript" (the default) or "xml", from
        /// <c>kscript docs --syntax</c> or the project's <c>make.syntax</c>. The reader's pick lasts for the browser tab.
        /// </summary>
        public string Format { get; set; } = "kscript";

        private readonly bool includeInstallGuide;

        private DocsSite(LanguageCatalog catalog, string version, bool includeInstallGuide)
        {
            Catalog = catalog;
            Version = version;
            this.includeInstallGuide = includeInstallGuide;
        }

        /// <summary>Builds every page. <paramref name="readGuide"/> reads a docs/ file (by default, the copy built into kscript).</summary>
        public static DocsSite Build(LanguageCatalog catalog = null, string version = null, Func<string, string> readGuide = null, bool includeInstallGuide = true)
        {
            var site = new DocsSite(catalog ?? LanguageCatalog.Default, version ?? KScript.Updates.SelfUpdater.CurrentVersion, includeInstallGuide);
            readGuide ??= ReadEmbeddedGuide;

            site.AddHome();
            foreach (Guide guide in Guides)
            {
                if (!includeInstallGuide && guide.Slug == "install") continue;
                string markdown = readGuide(guide.File);
                if (markdown != null) site.AddGuide(guide, markdown);
            }
            site.AddContents();
            site.AddReference();
            site.AddModelsPage();
            site.AddDirectivesPage();
            site.AddEditorPage();
            site.AddEnvironmentPage();
            site.AddBuiltinPages();
            site.AddCli();
            site.AddMakeCommands();
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

        internal static readonly Regex Fence = new Regex(@"^(`{3,})(?!`)([^`\n]*)\n(.*?)^\1`*[ \t]*$", RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex TableCodeSpan = new Regex(@"<t[dh][^>]*>.*?</t[dh]>", RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex Heading = new Regex(@"<h([1-3]) id=""([^""]+)"">(.*?)</h\1>", RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex Href = new Regex(@"href=""([^""#:]*)(#[^""]*)?""", RegexOptions.Compiled);
        private static readonly Regex LocalLink = new Regex(@"href=""#([^""]+)""", RegexOptions.Compiled);

        /// <summary>The URL of a link in a guide: other guides stay on the site; files in the repository go to GitHub.</summary>
        internal static string LinkTarget(string target, string anchor, bool includeInstallGuide = true)
        {
            if (target.Length == 0) return anchor;
            string file = target.Replace("docs/", string.Empty);
            Guide guide = Guides.FirstOrDefault(g => string.Equals(g.File, file, StringComparison.OrdinalIgnoreCase));
            if (guide != null)
                return !includeInstallGuide && guide.Slug == "install"
                    ? $"{Repository}/blob/master/docs/{guide.File}{anchor}"
                    : "/guide/" + guide.Slug + anchor;
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
                string[] info = m.Groups[2].Value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string language = info.Length > 0 ? info[0] : guide.FenceLanguage;
                if (language == "try") language = guide.FenceLanguage;
                bool runnable = info.Contains("try") && (language == "xml" || language == "kscript");
                string code = m.Groups[3].Value;
                blocks.Add(runnable ? Html.Try(code, language) : (language == "sh" ? Html.CommandTable(code) : null) ?? Html.Code(code, language));
                return $"\n<div data-block=\"{blocks.Count - 1}\"></div>\n";
            });

            string html = Markdig.Markdown.ToHtml(withPlaceholders, Html.Pipeline);
            html = Regex.Replace(html, @"<div data-block=""(\d+)""></div>", m => blocks[int.Parse(m.Groups[1].Value)]);
            html = Href.Replace(html, m => $"href=\"{Html.H(LinkTarget(m.Groups[1].Value, m.Groups[2].Value, includeInstallGuide))}\"");
            html = Regex.Replace(html, "<table>", "<div class=\"table\"><table>").Replace("</table>", "</table></div>");
            // In a table, `a\|b` keeps the bar from ending the cell. Markdig leaves the backslash in a code span; GitHub doesn't.
            html = TableCodeSpan.Replace(html, m => m.Value.Replace("\\|", "|", StringComparison.Ordinal));

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
            Add(page);
            AddGuideTopics(page, markdown);
            AddHeadings(page, index: page.Topics.Count == 0);
        }

        private void AddGuideTopics(DocPage parent, string markdown)
        {
            var sourceHeadings = Markdig.Markdown.Parse(markdown, Html.Pipeline)
                .OfType<HeadingBlock>().Where(h => h.Level == 2).ToArray();
            var rendered = Heading.Matches(parent.Body).Where(h => h.Groups[1].Value == "2").ToArray();
            var sourceById = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < sourceHeadings.Length; i++)
            {
                var source = sourceHeadings[i];
                int end = i + 1 < sourceHeadings.Length ? sourceHeadings[i + 1].Span.Start : markdown.Length;
                if (source.GetAttributes().Id is string id) sourceById[id] = markdown[(source.Span.End + 1)..end];
            }

            for (int i = 0; i < rendered.Length; i++)
            {
                var heading = rendered[i];
                string id = heading.Groups[2].Value;
                if (!sourceById.TryGetValue(id, out string topicMarkdown)) continue;
                int end = i + 1 < rendered.Length ? rendered[i + 1].Index : parent.Body.Length;
                string body = parent.Body[(heading.Index + heading.Length)..end];
                body = LocalLink.Replace(body, m => body.Contains($"id=\"{m.Groups[1].Value}\"", StringComparison.Ordinal) || m.Groups[1].Value == id
                    ? m.Value : $"href=\"{parent.Path}#{m.Groups[1].Value}\"");
                string title = Html.Text(heading.Groups[3].Value);
                var topic = new DocPage
                {
                    Path = parent.Path + "/" + id,
                    ParentPath = parent.Path,
                    Title = title,
                    Section = parent.Title,
                    Description = Shorten(Html.Text(Regex.Match(body, "<p>(.*?)</p>", RegexOptions.Singleline).Groups[1].Value)),
                    Body = $"<p class=\"topic-parent\"><a href=\"{parent.Path}\">{Html.H(parent.NavTitle ?? parent.Title)}</a></p>"
                        + $"<h1 id=\"{id}\">{heading.Groups[3].Value}</h1>" + body,
                    Source = parent.Source,
                    TopicMarkdown = "# " + title + "\n\n" + topicMarkdown.TrimStart('\r', '\n'),
                };
                parent.Topics.Add(topic);
                Add(topic);
                AddHeadings(topic);
            }
        }

        private void AddHeadings(DocPage page, bool index = true)
        {
            foreach (Match m in Heading.Matches(page.Body))
            {
                int level = int.Parse(m.Groups[1].Value);
                if (level < 2) continue;
                string text = Html.Text(m.Groups[3].Value);
                page.Toc.Add((level, m.Groups[2].Value, text));
                if (index)
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
            Pages.Where(p => p.Section != null && p.ParentPath == null)
                 .GroupBy(p => p.Section)
                 .OrderBy(g => Array.IndexOf(SectionOrder, g.Key) is int i && i >= 0 ? i : 99)
                 .Select(g => (g.Key, g.ToList()));

        #endregion

        #region Output

        /// <summary>The search index the page script loads.</summary>
        /// <remarks>Built as JSON nodes rather than serialised, so it needs no reflection (Native AOT). Null fields are left out.</remarks>
        public string SearchJson()
        {
            var entries = new JsonArray();
            foreach (SearchEntry s in Search)
            {
                var entry = new JsonObject();
                foreach ((string key, string value) in new[] { ("t", s.Title), ("c", s.CodeTitle), ("u", s.Url), ("s", s.Section), ("k", s.Kind), ("d", s.Text) })
                {
                    if (value != null) entry[key] = value;
                }
                entries.Add((JsonNode)entry);
            }
            return entries.ToJsonString();
        }

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
