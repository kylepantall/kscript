using KScript.Scaffolding;

namespace KScript.Tests;

/// <summary>kscript make's less common answers: bad names, broken templates, folders that get in the way.</summary>
public class ScaffoldingCoverageTests
{
    private static (int Code, string Out, string Err) Make(params string[] args)
    {
        var o = new StringWriter { NewLine = "\n" };
        var e = new StringWriter { NewLine = "\n" };
        int code = MakeCommand.Run(args, o, e);
        return (code, o.ToString(), e.ToString());
    }

    private static string Fails(Action make) => Assert.Throws<StubException>(make).Message;

    [Theory]
    [InlineData("component", "Hero")]
    [InlineData("page", "about")]
    [InlineData("markdown", "blog/first-post")]
    [InlineData("layout", "main")]
    [InlineData("script", "tidy-downloads")]
    [InlineData("controller", "people")]
    [InlineData("request", "NewPerson")]
    [InlineData("middleware", "secure-headers")]
    [InlineData("view", "people/show")]
    [InlineData("partial", "footer")]
    [InlineData("css", "site")]
    [InlineData("storage", "people")]
    [InlineData("cache", "api-cache")]
    [InlineData("job", "send-reminders")]
    [InlineData("window", "settings")]
    [InlineData("config", "staging")]
    [InlineData("env", "base")]
    [InlineData("locale", "fr")]
    [InlineData("logger", "alerts")]
    [InlineData("card", "Example")]
    public void A_missing_name_gets_an_example(string type, string example)
    {
        using var site = new TempSite(("_templates/card.html", "<div class=\"card\"></div>"));
        string message = Fails(() => Scaffolder.Make(new MakeRequest { Type = type, Name = " ", Root = site.Root }));
        Assert.Contains($"kscript make {type} {example}.", message);
    }

    [Fact]
    public void Names_and_templates_that_cant_work()
    {
        using var site = new TempSite();
        Assert.Contains("has no letters or digits to make a file name", Fails(() => Scaffolder.Make(new MakeRequest { Type = "page", Name = "!!!", Root = site.Root })));
        Assert.Contains("to make a component name", Fails(() => Scaffolder.Make(new MakeRequest { Type = "component", Name = "!!!", Root = site.Root })));
        Assert.Contains("must start with a letter", Fails(() => Scaffolder.Make(new MakeRequest { Type = "component", Name = "2x", Root = site.Root })));
        Assert.Contains("There is no built-in 'nope' template", Fails(() => Scaffolder.PublishTemplates(site.Root, false, new[] { "nope" })));

        site.Write("_templates/card.html", "<div />");
        site.Write("_templates/card.md", "# Card");
        Assert.Contains("has more than one 'card' template", Fails(() => Scaffolder.Make(new MakeRequest { Type = "card", Name = "x", Root = site.Root })));

        Assert.Contains("is never closed", Fails(() => Scaffolder.SplitHeader("--- make\nto: x\n", "t.html")));
        Assert.Contains("needs a value name", Fails(() => StubTemplate.Render("{{#if}}x{{/if}}", new Dictionary<string, string>())));
        Assert.Equal("ab", StubTemplate.Render("{{ a | lower | trim }}", new Dictionary<string, string> { ["a"] = " AB " }));
    }

    [Fact]
    public void The_top_index_page_is_home()
    {
        using var site = new TempSite(("_layout.html", "<slot />"));
        var result = Scaffolder.Make(new MakeRequest { Type = "page", Name = "index", Root = site.Root, DryRun = true });
        Assert.Contains("Home", result.Content);
    }

    [Fact]
    public void Projects_need_a_folder_name_that_isnt_a_file()
    {
        using var site = new TempSite(("taken", "a file"));
        Assert.Contains("Give the project a folder name", Fails(() => ProjectScaffolder.Create(site.Root, " ")));
        Assert.Contains("is a file, not a folder", Fails(() => ProjectScaffolder.Create(site.Root, "taken")));
    }

    [Fact]
    public void The_command_reports_what_gets_in_the_way()
    {
        using var site = new TempSite(("_layout.html", "<slot />"), ("blog", "a file, not a folder"));
        Directory.CreateDirectory(Path.Combine(site.Root, "about.html"));

        Assert.Contains("does not exist", Make("page", "x", "--dir", Path.Combine(site.Root, "missing")).Err);
        var post = Make("page", "blog/post", "--dir", site.Root);
        Assert.True(post.Code == 1, post.Out + post.Err);
        var about = Make("page", "about", "--dir", site.Root, "--force");
        Assert.True(about.Code == 1, about.Out + about.Err);

        using var broken = new TempSite(("kscript.json", "{ nope"));
        string previous = Directory.GetCurrentDirectory();
        (int code, string err) = (0, null);
        try
        {
            Directory.SetCurrentDirectory(broken.Root);
            (code, _, err) = Make("page", "x");
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
        }
        Assert.Equal(1, code);
        Assert.Contains("kscript.json", err);
    }
}
