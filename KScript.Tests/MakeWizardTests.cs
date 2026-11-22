using KScript.Output;
using KScript.Scaffolding;

namespace KScript.Tests;

/// <summary>
/// kscript make &lt;type&gt; with no name: the questions it asks at a terminal, the error it gives without one, the styled
/// help, and the TODO comments in what it makes.
/// </summary>
public sealed class MakeWizardTests : IDisposable
{
    private readonly Func<TextWriter, CliPrompt> previousPrompts = MakeCommand.Prompts;

    public void Dispose() => MakeCommand.Prompts = previousPrompts;

    private static void Answer(string answers) =>
        MakeCommand.Prompts = w => new CliPrompt(new CliOutput(w, colour: false), new StringReader(answers), interactive: true);

    private static (int Code, string Out, string Err) Make(string root, params string[] args)
    {
        var o = new StringWriter { NewLine = "\n" };
        var e = new StringWriter { NewLine = "\n" };
        int code = MakeCommand.Run(args.Concat(new[] { "--dir", root }).ToArray(), o, e);
        return (code, o.ToString(), e.ToString());
    }

    [Fact]
    public void A_page_with_no_name_asks_for_the_name_and_its_options()
    {
        using var site = new TempSite();
        // The name (an empty answer is asked again), the title, and Enter to skip the layout.
        Answer("\nabout\nAbout us\n\n");
        var (code, output, error) = Make(site.Root, "page");
        Assert.True(code == 0, error + output);
        Assert.Contains("New page", output);
        Assert.Contains("What should the page be called?", output);
        Assert.Contains("An answer is needed.", output);
        Assert.Contains("(Enter to skip)", output);
        Assert.Contains("INFO  Created about.html", output);
        Assert.Contains("title=\"About us\"", File.ReadAllText(Path.Combine(site.Root, "about.html")));
    }

    [Fact]
    public void Yes_no_options_are_asked_as_yes_no_questions()
    {
        using var site = new TempSite();
        // Name, --fields skipped, --timestamps yes, --soft-deletes no.
        Answer("Post\n\ny\nn\n");
        var (code, _, error) = Make(site.Root, "model");
        Assert.True(code == 0, error);
        string model = File.ReadAllText(Directory.GetFiles(site.Root, "post.*", SearchOption.AllDirectories).Single());
        Assert.Contains("model Post(timestamps) {", model);
        Assert.DoesNotContain("soft_deletes", model);
        Assert.True(MakeWizard.IsFlag(Scaffolder.FindTemplate(site.Root, "model"), "timestamps"));
        Assert.False(MakeWizard.IsFlag(Scaffolder.FindTemplate(site.Root, "model"), "fields"));
    }

    [Fact]
    public void Given_options_mean_only_the_name_is_asked()
    {
        using var site = new TempSite();
        Answer("contact\n");
        var (code, output, error) = Make(site.Root, "page", "--title", "Say hello");
        Assert.True(code == 0, error);
        Assert.DoesNotContain("Enter to skip", output);
        Assert.Contains("title=\"Say hello\"", File.ReadAllText(Path.Combine(site.Root, "contact.html")));
    }

    [Fact]
    public void Without_a_terminal_or_with_n_a_missing_name_is_an_error()
    {
        using var site = new TempSite();
        MakeCommand.Prompts = w => new CliPrompt(new CliOutput(w, colour: false), new StringReader(""), interactive: false);
        var (code, _, error) = Make(site.Root, "component");
        Assert.Equal(2, code);
        Assert.Equal("   ERROR  Give the component a name, e.g. kscript make component Hero.\n          At a terminal, kscript make component asks for it.\n", error);

        Answer("Hero\n");
        (code, _, error) = Make(site.Root, "component", "-n");
        Assert.Equal(2, code);
        Assert.Contains("Give the component a name", error);
    }

    [Fact]
    public void Help_is_grouped_and_one_type_reads_like_kscript_help()
    {
        using var site = new TempSite();
        var (_, list, _) = Make(site.Root);
        Assert.Contains("Usage:\n  kscript make <type> <name>", list);
        Assert.Contains("Projects:\n  project <folder>", list);
        Assert.Matches(@"Pages and styles:\n  page +An HTML page", list);
        Assert.Contains("  $ kscript make route users --api", list);

        var (_, one, _) = Make(site.Root, "model", "--help");
        Assert.StartsWith("Description:\n  A model in models/", one);
        Assert.Contains("Usage:\n  kscript make model <Name>", one);
        Assert.Matches(@"Options:\n  --fields +The fields", one);
        Assert.Contains("-n, --no-interaction", one);
        Assert.Contains("Examples:\n  $ kscript make model User", one);
    }

    [Fact]
    public void Command_lines_are_coloured_only_when_colour_is_on()
    {
        Assert.Equal("kscript make page <path> [--title x]", MakeCommand.Command(CliStyle.Plain, "kscript make page <path> [--title x]"));
        string coloured = MakeCommand.Command(new CliStyle(true), "kscript make page <path> [--title x]  a note");
        Assert.Contains("\u001b[32mkscript make\u001b[0m", coloured);
        Assert.Contains("\u001b[33m<path>\u001b[0m", coloured);
        Assert.Contains("\u001b[36m--title\u001b[0m", coloured);
        Assert.Contains("\u001b[90m[\u001b[0m", coloured);
        Assert.EndsWith("\u001b[90m  a note\u001b[0m", coloured);
        // A script name isn't taken for a command.
        Assert.StartsWith("\u001b[32mkscript\u001b[0m main.kscript", MakeCommand.Command(new CliStyle(true), "kscript main.kscript"));
    }

    [Theory]
    [InlineData("controller", "Post", "// TODO: Load the list")]
    [InlineData("middleware", "Audit", "// TODO: Change $request")]
    [InlineData("job", "SendMail", "// TODO: Do the work here.")]
    [InlineData("extension", "string --methods shout", "// TODO: Work out the result from $this.")]
    [InlineData("module", "tools", "// TODO: Work out the result from $value.")]
    [InlineData("model", "User", "// TODO: Add the fields")]
    [InlineData("test", "tools", "// TODO: Replace these examples")]
    [InlineData("page", "about", "<!-- TODO: Add what the page shows. -->")]
    public void Made_files_say_what_to_fill_in(string type, string nameAndOptions, string todo)
    {
        using var site = new TempSite();
        var (code, _, error) = Make(site.Root, new[] { type }.Concat(nameAndOptions.Split(' ')).Append("-n").ToArray());
        Assert.True(code == 0, error);
        string made = string.Join("\n", Directory.GetFiles(site.Root, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith("_layout.html", StringComparison.Ordinal)).Select(File.ReadAllText));
        Assert.Contains(todo, made);
    }
}
