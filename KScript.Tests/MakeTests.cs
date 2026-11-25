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
        Assert.Equal("<p>{$.sub_title}</p>", Render("<p>{$.{{ item | var }}}</p>", ("item", "sub-title")));
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
    public void ModelComponentGeneratorRequiresExplicitFieldsAndRendersAWorkingBinding()
    {
        using var site = new TempSite();
        var missing = Make(site.Root, "component", "ProfileEditor", "--model-update", "edit_profile");
        Assert.Equal(2, missing.Code);
        Assert.Contains("--fields", missing.Err);
        var made = Make(site.Root, "component", "ProfileEditor", "--model-update", "edit_profile", "--fields", "bio,website", "--autosave");
        Assert.True(made.Code == 0, made.Err);
        string source = Read(site, "_components/profile-editor.kscript");
        Assert.Contains("model-update=\"edit_profile\" key=\"{$.record.id}\" autosave", source);
        Assert.Contains("name=\"bio\" value=\"{$.record.bio}\"", source);
        Assert.Contains("name=\"website\" value=\"{$.record.website}\"", source);
        Assert.Contains("record=\"{$record}\"", made.Out);
        Assert.Equal(0, Make(site.Root, "component", "ProfileEditor", "--model-update", "edit_profile", "--fields", "bio", "--autosave", "750ms", "--force").Code);
        Assert.Contains("autosave=\"750ms\"", Read(site, "_components/profile-editor.kscript"));
        var live = Make(site.Root, "component", "OrderDetails", "--live");
        Assert.Equal(0, live.Code);
        site.Write("index.html", "<order-details>Order contents</order-details>");
        string html = site.Render("index.html").Body;
        Assert.Contains("data-ks-on-click", html);
        Assert.DoesNotContain("Order contents", html);
    }

    [Fact]
    public void ComponentIsCreatedAndRendersInAPage()
    {
        using var site = new TempSite(("_layout.html", "<main><slot /></main>"));
        var (code, output, err) = Make(site.Root, "component", "Hero", "--props", "title,sub-title");

        Assert.Equal(0, code);
        Assert.Equal("", err);
        Assert.Contains("Created _components/hero.kscript", output);
        Assert.Contains("<hero title=\"…\" sub-title=\"…\" />", output);
        string source = Read(site, "_components/hero.kscript");
        Assert.Contains("type Hero extends Component", source);
        Assert.Contains("fn render(): Component", source);
        Assert.Contains("<p>{$.sub_title}</p>", source);

        // A one-word component works as <hero>, not only hyphenated names.
        site.Write("index.html", "<hero title=\"Hello\" sub-title=\"World\"><b>inner</b></hero>");
        string html = site.Render("index.html").Body;
        Assert.Contains("<p>Hello</p>", html);
        Assert.Contains("<p>World</p>", html);
        Assert.Contains("<b>inner</b>", html);
    }

    [Fact]
    public void ComponentIsWrittenInXmlWhenAskedAndRendersInAPage()
    {
        using var site = new TempSite(("_layout.html", "<main><slot /></main>"));
        var (code, output, err) = Make(site.Root, "component", "Hero", "--props", "title,sub-title", "--syntax", "xml");

        Assert.True(code == 0, err);
        Assert.Contains("Created _components/hero.ks.xml", output);
        Assert.False(File.Exists(Path.Combine(site.Root, "_components/hero.kscript")));
        Assert.Contains("<type name=\"Hero\" extends=\"Component\">", Read(site, "_components/hero.ks.xml"));

        site.Write("index.html", "<hero title=\"Hello\" sub-title=\"World\"><b>inner</b></hero>");
        string html = site.Render("index.html").Body;
        Assert.Contains("<p>Hello</p>", html);
        Assert.Contains("<p>World</p>", html);
        Assert.Contains("<b>inner</b>", html);

        // A project that writes XML (make.syntax) gets XML components too, the model-bound and live ones included.
        site.Write("kscript.json", "{ \"make\": { \"syntax\": \"xml\" } }");
        Assert.Equal(0, Make(site.Root, "model", "Note", "--fields", "title: string", "--component").Code);
        Assert.Equal(0, Make(site.Root, "component", "OrderDetails", "--live").Code);
        Assert.True(File.Exists(Path.Combine(site.Root, "_components/note.ks.xml")));
        Assert.True(File.Exists(Path.Combine(site.Root, "_components/order-details.ks.xml")));
        var o = new StringWriter();
        Assert.True(KScript.LanguageServer.CheckCommand.Run(new[] { site.Root }, o) == 0, o.ToString());
    }

    [Fact]
    public void ModelAndComponentCanBeGeneratedTogetherAndRendered()
    {
        using var site = new TempSite();
        var args = new[] { "model", "Note", "--fields", "title: string, body: string", "--component" };
        var preview = Make(site.Root, args.Append("--dry-run").ToArray());
        Assert.True(preview.Code == 0, preview.Err);
        Assert.Contains("models/note.kscript", preview.Out);
        Assert.Contains("_components/note.kscript", preview.Out);
        Assert.False(File.Exists(Path.Combine(site.Root, "models/note.kscript")));

        var made = Make(site.Root, args);
        Assert.True(made.Code == 0, made.Err);
        Assert.Contains("record: Note", Read(site, "_components/note.kscript"));

        var card = Make(site.Root, "component", "NoteCard", "--model", "Note", "--fields", "title");
        Assert.True(card.Code == 0, card.Err);
        site.Write("index.html", """
            <import path="models/note.kscript" />
            <let name="note" value="Note({ id: 12, title: 'A & B', body: 'Private draft' })" />
            <note record="{$note}" />
            <note-card record="{$note}" />
            """);
        string html = site.Render("index.html").Body;
        Assert.Contains("Note 12", html);
        Assert.Contains("A &amp; B", html);
        Assert.DoesNotContain("Private draft", html);
    }

    [Fact]
    public void ACompanionConflictLeavesTheModelUnwritten()
    {
        using var site = new TempSite(("_components/note.kscript", "mine"));
        Assert.Equal(2, Make(site.Root, "model", "Note", "--component").Code);
        Assert.Equal("mine", Read(site, "_components/note.kscript"));
        Assert.False(File.Exists(Path.Combine(site.Root, "models/note.kscript")));
    }

    [Fact]
    public void ComponentWithoutPropsShowsItsTitle()
    {
        using var site = new TempSite();
        Assert.Equal(0, Make(site.Root, "component", "PricingTable").Code);
        Assert.Contains("<h2>Pricing Table</h2>", Read(site, "_components/pricing-table.kscript"));
    }

    [Theory]
    [InlineData("Section")]
    [InlineData("layout")]
    [InlineData("Input")]
    public void ComponentNamesThatClashWithElementsAreRefused(string name)
    {
        using var site = new TempSite();
        var (code, _, err) = Make(site.Root, "component", name);
        Assert.Equal(2, code);
        Assert.Contains("already an HTML or KScript element", err);
        Assert.False(Directory.Exists(Path.Combine(site.Root, "_components")));
    }

    [Fact]
    public void PageUsesTheLayoutAndRenders()
    {
        using var site = new TempSite(("_layout.html", "<html><title>{$title}</title><body><slot /></body></html>"));
        Assert.Equal(0, Make(site.Root, "page", "AboutUs", "--title", "About the team").Code);

        Assert.Equal("<layout path=\"_layout.html\" title=\"About the team\">\n  <h1>About the team</h1>\n  <!-- TODO: Add what the page shows. -->\n</layout>\n", Read(site, "about-us.html"));
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
        Assert.Equal(2, code);
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

        site.Write("index.html", "<layout path=\"_layouts/main.html\" title=\"Home\"><p>hi</p></layout>");
        string html = site.Render("index.html").Body;
        Assert.Contains("<title>Home</title>", html);
        Assert.Contains("<p>hi</p>", html);
    }

    [Fact]
    public void ScriptRuns()
    {
        using var site = new TempSite();
        // Scripts are .kscript by default, and XML with --syntax xml.
        Assert.Equal(0, Make(site.Root, "script", "tidy_downloads").Code);
        Assert.Equal("// TODO: Write what the script does.\necho \"Hello from Tidy Downloads!\"\n", Read(site, "scripts/tidy-downloads.kscript"));
        var output = new StringWriter();
        new KScript.Runtime.Interpreter(KScript.Runtime.KScriptHost.ForText(output, "")).RunFile(Path.Combine(site.Root, "scripts", "tidy-downloads.kscript"));
        Assert.Contains("Hello from Tidy Downloads!", output.ToString());

        Assert.Equal(0, Make(site.Root, "script", "tidy_downloads", "--syntax", "xml").Code);
        string xml = Read(site, "scripts/tidy-downloads.ks.xml");
        Assert.StartsWith("<kscript>", xml);
        Assert.Contains("Hello from Tidy Downloads!", ScriptRunner.Run(xml));
    }

    [Fact]
    public void DocumentBuildsPdfAndWord()
    {
        using var site = new TempSite();
        Assert.Equal(0, Make(site.Root, "document", "annual_report").Code);
        string script = Read(site, "documents/annual-report.kscript");
        Assert.Contains("template: paper,\n  title: \"Annual Report\"", script);
        new KScript.Runtime.Interpreter(KScript.Runtime.KScriptHost.ForText(new StringWriter(), "")).RunFile(Path.Combine(site.Root, "documents", "annual-report.kscript"));
        Assert.StartsWith("%PDF-", File.ReadAllText(Path.Combine(site.Root, "documents", "annual-report.pdf")));
        Assert.True(File.Exists(Path.Combine(site.Root, "documents", "annual-report.docx")));

        Assert.Equal(0, Make(site.Root, "document", "annual_report", "--syntax", "xml", "--template", "report").Code);
        string xml = Read(site, "documents/annual-report.ks.xml");
        Assert.Matches(@"<KScript.Document\s+template=""report""", xml);
        // The .kscript template's [a, b] list is plain text in XML.
        Assert.Contains("to=\"annual-report.pdf, annual-report.docx\"", xml);
    }

    [Fact]
    public void ErrorTypesAreMadeInLibErrors()
    {
        using var site = new TempSite(("kscript.json", "{}"));
        Assert.Equal(0, Make(site.Root, "error", "payment_failed", "--status", "402", "--extends", "validation", "--fields", "order: int, reason?: string", "--message", "Order {$order} could not be paid").Code);
        Assert.Equal("/// Payment Failed.\nerror PaymentFailed(status: 402, message: \"Order {$order} could not be paid\") extends Validation {\n  order: int\n  reason?: string\n}\n",
            Read(site, "errors/payment-failed.kscript"));
        Assert.Equal(0, Make(site.Root, "error", "Gone").Code);
        Assert.Equal("/// Gone.\nerror Gone(message: \"Gone\")\n", Read(site, "errors/gone.kscript"));

        site.Write("main.kscript", "import \"errors/payment-failed.kscript\"\ntry {\n  throw PaymentFailed(order: 7)\n} catch Validation as e {\n  echo \"{ $e.message } { $e.status }\"\n}\n");
        var output = new StringWriter();
        new KScript.Runtime.Interpreter(KScript.Runtime.KScriptHost.ForText(output, "")).RunFile(Path.Combine(site.Root, "main.kscript"));
        Assert.Contains("Order 7 could not be paid 402", output.ToString());

        Assert.Equal(0, Make(site.Root, "error", "Gone", "--syntax", "xml").Code);
        Assert.Contains("<error name=\"Gone\" message=\"Gone\" description=\"Gone.\" />", Read(site, "errors/gone.ks.xml"));
    }

    [Fact]
    public void ExistingFilesNeedForce()
    {
        using var site = new TempSite(("_components/hero.kscript", "mine"));
        var (code, _, err) = Make(site.Root, "component", "Hero");
        Assert.Equal(2, code);
        Assert.Contains("already exists", err);
        Assert.Equal("mine", Read(site, "_components/hero.kscript"));

        var forced = Make(site.Root, "component", "Hero", "--force");
        Assert.Equal(0, forced.Code);
        Assert.Contains("Replaced _components/hero.kscript", forced.Out);
    }

    [Fact]
    public void DockerKitHonoursTheProjectConfigAndChecksEveryOutputBeforeWriting()
    {
        using var site = new TempSite(("kscript.json", """{"serve":{"root":"public","port":4820}}"""), ("public/index.html", "Ready"));
        string web = Path.Combine(site.Root, "public");
        Assert.Contains("kscript make docker", Make(web, "docker", "--help").Out);
        Assert.Contains("Would create Dockerfile", Make(web, "docker", "--dry-run").Out);
        Assert.False(File.Exists(Path.Combine(site.Root, "compose.yaml")));
        File.WriteAllText(Path.Combine(site.Root, ".dockerignore"), "mine");
        Assert.NotEqual(0, Make(web, "docker").Code);
        Assert.False(File.Exists(Path.Combine(site.Root, "Dockerfile")));
        Assert.Equal("mine", Read(site, ".dockerignore"));

        Assert.Equal(0, Make(web, "docker", "--force").Code);
        Assert.False(File.Exists(Path.Combine(web, "Dockerfile")));
        Assert.Contains("4820:4820", Read(site, "compose.yaml"));
        Assert.Contains("USER app", Read(site, "Dockerfile"));
        Assert.Contains("EXPOSE 4820", Read(site, "Dockerfile"));
        Assert.Contains("HEALTHCHECK", Read(site, "Dockerfile"));
        Assert.Contains("KSCRIPT_SOURCE", Read(site, "compose.yaml"));
    }

    [Fact]
    public void DockerSourceIsPinnedForReleasesAndExplicitForDevelopmentBuilds()
    {
        const string sha = "0123456789abcdef0123456789abcdef01234567";
        Assert.Contains(".git#" + sha, DockerMaker.SourceContext(sha));
        Assert.Contains("KSCRIPT_SOURCE:?", DockerMaker.SourceContext(null));
        Assert.Contains("KSCRIPT_SOURCE:?", DockerMaker.SourceContext("main"));
    }

    [Fact]
    public void BareDockerOptionRendersTheSameKitInADryRun()
    {
        using var parent = new TempSite();
        var project = ProjectScaffolder.Create(parent.Root, "portal", new Dictionary<string, string>
        {
            ["template"] = "bare", ["web"] = "true", ["api"] = "true", ["port"] = "4820", ["docker"] = "true",
        }, dryRun: true);
        Assert.Contains("4820:4820", project.Contents["compose.yaml"]);
        Assert.Contains("**/*.sqlite*", project.Contents[".dockerignore"]);
        Assert.False(Directory.Exists(project.Directory));
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
        foreach (var f in new[] { "component.kscript", "page.html", "markdown.md", "layout.html", "script.kscript", "test.kscript", "document.kscript" })
        {
            Assert.Contains($"Created _templates/{f}", output);
            Assert.True(File.Exists(Path.Combine(site.Root, "_templates", f)));
        }

        // Published templates produce the same files as the built-ins, and are not overwritten again without --force.
        Assert.Equal(0, Make(site.Root, "component", "Hero", "--props", "title").Code);
        Assert.Contains("<p>{$.title}</p>", Read(site, "_components/hero.kscript"));
        site.Write("_templates/page.html", "custom");
        Assert.Contains("Nothing to copy", Make(site.Root, "templates", "page").Out);
        Assert.Equal("custom", Read(site, "_templates/page.html"));
    }

    [Theory]
    [InlineData(new[] { "widget", "Foo" }, "There is no 'widget' template")]
    [InlineData(new[] { "component" }, "Give the component a name")]
    [InlineData(new[] { "document" }, "kscript make document annual-report")]
    [InlineData(new[] { "page", "../outside" }, "is not a valid name")]
    [InlineData(new[] { "page", "about", "us" }, "Did you mean: kscript make page \"about us\"")]
    public void MistakesAreExplained(string[] args, string message)
    {
        using var site = new TempSite();
        var (code, _, err) = Make(site.Root, args);
        Assert.Equal(2, code);
        Assert.Contains(message, err);
    }

    [Fact]
    public void TemplatesCannotWriteOutsideTheProject()
    {
        using var site = new TempSite(("_templates/evil.html", "--- make\nto: ../{{ kebab }}.html\n---\nx"));
        var (code, _, err) = Make(site.Root, "evil", "x");
        Assert.Equal(2, code);
        Assert.Contains("outside the project folder", err);
    }
}
