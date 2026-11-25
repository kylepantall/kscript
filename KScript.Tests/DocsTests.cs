using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using KScript.Docs;
using KScript.Language.Catalog;
using KScript.Metadata;
using KScript.Runtime;
using KScript.Syntax;

using Markdig.Syntax;

using Microsoft.AspNetCore.Http;

namespace KScript.Tests;

public sealed class DocsTests
{
    private static readonly Lazy<DocsSite> Site = new Lazy<DocsSite>(() => DocsSite.Build());

    [Fact]
    public void ThrowReferenceUsesCodeSyntaxAndOnlyShowsAttributeTableForXml()
    {
        var site = Site.Value;
        string html = site.Find("/reference/core").Body;
        int start = html.IndexOf("id=\"el-throw\"", StringComparison.Ordinal);
        Assert.True(start >= 0);
        string entry = html[start..html.IndexOf("</section>", start, StringComparison.Ordinal)];
        Assert.Contains("table f-xml", entry);
        Assert.DoesNotContain("table f-ks", entry);
        var markdown = new KScript.Docs.Mcp.DocsMarkdown(site);
        var statement = site.Catalog.GetElement("throw");
        string code = markdown.Element(statement, "kscript");
        Assert.Contains("throw Error(message:", code);
        Assert.DoesNotContain("| Attribute |", code);
        Assert.Contains("| Attribute |", markdown.Element(statement, "xml"));
    }

    [Fact]
    public void ApiOptionsHaveNavigableFieldsAndExamplesInBothReferences()
    {
        var site = Site.Value;
        var api = site.Catalog.GetCommand("api");
        var options = api.Signatures.Single().Parameters.Single(p => p.Name == "options");
        Assert.Equal(KType.Map, options.Type);
        Assert.Equal(new[] { "method", "headers", "body", "timeout", "retries", "retry_delay" }, options.Fields.Select(f => f.Name));
        string html = site.Find("/reference/net").Body;
        Assert.Contains("href=\"#cmd-api-options\"", html);
        Assert.Contains("id=\"cmd-api-options\"", html);
        Assert.Contains("href=\"/reference/methods/response\"", html);
        var markdown = new KScript.Docs.Mcp.DocsMarkdown(site);
        foreach (string format in new[] { "kscript", "xml" })
        {
            string page = markdown.Callable(api, format);
            Assert.Contains("kscript://docs/reference/methods/map", page);
            Assert.Contains("kscript://docs/reference/methods/response", page);
            foreach (var field in options.Fields)
            {
                string slug = field.Name.Replace('_', '-');
                Assert.Contains($"id=\"cmd-api-options-{slug}-field\"", html);
                Assert.Contains(field.DefaultValue, WebUtility.HtmlDecode(html));
                Assert.Contains(field.Description, page);
                Assert.Contains(field.Example, page);
                Assert.Contains(field.DefaultValue, page);
            }
            Assert.Contains("method: 'POST'", page);
            Assert.Contains(format == "xml" ? "<import package=\"KScript.Net\"" : "import KScript.Net", page);
        }
        Assert.Contains("In-process requests to project routes ignore this field", html);
    }

    private static readonly Regex TryBox = new Regex(@"<div class=""try""[^>]*>", RegexOptions.Compiled);
    private static readonly Regex DataAttribute = new Regex(@"data-(xml|kscript)=""([^""]*)""", RegexOptions.Compiled);
    private static readonly Regex InputAttribute = new Regex(@"data-input=""([^""]*)""", RegexOptions.Compiled);

    /// <summary>The packages whose commands and methods all need an example, because the docs can run them.</summary>
    private static readonly string[] RunnablePackages = { "KScript.Core", "KScript.Math", "KScript.IO", "KScript.Terminal", "KScript.Markdown" };

    /// <summary>Every runnable example on the site, in each format it is shown in.</summary>
    public static IEnumerable<(string Page, string Format, string Code, string Input)> Examples()
    {
        foreach (DocPage page in Site.Value.Pages)
            foreach (var (format, code, input) in ReadRunnableExamples(page.Body))
                yield return (page.Path, format, code, input);
    }

    private static IEnumerable<(string Format, string Code, string Input)> ReadRunnableExamples(string html)
    {
        foreach (Match box in TryBox.Matches(html))
        {
            Match input = InputAttribute.Match(box.Value);
            foreach (Match data in DataAttribute.Matches(box.Value))
                yield return (data.Groups[1].Value, WebUtility.HtmlDecode(data.Groups[2].Value), input.Success ? WebUtility.HtmlDecode(input.Groups[1].Value) : null);
        }
    }

    #region The examples

    [Fact]
    public void Every_runnable_example_runs_without_an_error_in_both_formats()
    {
        var failures = new List<string>();
        int count = 0;
        foreach (var (page, format, code, input) in Examples())
        {
            count++;
            PlaygroundResult result = Playground.Run(code, format, input);
            bool exits = code.Contains("exit", StringComparison.Ordinal);
            if (result.Error != null || (result.ExitCode != 0 && !exits))
                failures.Add($"{page} ({format}):\n{code}\n=> {result.Error ?? "exit " + result.ExitCode}\n{result.Output}");
        }
        Assert.True(count > 500, $"Only {count} examples were found.");
        Assert.True(failures.Count == 0, $"{failures.Count} of {count} examples failed:\n\n" + string.Join("\n\n", failures));
    }

    [Fact]
    public void Every_runnable_example_can_be_opened_in_both_formats()
    {
        var missing = new List<string>();
        foreach (DocPage page in Site.Value.Pages)
        {
            foreach (Match box in TryBox.Matches(page.Body))
            {
                MatchCollection formats = DataAttribute.Matches(box.Value);
                if (formats.Select(match => match.Groups[1].Value).ToHashSet().Count != 2)
                {
                    string code = WebUtility.HtmlDecode(formats.FirstOrDefault()?.Groups[2].Value);
                    missing.Add($"{page.Path}: {code}");
                }
            }
        }
        Assert.True(missing.Count == 0, "Examples missing a format:\n" + string.Join("\n", missing.Distinct()));
    }

    [Theory]
    [InlineData("/reference/core", "el-match", "kscript", "Unknown\n")]
    [InlineData("/reference/core", "el-match", "xml", "Unknown\n")]
    public void Runnable_reference_examples_produce_the_expected_output(string page, string anchor, string format, string expected)
    {
        string html = Site.Value.Find(page).Body;
        int start = html.IndexOf($"id=\"{anchor}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"{page} has no #{anchor} entry.");
        int end = html.IndexOf("</section>", start, StringComparison.Ordinal);
        var example = Assert.Single(ReadRunnableExamples(html[start..end]), example => example.Format == format);
        PlaygroundResult result = Playground.Run(example.Code, example.Format, example.Input);

        Assert.Null(result.Error);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, result.Output);
    }

    [Fact]
    public void Every_command_and_method_the_docs_can_run_has_an_example()
    {
        var catalog = LanguageCatalog.Default;
        var missing = catalog.Commands.Concat(catalog.Methods)
            .Where(c => RunnablePackages.Contains(c.Package) && string.IsNullOrWhiteSpace(c.Example))
            .Select(c => c.Kind == CallableKind.Method ? c.ReceiverType + "->" + c.Name : "@" + c.Name)
            .ToList();
        Assert.True(missing.Count == 0, "These need an example (p.Examples in their package): " + string.Join(", ", missing));
    }

    [Fact]
    public void Examples_that_are_only_shown_or_take_input_name_real_entries()
    {
        var catalog = LanguageCatalog.Default;
        foreach (string key in DocsSite.StaticExamples.Keys.Concat(DocsSite.ExampleInputs.Keys))
        {
            bool known = key.StartsWith("<", StringComparison.Ordinal) ? catalog.GetElement(key.Trim('<', '>'))?.Example != null
                : key.StartsWith("@", StringComparison.Ordinal) && catalog.GetCommand(key.Substring(1))?.Example != null;
            Assert.True(known, $"{key} has no example in the catalog any more.");
        }
    }

    [Fact]
    public void Package_examples_must_name_a_command_or_method()
    {
        var package = new ExamplePackage();
        var error = Assert.Throws<InvalidOperationException>(() => package.Commands.Count);
        Assert.Contains("@nothing", error.Message);
        Assert.Equal("@thing()", ExamplePackage.Seen);

        foreach (string key in new[] { "string->missing", "thing" })
        {
            var bad = new BadKeyPackage(key);
            Assert.Throws<InvalidOperationException>(() => bad.Methods.Count);
        }
    }

    private sealed class ExamplePackage : KScript.Runtime.KScriptPackage
    {
        public static string Seen;
        public override string Name => "KScript.Example";
        public override string Description => "Tests examples.";

        protected override void Register(PackageBuilder p)
        {
            var thing = p.Command("thing", "", "A thing.", KType.String, c => "thing");
            p.Method(KType.String, "shout", "", "Shouts.", KType.String, (t, c) => t);
            p.Examples(("@thing", "@thing()"), ("string->shout", "'hi'->shout()"));
            Seen = thing.Info.Example;
            p.Examples(("@nothing", "@nothing()"));
        }
    }

    private sealed class BadKeyPackage : KScript.Runtime.KScriptPackage
    {
        private readonly string key;
        public BadKeyPackage(string key) => this.key = key;
        public override string Name => "KScript.Bad";
        public override string Description => "Tests bad example keys.";
        protected override void Register(PackageBuilder p) => p.Examples((key, "1"));
    }

    #endregion

    #region The pages

    [Fact]
    public void Every_doc_file_is_a_guide_or_left_out_on_purpose()
    {
        var files = DocsSite.GuideFiles.ToList();
        Assert.Contains("language.md", files);
        foreach (string file in files)
        {
            Assert.True(DocsSite.Guides.Any(g => g.File == file) || DocsSite.NotGuides.ContainsKey(file),
                $"docs/{file} is neither a guide nor in DocsSite.NotGuides. Add it to one of them.");
        }
        foreach (DocsSite.Guide guide in DocsSite.Guides)
        {
            Assert.Contains(guide.File, files);
            Assert.NotNull(Site.Value.Find("/guide/" + guide.Slug));
        }
    }

    [Fact]
    public void User_docs_have_no_maintainer_release_steps()
    {
        string[] maintainerOnly = { "git tag", "git push origin v", "release.yml", "VSCE_PAT", "OVSX_PAT", "publish the release", "Remote Control" };
        foreach (DocPage page in Site.Value.Pages)
        {
            foreach (string phrase in maintainerOnly)
                Assert.DoesNotContain(phrase, page.Body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void The_reference_covers_everything_in_the_catalog()
    {
        var catalog = LanguageCatalog.Default;
        DocsSite site = Site.Value;
        void Has(string title, string kind)
        {
            SearchEntry entry = site.Search.FirstOrDefault(s => s.Title == title && s.Kind == kind);
            Assert.True(entry != null, $"{kind} {title} is not in the docs.");
            string[] parts = entry.Url.Split('#');
            DocPage page = site.Find(parts[0]);
            Assert.NotNull(page);
            Assert.Contains($"id=\"{parts[1]}\"", page.Body);
        }

        foreach (string package in catalog.Packages.Keys)
            Assert.NotNull(site.Pages.FirstOrDefault(p => p.Title == package && p.Section == "Reference"));
        foreach (ElementInfo e in catalog.Elements.Where(e => !e.Planned)) Has("<" + DocsSite.ElementName(e) + ">", "element");
        foreach (CallableInfo c in catalog.Commands.Where(c => !c.Planned)) Has("@" + c.Name + "()", "command");
        foreach (CallableInfo m in catalog.Methods.Where(m => !m.Planned)) Has(m.ReceiverType + "->" + m.Name + "()", "method");
        foreach (string v in catalog.BuiltInVariables.Keys) Has("$" + v, "variable");
        foreach (CliCommand c in CliReference.Commands) Assert.Contains(site.Search, s => s.Kind == "cli" && s.Url == "/cli#cli-" + c.Name);
    }

    [Fact]
    public void Every_link_between_pages_goes_somewhere()
    {
        DocsSite site = Site.Value;
        var broken = new List<string>();
        foreach (DocPage page in site.Pages)
        {
            string html = site.Render(page);
            foreach (Match m in Regex.Matches(html, @"href=""(/[^""#]*)?(#[^""]*)?"""))
            {
                string path = m.Groups[1].Success ? m.Groups[1].Value : page.Path;
                if (path.StartsWith("/assets/", StringComparison.Ordinal)) continue;
                DocPage target = site.Find(path);
                if (target == null)
                {
                    broken.Add($"{page.Path} -> {path}");
                    continue;
                }
                if (m.Groups[2].Success && m.Groups[2].Value.Length > 1)
                {
                    string id = WebUtility.HtmlDecode(m.Groups[2].Value.Substring(1));
                    if (!site.Render(target).Contains($"id=\"{id}\"")) broken.Add($"{page.Path} -> {path}#{id}");
                }
            }
        }
        Assert.True(broken.Count == 0, "Broken links:\n" + string.Join("\n", broken.Distinct()));
    }

    [Fact]
    public void Page_paths_and_fragment_targets_are_unique_and_searchable()
    {
        DocsSite site = Site.Value;
        var destinations = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (DocPage page in site.Pages)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (!destinations.TryAdd(page.Path, ids)) problems.Add($"Duplicate page {page.Path}");
            foreach (Match match in Regex.Matches(site.Render(page), @"\sid=""([^""]+)"""))
            {
                string id = WebUtility.HtmlDecode(match.Groups[1].Value);
                if (!ids.Add(id)) problems.Add($"Duplicate fragment {page.Path}#{id}");
            }
        }
        foreach (SearchEntry entry in site.Search)
        {
            string[] target = entry.Url.Split('#', 2);
            if (!destinations.TryGetValue(target[0], out var ids) || (target.Length == 2 && !ids.Contains(target[1])))
                problems.Add($"Missing search destination {entry.Url} ({entry.Title})");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems.Distinct()));
    }

    [Fact]
    public void Pages_have_a_title_navigation_search_and_both_themes()
    {
        DocsSite site = Site.Value;
        DocPage language = site.Find("/guide/language");
        string html = site.Render(language);
        Assert.Contains("<title>The KScript language · KScript docs</title>", html);
        Assert.Contains("aria-current=\"page\"", html);
        Assert.Contains("id=\"search\"", html);
        Assert.Contains("theme-toggle", html);
        Assert.Contains("On this page", html);
        Assert.Contains("Edit this page on GitHub", html);
        Assert.Contains("class=\"pager\"", html);
        Assert.Single(Regex.Matches(language.Body, "<h1"));
        Assert.Contains("<title>KScript docs</title>", site.Render(site.Find("/")));
        Assert.DoesNotContain("class=\"pager\"", site.Render(site.Find("/404")));

        string css = DocsSite.ReadAsset("docs.css");
        Assert.Contains("[data-theme=\"dark\"]", css);
        Assert.Contains("prefers-color-scheme: dark", css);
        string js = DocsSite.ReadAsset("docs.js");
        Assert.Contains("/api/run", js);
        Assert.Contains("X-KScript-Docs", js);

        var search = JsonDocument.Parse(site.SearchJson()).RootElement;
        Assert.True(search.GetArrayLength() > 500);
        Assert.Contains(search.EnumerateArray(), e => e.GetProperty("t").GetString() == "string->replace()");
    }

    [Fact]
    public void Guides_turn_markdown_into_pages_with_runnable_and_plain_code()
    {
        var docs = new Dictionary<string, string>
        {
            ["getting-started.md"] = "# Start\n\nIntro text.\n\n## One\n\n```xml try\n<echo>Hi</echo>\n```\n\n```try\n<echo>Bare</echo>\n```\n\n```sh\nkscript x.ks.xml\n```\n\n| a | b |\n| - | - |\n| 1 | 2 |\n\nSee [the language](language.md#functions), [a sample](../samples/x) and [a file](../samples/x.ks.xml).\n",
            ["code-format.md"] = "# Code\n\n```\nx = 1\n```\n\n### Deep\n\nText.\n",
        };
        DocsSite site = DocsSite.Build(version: "v9.9.9", readGuide: f => docs.GetValueOrDefault(f));
        DocPage start = site.Find("/guide/getting-started");
        Assert.Equal("Start", start.Title);
        Assert.Equal("Intro text.", start.Description);
        Assert.Equal(2, TryBox.Matches(start.Body).Count);
        Assert.Contains("data-lang=\"sh\"", start.Body);
        Assert.Contains("<div class=\"table\"><table>", start.Body);
        Assert.Contains("href=\"/guide/language#functions\"", start.Body);
        Assert.Contains($"href=\"{DocsSite.Repository}/tree/master/samples/x\"", start.Body);
        Assert.Contains($"href=\"{DocsSite.Repository}/blob/master/samples/x.ks.xml\"", start.Body);
        Assert.Contains((2, "one", "One"), start.Toc);
        Assert.Contains("v9.9.9", site.Render(start));

        DocPage code = site.Find("/guide/code-format");
        Assert.Contains("data-lang=\"kscript\"", code.Body);
        Assert.Contains((3, "deep", "Deep"), code.Toc);
        Assert.Null(site.Find("/guide/language"));
    }

    [Fact]
    public void Guide_topics_have_their_own_pages_and_keep_the_original_section_links()
    {
        var site = Site.Value;
        var guide = site.Find("/guide/data");
        var models = site.Find("/guide/data/models");
        Assert.NotNull(models);
        Assert.Contains("id=\"models\"", guide.Body);
        Assert.Contains("<h1 id=\"models\">Models</h1>", models.Body);
        Assert.Contains("id=\"extending-models\"", models.Body);
        Assert.DoesNotContain("id=\"queries\"", models.Body);
        string rendered = site.Render(models);
        Assert.Contains("href=\"/guide/data/models\" aria-current=\"page\"", rendered);
        Assert.Contains("href=\"/guide/data/queries\"", rendered);
        Assert.Contains("href=\"/guide/data\">", rendered);
        Assert.Contains("data-lang=\"kscript\"", site.Find("/guide/components/component-inputs-rendering-and-slots").Body);
        Assert.Contains("@contrast_color", site.Find("/guide/stylesheets/stylesheet-cheatsheet").Body);
    }

    [Fact]
    public void Links_in_guides_point_at_the_site_or_github()
    {
        Assert.Equal("/guide/http#views", DocsSite.LinkTarget("http.md", "#views"));
        Assert.Equal("/guide/web", DocsSite.LinkTarget("docs/web.md", null));
        Assert.Equal("#here", DocsSite.LinkTarget("", "#here"));
        Assert.Equal("/reference", DocsSite.LinkTarget("/reference", null));
        Assert.Equal($"{DocsSite.Repository}/blob/master/docs/contributing.md", DocsSite.LinkTarget("contributing.md", null));
        Assert.Equal($"{DocsSite.Repository}/blob/master/.github/workflows/ci.yml", DocsSite.LinkTarget("../.github/workflows/ci.yml", null));
    }

    [Fact]
    public void Reference_entries_show_signatures_parameters_and_examples()
    {
        DocsSite site = Site.Value;
        string strings = site.Find("/reference/methods/string").Body;
        Assert.Contains("id=\"m-replace\"", strings);
        Assert.Contains("href=\"/reference/methods/number#m-round\"", strings);
        Assert.DoesNotContain("/reference/methods/bool#m-not", strings);
        Assert.Contains("Arguments can be named", strings);

        string io = site.Find("/reference/io").Body;
        Assert.Contains("one of <code>home</code>", io);
        Assert.Contains("a flag: <code>recursive</code>", io);
        Assert.Contains("&lt;import package=&quot;KScript.IO&quot; /&gt;", io);

        string core = site.Find("/reference/core").Body;
        Assert.Contains("nothing to import", core);
        Assert.Contains("Follows <span class=\"f-xml\"><code>&lt;if&gt;</code></span><span class=\"f-ks\"><code>if</code></span>", core);
        Assert.Contains("&lt;elif&gt;", core);

        string http = site.Find("/reference/http").Body;
        Assert.Contains("&lt;KScript.Routes&gt;", http);
        Assert.Contains("can't run inside the docs", http);
        Assert.DoesNotContain("class=\"try\"", http);

        Assert.Contains("in a .ks.css file", site.Find("/reference/style").Body);

        string terminal = site.Find("/reference/terminal").Body;
        Assert.Contains("data-input=", terminal);

        Assert.Contains("$args", site.Find("/reference/variables").Body);
        Assert.Contains("Fields", site.Find("/reference/methods/file").Body);
        Assert.Contains("--no-open", site.Find("/cli").Body);
        Assert.Contains("class=\"playground\"", site.Find("/playground").Body);
    }

    [Theory]
    [InlineData("email", "string", "lower")]
    [InlineData("email", "string", "split")]
    [InlineData("semver[]", "list", "filter")]
    public void Type_references_link_inherited_methods_to_their_definitions(string type, string definingType, string method)
    {
        DocsSite site = Site.Value;
        string path = "/reference/methods/" + type;
        string definition = "/reference/methods/" + definingType + "#m-" + method;
        string html = site.Find(path).Body;
        Assert.Contains($"Methods from {definingType}", html);
        Assert.Contains($"href=\"{definition}\"", html);
        var docs = new KScript.Docs.Mcp.DocsMarkdown(site);
        string markdown = docs.Page(path, "kscript");
        Assert.Contains($"## Methods from {definingType}", markdown);
        Assert.Contains($"(kscript://docs{definition})", markdown);

        int count = site.Catalog.MethodsFor(type).Count(m => !m.Planned);
        Assert.Contains($"{count} methods", Regex.Match(site.Find("/reference").Body, Regex.Escape($"href=\"{path}\"") + ".*?</span>").Value);
        Assert.Contains($"]({KScript.Docs.Mcp.DocsMarkdown.UriOf(path)}): {site.Catalog.FieldsOf(type).Count} fields, {count} methods", docs.Page("/reference", "kscript"));
    }

    [Fact]
    public void Html_helpers()
    {
        Assert.Equal("early-returns-instead-of-else", Html.Slug("Early returns instead of `else`"));
        Assert.Equal("a-b", Html.Slug("  A -- b  "));
        Assert.Equal("Use &lt;elif&gt; or <code>&lt;else&gt;</code>", Html.Inline("Use <elif> or `<else>`"));
        Assert.Equal(string.Empty, Html.Inline("  "));
        Assert.StartsWith("<p>", Html.Inline("One.\n\nTwo."));
        Assert.Equal("a b", Html.Text("<p>a</p>\n<p>b</p>"));
        Assert.Contains("data-lang=\"text\"", Html.Code("x", null));
        Assert.Contains(">ps1<", Html.Code("x", "ps1"));
        Assert.Contains("data-title=\"t\"", Html.Try("<echo>x</echo>", title: "t"));
        Assert.DoesNotContain("data-kscript", Html.Try("<echo>", "xml"));
        Assert.Equal("Short", DocsSite.Shorten("Short"));
        Assert.EndsWith("…", DocsSite.Shorten(string.Join(" ", Enumerable.Repeat("word", 80))));
        Assert.Equal("<echo>{ 1 + 1 }</echo>", DocsSite.ExampleScript("1 + 1"));
        Assert.Equal("<echo>x</echo>", DocsSite.ExampleScript(" <echo>x</echo> "));
        Assert.Equal("expression", DocsSite.KindName(AttributeKind.Expression));
        foreach (AttributeKind kind in Enum.GetValues<AttributeKind>()) Assert.NotEmpty(DocsSite.KindName(kind));
    }

    [Fact]
    public void The_site_is_written_as_a_folder_kscript_serve_can_serve()
    {
        string folder = Path.Combine(Path.GetTempPath(), "kscript-docs-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Site.Value.WriteTo(folder);
            Assert.True(File.Exists(Path.Combine(folder, "_pages", "index.html")));
            Assert.True(File.Exists(Path.Combine(folder, "_pages", "guide", "language.html")));
            Assert.True(File.Exists(Path.Combine(folder, "_pages", "404.html")));
            Assert.True(File.Exists(Path.Combine(folder, "assets", "search.json")));
            Assert.True(File.Exists(Path.Combine(folder, "assets", "docs.js")));
            Assert.Contains("<KScript.Routes>", File.ReadAllText(Path.Combine(folder, "routes.ks.xml")));
            Assert.Equal("guide/language.html", DocsSite.PageFile("/guide/language"));
            Assert.Equal("index.html", DocsSite.PageFile("/"));
            Assert.Equal(0, KScript.LanguageServer.CheckCommand.Run(new[] { Path.Combine(folder, "routes.ks.xml") }, TextWriter.Null));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    #endregion

    #region The sandbox

    [Fact]
    public void Playground_runs_statements_and_whole_scripts()
    {
        Assert.Equal("3\n", Playground.Run("<echo>{ 1 + 2 }</echo>").Output);
        Assert.Equal("hi\n", Playground.Run("<kscript>\n  <echo>hi</echo>\n</kscript>").Output);
        Assert.Equal("hi\n", Playground.Run("<!-- a comment -->\n<kscript><echo>hi</echo></kscript>").Output);
        Assert.Equal("6\n", Playground.Run("x = 2\necho \"{ $x * 3 }\"", "kscript").Output);
        Assert.EndsWith("Joan\n", Playground.Run("<import package=\"KScript.Terminal\" />\n<input into=\"n\">Name? </input>\n<echo>$n</echo>", input: "Joan\n").Output);

        Assert.True(Playground.IsWholeScript("<?xml version=\"1.0\"?><kscript/>"));
        Assert.True(Playground.IsWholeScript("<KScript.Window/>"));
        Assert.False(Playground.IsWholeScript("<!-- open comment"));
        Assert.False(Playground.IsWholeScript("<echo/>"));
        Assert.Equal("<kscript><echo/>\n</kscript>", Playground.AsScript("<echo/>", "xml"));
        Assert.Equal("x = 1", Playground.AsScript("x = 1", "kscript"));
        Assert.Equal("<kscript>\n</kscript>", Playground.AsScript(null, "xml"));
    }

    [Fact]
    public void Playground_errors_keep_the_readers_line_numbers()
    {
        PlaygroundResult result = Playground.Run("<echo>ok</echo>\n<echo>{ $missing }</echo>");
        Assert.Equal("ok\n", result.Output);
        Assert.Equal(3, result.ExitCode);
        Assert.StartsWith("example.ks.xml:2:", result.Error);
        Assert.DoesNotContain(Path.GetTempPath(), result.Error);

        Assert.StartsWith("example.kscript:1:", Playground.Run("echo $nope", "kscript").Error);
        Assert.Equal(2, Playground.Run("<exit code=\"2\" />").ExitCode);
    }

    [Fact]
    public void Playground_keeps_files_in_their_own_folder_and_removes_it()
    {
        PlaygroundResult result = Playground.Run("<import package=\"KScript.IO\" />\n<do>@write_text('a.txt', 'hello')</do>\n<echo>{ @read_text('a.txt') } { @cwd() }</echo>");
        Assert.Null(result.Error);
        string folder = result.Output.Trim().Split(' ', 2)[1];
        Assert.Contains("kscript-try-", folder);
        Assert.False(Directory.Exists(folder));

        PlaygroundResult escape = Playground.Run("<import package=\"KScript.IO\" />\n<echo>{ @read_text('../../outside.txt') }</echo>");
        Assert.Contains("Permission", escape.Error);
        Assert.Contains("the example's folder", escape.Error);
    }

    [Fact]
    public void Playground_gives_KScript_Data_a_database_in_memory_for_each_run()
    {
        const string code = "import KScript.Data\nmodel Note {\n  body: string\n}\n$db->create_table(Note)\nNote->create(body: 'hi')\necho Note->count()\necho $db";
        PlaygroundResult first = Playground.Run(code, "kscript");
        Assert.Null(first.Error);
        Assert.Equal("1\nsqlite database 'main' (memory)\n", first.Output);
        // A second run starts empty: the table isn't there until it makes it again.
        Assert.Equal(first.Output, Playground.Run(code, "kscript").Output);
        Assert.Equal("0\n", Playground.Run("import KScript.Data\necho $db->tables()->count()", "kscript").Output);
    }

    [Fact]
    public void Playground_has_no_network_servers_windows_or_models()
    {
        foreach (string package in Playground.Unavailable.Keys)
        {
            PlaygroundResult result = Playground.Run($"<import package=\"{package}\" />");
            Assert.Contains("can't run in the docs", result.Error);
            Assert.Contains(Playground.Unavailable[package], result.Error);
        }
        Assert.Equal(Playground.Packages.Count + 1, Playground.CreateRegistry().Names.Count());
    }

    [Fact]
    public void Playground_stops_long_runs_and_big_output()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        PlaygroundResult loop = Playground.Run("<while test=\"true\"><do>1</do></while>", timeout: TimeSpan.FromSeconds(2));
        Assert.NotNull(loop.Error);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30));

        PlaygroundResult wait = Playground.Run("<wait duration=\"10s\" />", timeout: TimeSpan.FromMilliseconds(300));
        Assert.NotNull(wait.Error);

        PlaygroundResult big = Playground.Run("<for each=\"i\" in=\"0..2000\"><echo>{ 'x'->repeat(60) }</echo></for>");
        Assert.True(big.Truncated);
        Assert.Equal(Playground.MaxOutput, big.Output.Length);

        PlaygroundResult huge = Playground.Run(new string(' ', Playground.MaxCode + 1));
        Assert.Contains("too long", huge.Error);

        PlaygroundResult colour = Playground.Run("<import package=\"KScript.Terminal\" />\n<echo>{ @color('ok', 'green') }</echo>");
        Assert.Equal("ok\n", colour.Output);
    }

    [Fact]
    public void Playground_converts_between_xml_and_kscript()
    {
        Assert.Equal("x = 1\necho \"{ $x }\"\n", Playground.Convert("<let name=\"x\" value=\"1\" />\n<echo>$x</echo>", "kscript"));
        Assert.Equal("<let name=\"x\" value=\"1\" />\n<echo>{ $x }</echo>\n", Playground.Convert("x = 1\necho \"{ $x }\"", "xml"));
        Assert.Equal("const max = 10\n", Playground.Convert("<const name=\"max\" value=\"10\" />", "kscript"));
        Assert.Null(Playground.Convert("<echo>", "kscript"));
        Assert.Null(Playground.Convert("if {", "xml"));
        Assert.Null(Playground.Convert("  ", "xml"));
        Assert.Null(Playground.Convert(new string('x', Playground.MaxCode + 1), "xml"));
        Assert.Equal("<other />\n", Playground.Unwrap("<other />"));
    }

    #endregion

    #region The server

    private static HttpRequest Request(string method, string path, string body = null, string host = "localhost:4180", bool header = true, string origin = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.Host = new HostString(host);
        if (header) context.Request.Headers[DocsCommand.RunHeader] = "1";
        if (origin != null) context.Request.Headers.Origin = origin;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body ?? string.Empty));
        return context.Request;
    }

    private static (int Status, JsonElement Json) Api(HttpRequest request)
    {
        KScript.Pages.PageResult result = DocsCommand.HandleApiAsync(request, 4180).GetAwaiter().GetResult();
        return (result.Status, JsonDocument.Parse(result.Body).RootElement);
    }

    [Fact]
    public async Task The_run_api_only_answers_the_docs_pages()
    {
        Assert.Null(await DocsCommand.HandleApiAsync(Request("GET", "/guide/language"), 4180));
        Assert.Equal(404, Api(Request("POST", "/api/other", "{}")).Status);
        Assert.Equal(405, Api(Request("GET", "/api/run")).Status);
        Assert.Equal(403, Api(Request("POST", "/api/run", "{\"code\":\"\"}", header: false)).Status);
        Assert.Equal(403, Api(Request("POST", "/api/run", "{\"code\":\"\"}", host: "evil.example:4180")).Status);
        Assert.Equal(403, Api(Request("POST", "/api/run", "{\"code\":\"\"}", origin: "http://evil.example")).Status);
        Assert.Equal(200, Api(Request("POST", "/api/run", "{\"code\":\"\"}", host: "127.0.0.1:4180", origin: "http://127.0.0.1:4180")).Status);
        Assert.Equal(400, Api(Request("POST", "/api/run", "not json")).Status);
        Assert.Equal(400, Api(Request("POST", "/api/run", "{\"code\": 3}")).Status);
        Assert.Equal(400, Api(Request("POST", "/api/run", "[]")).Status);
        Assert.Equal(413, Api(Request("POST", "/api/run", new string(' ', Playground.MaxCode * 2 + 2048))).Status);

        Assert.True(DocsCommand.IsLocal(Request("POST", "/api/run", host: "[::1]:4180"), 4180));
        Assert.False(DocsCommand.IsLocal(Request("POST", "/api/run", origin: "http://localhost:9999"), 4180));
    }

    [Fact]
    public void The_run_api_runs_and_converts_code()
    {
        var (status, json) = Api(Request("POST", "/api/run", "{\"code\":\"<echo>{ 6 * 7 }</echo>\",\"format\":\"xml\"}"));
        Assert.Equal(200, status);
        Assert.Equal("42\n", json.GetProperty("output").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("error").ValueKind);
        Assert.Equal(0, json.GetProperty("exitCode").GetInt32());

        var (s2, j2) = Api(Request("POST", "/api/convert", "{\"code\":\"<echo>hi</echo>\",\"to\":\"kscript\"}"));
        Assert.Equal(200, s2);
        Assert.Equal("echo \"hi\"\n", j2.GetProperty("code").GetString());

        var (s3, j3) = Api(Request("POST", "/api/convert", "{\"code\":\"if {\",\"to\":\"xml\"}"));
        Assert.Equal(422, s3);
        Assert.Contains("XML", j3.GetProperty("error").GetString());
        Assert.Contains(".kscript", Api(Request("POST", "/api/convert", "{\"code\":\"<echo>\",\"to\":\"kscript\"}")).Json.GetProperty("error").GetString());
    }

    [Fact]
    public void Examples_show_in_the_projects_format_and_the_reader_can_switch()
    {
        // Script blocks come in both formats, with a switch; anything that doesn't convert stays as it is.
        string pair = Html.Code("<echo>Hi</echo>", "xml");
        Assert.StartsWith("<div class=\"code-pair\">", pair);
        Assert.Contains("data-lang=\"xml\"", pair);
        Assert.Contains("data-lang=\"kscript\"", pair);
        Assert.Contains("echo \"Hi\"", WebUtility.HtmlDecode(pair));
        Assert.Equal(2, Regex.Matches(pair, "class=\"fmt-tabs\"").Count);
        Assert.StartsWith("<div class=\"code-pair\">", Html.Code("x = 1\n", "kscript"));
        Assert.DoesNotContain("code-pair", Html.Code("<echo>unclosed", "xml"));
        Assert.DoesNotContain("code-pair", Html.Code("{ \"a\": 1 }", "json"));

        // The page starts in the site's format, and the reader's pick (kept for the tab) wins over it.
        DocsSite site = DocsSite.Build();
        string kscript = site.Render(site.Find("/"));
        Assert.Contains("data-format=\"kscript\" data-default-format=\"kscript\"", kscript);
        Assert.Contains("sessionStorage.getItem('kscript-docs-format')", kscript);
        Assert.Contains("class=\"fmt-switch fmt-tabs\"", kscript);
        site.Format = "xml";
        Assert.Contains("data-format=\"xml\" data-default-format=\"xml\"", site.Render(site.Find("/")));
        string css = DocsSite.ReadAsset("docs.css");
        Assert.Contains(":root[data-format=\"kscript\"] .code-pair > .code[data-lang=\"xml\"]", css);
        Assert.Contains("setPreferredFormat", DocsSite.ReadAsset("docs.js"));

        Assert.Equal("kscript", DocsCommand.NormalizeFormat(" KScript "));
        Assert.Equal("xml", DocsCommand.NormalizeFormat("xml"));
        // One spelling each: the extensions and old short names are not syntax names.
        Assert.Null(DocsCommand.NormalizeFormat("ks"));
        Assert.Null(DocsCommand.NormalizeFormat("ks.xml"));
        Assert.Null(DocsCommand.NormalizeFormat("yaml"));
        Assert.Null(DocsCommand.NormalizeFormat(null));

        // kscript docs takes the default from the project it runs in: make.syntax, else .kscript.
        string root = Path.Combine(Path.GetTempPath(), "kscript-docs-format-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "xml-project", "src"));
            Directory.CreateDirectory(Path.Combine(root, "plain-project"));
            Directory.CreateDirectory(Path.Combine(root, "broken"));
            File.WriteAllText(Path.Combine(root, "xml-project", "kscript.json"), "{ \"make\": { \"syntax\": \"xml\" } }");
            File.WriteAllText(Path.Combine(root, "plain-project", "kscript.json"), "{ }");
            File.WriteAllText(Path.Combine(root, "broken", "kscript.json"), "{ nope");
            Assert.Equal("xml", DocsCommand.ProjectFormat(Path.Combine(root, "xml-project", "src")));
            Assert.Equal("kscript", DocsCommand.ProjectFormat(Path.Combine(root, "plain-project")));
            Assert.Equal("kscript", DocsCommand.ProjectFormat(Path.Combine(root, "broken")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        var output = new StringWriter();
        var error = new StringWriter();
        Assert.Equal(2, DocsCommand.Run(new[] { "--syntax", "yaml" }, output, error));
        Assert.Contains("--syntax needs kscript or xml", error.ToString());
        Assert.Equal(2, DocsCommand.Run(new[] { "--syntax" }, output, error));
        Assert.Equal(2, DocsCommand.Run(new[] { "--format", "xml" }, output, error));
        Assert.Contains("--format was renamed. Use --syntax kscript|xml.", error.ToString());
    }

    [Fact]
    public void Docs_command_options()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        Assert.Equal(0, DocsCommand.Run(new[] { "--help" }, output, error));
        Assert.Contains("Usage: kscript docs", output.ToString());
        Assert.Equal(2, DocsCommand.Run(new[] { "--port" }, output, error));
        Assert.Equal(2, DocsCommand.Run(new[] { "--port", "70000" }, output, error));
        Assert.Contains("--port needs a number", error.ToString());
        Assert.Equal(2, DocsCommand.Run(new[] { "--nope" }, output, error));
        Assert.Contains("Unknown option '--nope'", error.ToString());

        var busy = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        busy.Start();
        try
        {
            int port = ((IPEndPoint)busy.LocalEndpoint).Port;
            Assert.False(DocsCommand.IsFree(port));
            Assert.NotEqual(port, DocsCommand.FreePort(port));
            Assert.Equal(1, DocsCommand.Run(new[] { "--port", port.ToString() }, output, error));
            Assert.Contains($"Port {port} is in use", error.ToString());
        }
        finally
        {
            busy.Stop();
        }
    }

    [Fact]
    public void Old_docs_folders_are_cleaned_up()
    {
        string old = Path.Combine(Path.GetTempPath(), "kscript-docs-old-" + Guid.NewGuid().ToString("N"));
        string fresh = Path.Combine(Path.GetTempPath(), "kscript-docs-new-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(old);
        Directory.CreateDirectory(fresh);
        Directory.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-3));
        DocsCommand.CleanUpOldSites();
        Assert.False(Directory.Exists(old));
        Assert.True(Directory.Exists(fresh));
        Directory.Delete(fresh);
    }

    [Fact]
    public void The_browser_is_opened_with_the_systems_command()
    {
        Assert.True(DocsCommand.BrowserCommand("http://x", windows: true, mac: false).UseShellExecute);
        Assert.Equal("open", DocsCommand.BrowserCommand("http://x", windows: false, mac: true).FileName);
        Assert.Equal("xdg-open", DocsCommand.BrowserCommand("http://x", windows: false, mac: false).FileName);

        var original = DocsCommand.StartProcess;
        try
        {
            var started = new List<string>();
            DocsCommand.StartProcess = info =>
            {
                started.Add(info.Arguments.Length > 0 ? info.Arguments : info.FileName);
                return null;
            };
            DocsCommand.OpenBrowser("http://localhost:1/");
            Assert.Equal(new[] { "http://localhost:1/" }, started);

            DocsCommand.StartProcess = info => throw new System.ComponentModel.Win32Exception("no browser");
            DocsCommand.OpenBrowser("http://localhost:1/");
        }
        finally
        {
            DocsCommand.StartProcess = original;
        }
    }

    [Fact]
    public async Task Kscript_docs_serves_the_site_until_stopped()
    {
        using var stop = new CancellationTokenSource();
        var output = new StringWriter();
        string opened = null;
        int port = DocsCommand.FreePort(DocsCommand.DefaultPort + 7);
        var before = Directory.GetDirectories(Path.GetTempPath(), "kscript-docs-*").ToHashSet();
        var server = Task.Run(() => DocsCommand.Run(new[] { "--port", port.ToString(), "--syntax", "xml", "string", "replace" }, output, TextWriter.Null, stop.Token, url => opened = url));

        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (opened == null && DateTime.UtcNow < deadline && !server.IsCompleted) await Task.Delay(50);
        Assert.Equal($"http://localhost:{port}/?q=string%20replace", opened);
        Assert.Contains("KScript docs: http://localhost:", output.ToString());

        HttpResponseMessage home = await http.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        string homePage = await home.Content.ReadAsStringAsync();
        Assert.Contains("<title>KScript docs</title>", homePage);
        Assert.Contains("data-default-format=\"xml\"", homePage);
        Assert.DoesNotContain("href=\"/guide/install\"", homePage);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/guide/install")).StatusCode);
        string gettingStarted = await http.GetStringAsync("/guide/getting-started");
        Assert.Contains("https://github.com/kylepantall/kscript/blob/master/docs/install.md", gettingStarted);
        Assert.Contains("The KScript language", await http.GetStringAsync("/guide/language"));
        Assert.Contains("string methods", await http.GetStringAsync("/reference/methods/string/"));
        Assert.Contains("--accent", await http.GetStringAsync("/assets/docs.css"));
        string search = await http.GetStringAsync("/assets/search.json");
        Assert.StartsWith("[", search);
        Assert.DoesNotContain("/guide/install", search);

        HttpResponseMessage missing = await http.GetAsync("/no/such/page");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Contains("Page not found", await missing.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/routes.ks.xml")).StatusCode);

        var run = new HttpRequestMessage(HttpMethod.Post, "/api/run") { Content = new StringContent("{\"code\":\"<echo>docs</echo>\"}", Encoding.UTF8, "application/json") };
        run.Headers.Add(DocsCommand.RunHeader, "1");
        Assert.Contains("\"output\":\"docs\\n\"", await (await http.SendAsync(run)).Content.ReadAsStringAsync());

        stop.Cancel();
        Assert.Equal(0, await server.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.DoesNotContain(Directory.GetDirectories(Path.GetTempPath(), "kscript-docs-*"), d => !before.Contains(d) && !d.Contains("-test-"));
    }

    /// <summary>
    /// Codes that mean an example in a guide is out of step with the code: something it uses was removed, renamed or
    /// deprecated, or takes different arguments now.
    /// </summary>
    private static readonly HashSet<string> OutOfSync = new HashSet<string>
    {
        DiagnosticCodes.UnknownElement, DiagnosticCodes.RetiredElement, DiagnosticCodes.UnknownAttribute, DiagnosticCodes.RetiredAttribute,
        DiagnosticCodes.MissingAttribute, DiagnosticCodes.InvalidAttributeValue, DiagnosticCodes.Deprecated, DiagnosticCodes.UnknownCommand,
        DiagnosticCodes.RetiredCommand, DiagnosticCodes.UnknownMethod, DiagnosticCodes.ArgumentCount, DiagnosticCodes.UnknownParameter,
        DiagnosticCodes.NamedArgument, DiagnosticCodes.InvalidArgumentValue, DiagnosticCodes.UnknownPackage,
        DiagnosticCodes.MarkupSyntax, DiagnosticCodes.ExpressionSyntax,
    };

    /// <summary>Functions that examples call as if the reader's script declared them; every other unknown @name fails.</summary>
    private static readonly HashSet<string> AssumedFunctions = new HashSet<string> { "greet", "greeting", "slugify", "set_age", "sign_up" };

    [Fact]
    public void Every_example_in_the_guides_and_readme_matches_the_code()
    {
        var sources = DocsSite.GuideFiles.Select(f => ("docs/" + f, DocsSite.ReadEmbeddedGuide(f))).ToList();
        string root = AppContext.BaseDirectory;
        while (root != null && !File.Exists(Path.Combine(root, "KScript.sln"))) root = Path.GetDirectoryName(root);
        if (root != null) sources.Add(("README.md", File.ReadAllText(Path.Combine(root, "README.md"))));

        var problems = new List<string>();
        int checkedCount = 0;
        foreach (var (file, markdown) in sources)
        {
            foreach (FencedCodeBlock example in Markdig.Markdown.Parse(markdown, Html.Pipeline).Descendants<FencedCodeBlock>())
            {
                if (example.Info == "json")
                {
                    try
                    {
                        using var json = JsonDocument.Parse(example.Lines.ToString());
                    }
                    catch (JsonException ex)
                    {
                        problems.Add($"{file}:{example.Line + 2 + ex.LineNumber}: {ex.Message}");
                    }
                }
                if (example.Info is not ("kscript" or "xml")) continue;
                bool code = example.Info == "kscript";
                string body = example.Lines.ToString();
                string ruleFile = KScript.Language.Rules.DocumentRules.FileName;
                bool rules = code && example.Arguments?.Trim() == ruleFile;
                string expected = Regex.Match(example.Arguments ?? string.Empty, @"^wrong\s+(KS\d{4})$").Groups[1].Value;
                bool wrap = !code && !Regex.IsMatch(body.TrimStart(), @"^(<!--.*?-->\s*)*<(kscript|KScript\.)", RegexOptions.Singleline);
                if (wrap) body = "<kscript>\n" + body + "\n</kscript>";
                string path = rules ? ruleFile : code ? "example.kscript" : "example.ks.xml";
                var analysis = KScript.Language.Analysis.Analyzer.Analyze(body, path, KScript.Language.Analysis.EmptyFileSystem.Instance);
                checkedCount++;
                if (expected.Length > 0 && !analysis.Diagnostics.Any(d => d.Code == expected))
                    problems.Add($"{file}:{example.Line + 1}: the example should report {expected}.");
                foreach (var d in analysis.Diagnostics.Where(d => d.Code != expected && (OutOfSync.Contains(d.Code) || (rules && d.Severity == DiagnosticSeverity.Error))))
                {
                    // Examples import files of the reader's that aren't here, and use types declared in an earlier example.
                    if (d.Code == DiagnosticCodes.InvalidAttributeValue && (d.Message.StartsWith("File not found", StringComparison.Ordinal)
                        || Regex.IsMatch(d.Message, "^Unknown type '[A-Z]"))) continue;
                    // Jobs and other types made by calling them (SendWelcome(user_id: 7)) come from those files too.
                    if (d.Code == DiagnosticCodes.UnknownCommand && Regex.IsMatch(d.Message, "^Unknown function '[A-Z]")) continue;
                    if (d.Code == DiagnosticCodes.UnknownCommand && Regex.Match(d.Message, @"Unknown command @(\w+)\.") is { Success: true } unknown && AssumedFunctions.Contains(unknown.Groups[1].Value)) continue;
                    int line = example.Line + 2 + body.AsSpan(0, Math.Min(d.Span.Start, body.Length)).Count('\n') - (wrap ? 1 : 0);
                    problems.Add($"{file}:{line}: {d.Code} {d.Message}");
                }
            }
        }
        Assert.True(checkedCount > 40, $"Only {checkedCount} examples were found.");
        Assert.True(problems.Count == 0, "Examples out of step with the code:\n" + string.Join("\n", problems));
    }

    private sealed class RetiringPackage : KScriptPackage
    {
        public override string Name => "KScript.Retiring";

        public override string Description => "Deprecated things, for the reference.";

        protected override void Register(PackageBuilder p)
        {
            p.Command("old_sum", "a, b", "Adds two numbers.", KType.Number, call => null);
            p.Command("fetch_text", "url, retries?", "Fetches text.", KType.String, call => null);
            p.Method(KType.String, "shout", "", "Upper-cases text.", KType.String, (target, call) => null);
            p.Element(new ElementInfo("beep", "Beeps.", ContentKind.Empty, new AttributeInfo("tone", AttributeKind.Template, "The sound.")), run => null);
            p.Element(new ElementInfo("old_beep", "Beeps.", ContentKind.Empty), run => null);
            p.Deprecate("@old_sum", "Sums moved to `KScript.Math`", "@sum");
            p.Deprecate("@fetch_text(retries)", "Requests are retried for you");
            p.Deprecate("string->shout", "", "->upper");
            p.Deprecate("<beep tone>", "Renamed", "sound");
            p.Deprecate("<old_beep>", "");
        }
    }

    [Fact]
    public void The_reference_strikes_through_and_labels_what_is_deprecated()
    {
        var registry = new KScript.Runtime.PackageRegistry();
        foreach (var (name, type, description) in KScript.Runtime.PackageRegistry.BuiltIn.Skip(1)) registry.Register(name, type, description);
        registry.Register(new RetiringPackage());
        DocsSite site = DocsSite.Build(new LanguageCatalog(registry), readGuide: _ => "# Guide\n");

        string package = site.Find("/reference/retiring").Body;
        Assert.Contains("<section class=\"entry deprecated\" id=\"cmd-old-sum\">", package);
        Assert.Contains("<del><code>@<span class=\"name\">old_sum</span>(<span class=\"param\">a</span>, <span class=\"param\">b</span>)</code></del> <span class=\"badge deprecated-badge\">deprecated</span>", package);
        Assert.Contains("<p class=\"deprecation\"><strong>Deprecated.</strong> Sums moved to <code>KScript.Math</code>. Use <code>@sum</code> instead.</p>", package);
        Assert.Contains("<del class=\"param\" title=\"Deprecated\">retries?</del>", package);
        Assert.Contains("<tr class=\"deprecated\"><td><del><code>retries</code></del> <span class=\"badge deprecated-badge\">deprecated</span>", package);
        Assert.Contains("<p class=\"deprecation\"><strong>Deprecated.</strong> Requests are retried for you.</p>", package);
        Assert.Contains("<tr class=\"deprecated\"><td><del><code>tone</code></del>", package);
        Assert.Contains("<p class=\"deprecation\"><strong>Deprecated.</strong> Renamed. Use <code>sound</code> instead.</p>", package);
        Assert.Contains("<del><span class=\"f-xml\"><code>&lt;old_beep&gt;</code></span><span class=\"f-ks\"><code>old_beep</code></span></del>", package);
        Assert.Contains("<p class=\"deprecation\"><strong>Deprecated.</strong></p>", package);
        Assert.Contains("title=\"Deprecated\"><del><code>-&gt;shout()</code></del></a>", package);
        Assert.Contains("<del class=\"param\"", site.Find("/reference/retiring").Body);

        string strings = site.Find("/reference/methods/string").Body;
        Assert.Contains("<p class=\"deprecation\"><strong>Deprecated.</strong> Use <code>-&gt;upper</code> instead.</p>", strings);
        Assert.Contains("<section class=\"entry\" id=\"", strings);

        var search = JsonDocument.Parse(site.SearchJson()).RootElement.EnumerateArray().ToList();
        Assert.StartsWith("Deprecated. ", search.Single(e => e.GetProperty("t").GetString() == "@old_sum()").GetProperty("d").GetString());
        Assert.StartsWith("Deprecated. ", search.Single(e => e.GetProperty("t").GetString() == "<old_beep>").GetProperty("d").GetString());
        Assert.Contains(".badge.deprecated-badge", DocsSite.ReadAsset("docs.css"));
    }

    [Fact]
    public void Stylesheet_examples_are_highlighted_as_kscript_css()
    {
        DocPage page = Site.Value.Find("/guide/stylesheets");
        Assert.NotNull(page);
        Assert.Contains("<div class=\"code\" data-lang=\"css\">", page.Body);

        string js = DocsSite.ReadAsset("docs.js");
        Assert.Contains("if (lang === 'css' || lang === 'scss' || lang === 'sass') return hlCss(code);", js);

        // Run the page's highlighter on a stylesheet when Node is around (it is on CI); the docs site needs no Node.
        string node = FindOnPath(OperatingSystem.IsWindows() ? "node.exe" : "node");
        if (node == null) return;
        string highlighter = js.Substring(js.IndexOf("  function esc(", StringComparison.Ordinal));
        highlighter = highlighter.Substring(0, highlighter.IndexOf("  function store(", StringComparison.Ordinal))
            + js.Substring(js.IndexOf("// ---------- Highlighting", StringComparison.Ordinal),
                js.IndexOf("document.querySelectorAll('.code')", StringComparison.Ordinal) - js.IndexOf("// ---------- Highlighting", StringComparison.Ordinal));
        string folder = Path.Combine(Path.GetTempPath(), "kscript-docs-test-hl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string sheet = "<let name=\"theme\" value=\"@read_json('theme.json')\" />\n"
                + "/* a comment */\n.p-{$name} { padding: {@rem($px)}; }\n"
                + "@media {@up('md')} { .grid { display: grid; } }\n<if $vars.dark>\n:root { --surface: #111; }\n</if>\n";
            File.WriteAllText(Path.Combine(folder, "hl.js"), highlighter
                + "\nprocess.stdout.write(highlight(require('fs').readFileSync(process.argv[2], 'utf8'), 'css'));\n");
            File.WriteAllText(Path.Combine(folder, "sheet.ks.css"), sheet);
            var info = new System.Diagnostics.ProcessStartInfo(node) { RedirectStandardOutput = true, UseShellExecute = false };
            info.ArgumentList.Add(Path.Combine(folder, "hl.js"));
            info.ArgumentList.Add(Path.Combine(folder, "sheet.ks.css"));
            using var process = System.Diagnostics.Process.Start(info)!;
            string html = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);

            Assert.Contains("<span class=\"hl-tag\">&lt;let</span>", html);
            Assert.Contains("<span class=\"hl-cmd\">@read_json</span>", html);
            Assert.Contains("<span class=\"hl-com\">/* a comment */</span>", html);
            Assert.Contains("<span class=\"hl-tag\">.p-</span><span class=\"hl-br\">{</span><span class=\"hl-var\">$name</span><span class=\"hl-br\">}</span>", html);
            Assert.Contains("<span class=\"hl-attr\">padding</span>", html);
            Assert.Contains("<span class=\"hl-kw\">@media</span> <span class=\"hl-br\">{</span><span class=\"hl-cmd\">@up</span>", html);
            Assert.Contains("<span class=\"hl-tag\">&lt;if</span> <span class=\"hl-var\">$vars</span>", html);
            Assert.Contains("<span class=\"hl-attr\">--surface</span>: <span class=\"hl-num\">#111</span>", html);
            // Removing the tags gives back the stylesheet.
            Assert.Equal(sheet, WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", "")));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    private static string FindOnPath(string file)
    {
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (dir.Length == 0) continue;
            string path = Path.Combine(dir, file);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    [Fact]
    public void The_reference_names_elements_the_way_the_readers_format_writes_them()
    {
        DocsSite site = Site.Value;
        DocPage core = site.Find("/reference/core");
        string html = site.Render(core);

        // Headings, the table of contents and cross references carry both names; the page shows the reader's.
        Assert.Contains("<span class=\"f-xml\"><code>&lt;break&gt;</code></span><span class=\"f-ks\"><code>break</code></span>", core.Body);
        Assert.Contains("<span class=\"f-xml\"><code>&lt;let&gt;</code></span><span class=\"f-ks\"><code>name = value</code></span>", core.Body);
        Assert.Contains("<span class=\"f-xml\"><code>&lt;for&gt;</code></span><span class=\"f-ks\"><code>for item in list</code></span>", core.Body);
        Assert.Contains("<h2 id=\"elements\"><span class=\"f-xml\">Elements</span><span class=\"f-ks\">Statements</span></h2>", core.Body);
        Assert.Contains("A loop: <code>break</code> and <code>continue</code> work inside.", core.Body);
        // Descriptions that name elements read the .kscript way too.
        Assert.Contains("<span class=\"f-ks\">A branch of match:", core.Body);
        Assert.Contains("<span class=\"f-xml\">A branch of &lt;match&gt;:", core.Body);
        Assert.Contains("<a href=\"#el-break\"><span class=\"f-xml\">&lt;break&gt;</span><span class=\"f-ks\">break</span></a>", html);
        Assert.Contains("<a href=\"#elements\"><span class=\"f-xml\">Elements</span><span class=\"f-ks\">Statements</span></a>", html);
        Assert.Equal("Window", DocsSite.CodeName(site.Catalog.GetElement("Window")));
        Assert.Contains("A .kscript file names a package only in its import: write <code>Window { }</code>, never <code>KScript.Window { }</code>.", site.Find("/reference/ui").Body);

        string css = DocsSite.ReadAsset("docs.css");
        Assert.Contains(":root[data-format=\"kscript\"] .f-xml, :root:not([data-format=\"kscript\"]) .f-ks { display: none; }", css);

        // Search matches either name, and shows the reader's.
        var search = JsonDocument.Parse(site.SearchJson()).RootElement.EnumerateArray().ToList();
        JsonElement brk = search.Single(e => e.GetProperty("t").GetString() == "<break>");
        Assert.Equal("break", brk.GetProperty("c").GetString());
        Assert.Equal("name = value", search.Single(e => e.GetProperty("t").GetString() == "<let>").GetProperty("c").GetString());
        Assert.False(search.Single(e => e.GetProperty("t").GetString() == "@now()").TryGetProperty("c", out _));
        string js = DocsSite.ReadAsset("docs.js");
        Assert.Contains("var text = ks && e.c ? e.c : e.t;", js);
        Assert.Contains("var kind = ks && e.k === 'element' ? 'statement' : e.k === 'project' ? 'yours' : e.k;", js);
    }

    #endregion
}
