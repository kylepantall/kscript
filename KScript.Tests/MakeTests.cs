using KScript.Scaffolding;

namespace KScript.Tests;

public class StubNameTests
{
    [Theory]
    [InlineData("Hero", "hero", "hero", "Hero", "hero", "Hero")]
    [InlineData("HeroBanner", "hero-banner", "hero_banner", "HeroBanner", "heroBanner", "Hero Banner")]
    [InlineData("hero-banner", "hero-banner", "hero_banner", "HeroBanner", "heroBanner", "Hero Banner")]
    [InlineData("hero_banner", "hero-banner", "hero_banner", "HeroBanner", "heroBanner", "Hero Banner")]
    [InlineData("about us", "about-us", "about_us", "AboutUs", "aboutUs", "About Us")]
    [InlineData("FAQPage", "faq-page", "faq_page", "FaqPage", "faqPage", "FAQ Page")]
    [InlineData("Step2Form", "step2-form", "step2_form", "Step2Form", "step2Form", "Step2 Form")]
    [InlineData("[id]", "[id]", "[id]", "[id]", "[id]", "[id]")]
    public void CaseConversions(string input, string kebab, string snake, string pascal, string camel, string title)
    {
        Assert.Equal(kebab, StubNames.Kebab(input));
        Assert.Equal(snake, StubNames.Snake(input));
        Assert.Equal(pascal, StubNames.Pascal(input));
        Assert.Equal(camel, StubNames.Camel(input));
        Assert.Equal(title, StubNames.Title(input));
    }
}

public class StubTemplateTests
{
    private static string Render(string template, params (string Key, string Value)[] values) =>
        StubTemplate.Render(template, values.ToDictionary(v => v.Key, v => v.Value));

    [Fact]
    public void ValuesFiltersAndFallbacks()
    {
        Assert.Equal("Hi hero-banner!", Render("Hi {{ name | kebab }}!", ("name", "HeroBanner")));
        Assert.Equal("HERO", Render("{{name|upper}}", ("name", "hero")));
        Assert.Equal("_layout.html", Render("{{ layout ?? _layout.html }}"));
        Assert.Equal("main.html", Render("{{ layout ?? _layout.html }}", ("layout", "main.html")));
        Assert.Equal("", Render("{{ missing }}"));
    }

    [Fact]
    public void SingleBracesAreLeftForKScript()
    {
        Assert.Equal("<p>{$sub_title}</p>", Render("<p>{${{ item | var }}}</p>", ("item", "sub-title")));
        Assert.Equal("{ $a ? 'x' : 'y' }", Render("{ $a ? 'x' : 'y' }"));
    }

    [Fact]
    public void IfElseAndUnless()
    {
        const string t = "{{#if live}}live{{else}}static{{/if}}/{{#unless live}}no{{/unless}}";
        Assert.Equal("live/", Render(t, ("live", "true")));
        Assert.Equal("static/no", Render(t));
        Assert.Equal("static/no", Render(t, ("live", "false")));
    }

    [Fact]
    public void EachRepeatsForCommaSeparatedValues()
    {
        Assert.Equal("<a b c>", Render("<{{#each xs}}{{ item }}{{#unless last}} {{/unless}}{{/each}}>", ("xs", "a, b,,c")));
        Assert.Equal("none", Render("{{#each xs}}{{ item }}{{else}}none{{/each}}"));
    }

    [Fact]
    public void BlockTagsOnTheirOwnLineLeaveNoBlankLines()
    {
        const string t = "<div>\n  {{#each props}}\n  <p>{{ item }}</p>\n  {{/each}}\n</div>\n";
        Assert.Equal("<div>\n  <p>a</p>\n  <p>b</p>\n</div>\n", Render(t, ("props", "a,b")));
        Assert.Equal("<div>\n</div>\n", Render(t));
    }

    [Theory]
    [InlineData("{{#if x}}never closed", "never closed")]
    [InlineData("{{/if}}", "no matching")]
    [InlineData("{{#if x}}a{{/each}}", "closes")]
    [InlineData("{{#loop x}}{{/loop}}", "Unknown block")]
    [InlineData("{{ x | shout }}", "Unknown filter")]
    [InlineData("{{#if x}}a{{else}}b{{else}}c{{/if}}", "more than one")]
    public void ErrorsSayWhatIsWrong(string template, string message)
    {
        var ex = Assert.Throws<StubException>(() => Render(template, ("x", "1")));
        Assert.Contains(message, ex.Message);
    }
}

public class ScaffolderTests
{
    private static (int Code, string Out, string Err) Make(string root, params string[] args)
    {
        var o = new StringWriter { NewLine = "\n" };
        var e = new StringWriter { NewLine = "\n" };
        int code = MakeCommand.Run(args.Concat(new[] { "--dir", root }).ToArray(), o, e);
        return (code, o.ToString(), e.ToString());
    }

    private static string Read(TempSite site, string path) => File.ReadAllText(Path.Combine(site.Root, path)).Replace("\r\n", "\n");

    [Fact]
    public void ComponentIsCreatedAndRendersInAPage()
    {
        using var site = new TempSite(("_layout.html", "<main><slot /></main>"));
        var (code, output, err) = Make(site.Root, "component", "Hero", "--props", "title,sub-title");

        Assert.Equal(0, code);
        Assert.Equal("", err);
        Assert.Contains("Created _components/hero.html", output);
        Assert.Contains("<hero title=\"…\" sub-title=\"…\" />", output);
        Assert.Equal("<section class=\"hero\">\n  <p>{$title}</p>\n  <p>{$sub_title}</p>\n  <slot />\n</section>\n", Read(site, "_components/hero.html"));

        // A one-word component works as <hero>, not only hyphenated names.
        site.Write("index.html", "<hero title=\"Hello\" sub-title=\"World\"><b>inner</b></hero>");
        string html = site.Render("index.html").Body;
        Assert.Contains("<p>Hello</p>", html);
        Assert.Contains("<p>World</p>", html);
        Assert.Contains("<b>inner</b>", html);
    }

    [Fact]
    public void ComponentWithoutPropsShowsItsTitle()
    {
        using var site = new TempSite();
        Assert.Equal(0, Make(site.Root, "component", "PricingTable").Code);
        Assert.Contains("<h2>Pricing Table</h2>", Read(site, "_components/pricing-table.html"));
    }

    [Theory]
    [InlineData("Section")]
    [InlineData("layout")]
    [InlineData("Input")]
    public void ComponentNamesThatClashWithElementsAreRefused(string name)
    {
        using var site = new TempSite();
        var (code, _, err) = Make(site.Root, "component", name);
        Assert.Equal(1, code);
        Assert.Contains("already an HTML or KScript element", err);
        Assert.False(Directory.Exists(Path.Combine(site.Root, "_components")));
    }

    [Fact]
    public void PageUsesTheLayoutAndRenders()
    {
        using var site = new TempSite(("_layout.html", "<html><title>{$title}</title><body><slot /></body></html>"));
        Assert.Equal(0, Make(site.Root, "page", "AboutUs", "--title", "About the team").Code);

        Assert.Equal("<layout src=\"_layout.html\" title=\"About the team\">\n  <h1>About the team</h1>\n</layout>\n", Read(site, "about-us.html"));
        string html = site.Render("about-us.html").Body;
        Assert.Contains("<title>About the team</title>", html);
        Assert.Contains("<h1>About the team</h1>", html);
    }

    [Fact]
    public void FirstPageInAnEmptyFolderAlsoGetsTheDefaultLayout()
    {
        using var site = new TempSite();
        var (code, output, err) = Make(site.Root, "page", "main");
        Assert.Equal(0, code);
        Assert.Equal("", err);
        Assert.Contains("Created main.html", output);
        Assert.Contains("Created _layout.html", output);
        Assert.Contains("<slot />", Read(site, "_layout.html"));

        string html = site.Render("main.html").Body;
        Assert.Contains("<title>Main</title>", html);
        Assert.Contains("<h1>Main</h1>", html);

        // The next page reuses it, and an explicit --layout never creates _layout.html.
        Assert.DoesNotContain("_layout.html", Make(site.Root, "page", "about").Out);
        using var other = new TempSite();
        Assert.DoesNotContain("_layout.html", Make(other.Root, "md", "post", "--layout", "_layouts/blog.html").Out);
        Assert.False(File.Exists(Path.Combine(other.Root, "_layout.html")));
    }

    [Fact]
    public void ATypeGivenTwiceSuggestsTheCommand()
    {
        using var site = new TempSite();
        var (code, _, err) = Make(site.Root, "markdown", "page", "about", "--title", "About us");
        Assert.Equal(1, code);
        Assert.Contains("kscript make markdown takes one name, but got 'page' and 'about'.", err);
        Assert.Contains("Did you mean: kscript make markdown about --title \"About us\"", err);
        Assert.Empty(Directory.GetFiles(site.Root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void DynamicPageReadsItsRouteParameters()
    {
        using var site = new TempSite(("_layout.html", "<slot />"));
        var (code, output, _) = Make(site.Root, "page", "people/[id]");
        Assert.Equal(0, code);
        Assert.Contains("Created people/[id].html", output);

        string page = Read(site, "people/[id].html");
        Assert.Contains("<let name=\"id\" value=\"$request.params.id\" />", page);
        Assert.Contains("title=\"People\"", page);

        var request = new Dictionary<string, object> { ["params"] = new Dictionary<string, object> { ["id"] = "42" } };
        Assert.Contains("<p>id: 42</p>", site.Render("people/[id].html", request).Body);
    }

    [Fact]
    public void MarkdownPageHasFrontMatter()
    {
        using var site = new TempSite(("_layouts/blog.html", "<article><h1>{$title}</h1><slot /></article>"));
        Assert.Equal(0, Make(site.Root, "md", "blog/FirstPost", "--layout", "_layouts/blog.html").Code);

        string md = Read(site, "blog/first-post.md");
        Assert.StartsWith("---\ntitle: First Post\nlayout: _layouts/blog.html\n---\n", md);
        string html = site.Render("blog/first-post.md").Body;
        Assert.Contains("<h1>First Post</h1>", html);
    }

    [Fact]
    public void LayoutCanListStylesheets()
    {
        using var site = new TempSite();
        Assert.Equal(0, Make(site.Root, "layout", "Main", "--css", "/site.css,/print.css", "--lang", "en-GB").Code);
        string layout = Read(site, "_layouts/main.html");
        Assert.Contains("<html lang=\"en-GB\">", layout);
        Assert.Contains("<link rel=\"stylesheet\" href=\"/site.css\">\n    <link rel=\"stylesheet\" href=\"/print.css\">", layout);

        site.Write("index.html", "<layout src=\"_layouts/main.html\" title=\"Home\"><p>hi</p></layout>");
        string html = site.Render("index.html").Body;
        Assert.Contains("<title>Home</title>", html);
        Assert.Contains("<p>hi</p>", html);
    }

    [Fact]
    public void ScriptRuns()
    {
        using var site = new TempSite();
        // Scripts are .kscript by default, and XML with --format xml.
        Assert.Equal(0, Make(site.Root, "script", "tidy_downloads").Code);
        Assert.Equal("echo \"Hello from Tidy Downloads!\"\n", Read(site, "scripts/tidy-downloads.kscript"));
        var output = new StringWriter();
        new KScript.Runtime.Interpreter(KScript.Runtime.KScriptHost.ForText(output, "")).RunFile(Path.Combine(site.Root, "scripts", "tidy-downloads.kscript"));
        Assert.Contains("Hello from Tidy Downloads!", output.ToString());

        Assert.Equal(0, Make(site.Root, "script", "tidy_downloads", "--format", "xml").Code);
        string xml = Read(site, "scripts/tidy-downloads.ks.xml");
        Assert.StartsWith("<kscript>", xml);
        Assert.Contains("Hello from Tidy Downloads!", ScriptRunner.Run(xml));
    }

    [Fact]
    public void ExistingFilesNeedForce()
    {
        using var site = new TempSite(("_components/hero.html", "mine"));
        var (code, _, err) = Make(site.Root, "component", "Hero");
        Assert.Equal(1, code);
        Assert.Contains("already exists", err);
        Assert.Equal("mine", Read(site, "_components/hero.html"));

        var forced = Make(site.Root, "component", "Hero", "--force");
        Assert.Equal(0, forced.Code);
        Assert.Contains("Replaced _components/hero.html", forced.Out);
    }

    [Fact]
    public void DryRunPrintsWithoutWriting()
    {
        using var site = new TempSite();
        var (code, output, _) = Make(site.Root, "page", "contact", "--dry-run");
        Assert.Equal(0, code);
        Assert.Contains("Would create contact.html", output);
        Assert.Contains("<h1>Contact</h1>", output);
        Assert.False(File.Exists(Path.Combine(site.Root, "contact.html")));
    }

    [Fact]
    public void ProjectTemplateReplacesTheBuiltIn()
    {
        using var site = new TempSite(("_templates/component.html",
            "--- make\nto: _components/{{ kebab }}.html\nhint: Custom!\n---\n<div class=\"{{ kebab }} {{ variant ?? plain }}\"><slot /></div>\n"));
        var (code, output, _) = Make(site.Root, "component", "HeroBanner", "--variant", "dark");

        Assert.Equal(0, code);
        Assert.Contains("(from _templates/component.html)", output);
        Assert.Contains("Custom!", output);
        Assert.Equal("<div class=\"hero-banner dark\"><slot /></div>\n", Read(site, "_components/hero-banner.html"));
    }

    [Fact]
    public void ProjectTemplatesAddNewTypes()
    {
        using var site = new TempSite(
            ("_templates/api.html", "--- make\ndescription: A JSON endpoint\nto: api/{{ path }}.html\n---\n<respond json=\"{ ok: true, name: '{{ name }}' }\" />\n"),
            ("_templates/note.txt", "{{ title }}\n"));

        var list = Make(site.Root);
        Assert.Contains("api", list.Out);
        Assert.Contains("A JSON endpoint  [_templates/api.html]", list.Out);
        Assert.Contains("note", list.Out);

        Assert.Equal(0, Make(site.Root, "api", "users/List").Code);
        Assert.True(File.Exists(Path.Combine(site.Root, "api/users/list.html")));

        // Without a header the file keeps the template's extension and goes where the name says.
        Assert.Equal(0, Make(site.Root, "note", "docs/ReleasePlan").Code);
        Assert.Equal("Release Plan\n", Read(site, "docs/release-plan.txt"));
    }

    [Fact]
    public void PublishCopiesBuiltInsToCustomise()
    {
        using var site = new TempSite();
        var (code, output, _) = Make(site.Root, "templates");
        Assert.Equal(0, code);
        foreach (var f in new[] { "component.html", "page.html", "markdown.md", "layout.html", "script.kscript", "test.kscript" })
        {
            Assert.Contains($"Created _templates/{f}", output);
            Assert.True(File.Exists(Path.Combine(site.Root, "_templates", f)));
        }

        // Published templates produce the same files as the built-ins, and are not overwritten again without --force.
        Assert.Equal(0, Make(site.Root, "component", "Hero", "--props", "title").Code);
        Assert.Contains("<p>{$title}</p>", Read(site, "_components/hero.html"));
        site.Write("_templates/page.html", "custom");
        Assert.Contains("Nothing to copy", Make(site.Root, "templates", "page").Out);
        Assert.Equal("custom", Read(site, "_templates/page.html"));
    }

    [Theory]
    [InlineData(new[] { "widget", "Foo" }, "There is no 'widget' template")]
    [InlineData(new[] { "component" }, "Give the component a name")]
    [InlineData(new[] { "page", "../outside" }, "is not a valid name")]
    [InlineData(new[] { "page", "about", "us" }, "Did you mean: kscript make page \"about us\"")]
    public void MistakesAreExplained(string[] args, string message)
    {
        using var site = new TempSite();
        var (code, _, err) = Make(site.Root, args);
        Assert.Equal(1, code);
        Assert.Contains(message, err);
    }

    [Fact]
    public void TemplatesCannotWriteOutsideTheProject()
    {
        using var site = new TempSite(("_templates/evil.html", "--- make\nto: ../{{ kebab }}.html\n---\nx"));
        var (code, _, err) = Make(site.Root, "evil", "x");
        Assert.Equal(1, code);
        Assert.Contains("outside the project folder", err);
    }
}
