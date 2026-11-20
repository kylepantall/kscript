namespace KScript.Tests;

/// <summary>kscript agents and kscript check --format from the command line, and where agent docs are kept out.</summary>
public sealed class AgentsCliTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("kscript-agents-cli").FullName;
    private readonly TextWriter stdout = Console.Out;

    public void Dispose()
    {
        Console.SetOut(stdout);
        Directory.Delete(dir, true);
    }

    private string Write(string name, string text)
    {
        string path = Path.Combine(dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private (int Code, string Out, string Err) Kscript(params string[] args)
    {
        var o = new StringWriter { NewLine = "\n" };
        var e = new StringWriter { NewLine = "\n" };
        Console.SetOut(o);
        try
        {
            int code = new CommandLine { Out = o, Error = e }.Run(args);
            return (code, o.ToString(), e.ToString());
        }
        finally
        {
            Console.SetOut(stdout);
        }
    }

    [Fact]
    public void CheckTakesAFormat()
    {
        string file = Write("main.kscript", "import KScript.Math\necho \"hi\"\n");
        Assert.Contains("warning KS6005", Kscript("check", file, "--format", "text").Out);
        Assert.Contains("\"code\": \"KS6005\"", Kscript("check", "--format", "json", file).Out);
        Assert.Contains("\"code\": \"KS6005\"", Kscript("check", file, "--format=json").Out);
        var bad = Kscript("check", file, "--format", "xml");
        Assert.Equal(2, bad.Code);
        Assert.Contains("Unknown --format 'xml'", bad.Err);
        Assert.Contains("\"errors\": 0", Kscript("check", file, "--warnaserror", "--format", "json").Out);
    }

    [Fact]
    public void AgentsRunsFromTheCommandLine()
    {
        Write("main.kscript", "fn one() => 1\n");
        var (code, output, _) = Kscript("agents", dir);
        Assert.Equal(0, code);
        Assert.Contains("wrote AGENTS.md", output);
        Assert.Contains("--check", Kscript("--help").Out);
        Assert.Contains("kscript agents [folder] [--check] [--print]", Kscript("--help").Out);
    }

    [Fact]
    public void ServeNeverServesAgentNotes()
    {
        Write("AGENTS.md", "# notes");
        Write("CLAUDE.md", "@AGENTS.md");
        Write("about.md", "# About");
        var site = new KScript.Pages.PageSite(dir);
        foreach (string path in new[] { "/AGENTS.md", "/AGENTS", "/agents", "/CLAUDE.md", "/CLAUDE" })
        {
            Assert.Equal(KScript.Pages.PageRoute.RouteKind.NotFound, site.Resolve(path).Kind);
        }
        Assert.Equal(KScript.Pages.PageRoute.RouteKind.Page, site.Resolve("/about").Kind);
    }

    [Fact]
    public void ConvertRefreshesAgentDocsInTheNewFormat()
    {
        Write("kscript.json", "{ \"name\": \"conv\" }");
        Write("lib/a.kscript", "fn one() => 1\n");
        Assert.Equal(0, Kscript("agents", dir).Code);
        var o = new StringWriter();
        Assert.Equal(0, KScript.Conversion.ConvertCommand.Run(new[] { dir, "--to", "xml" }, o, new StringWriter()));
        Assert.Contains("AGENTS.md: updated for the converted files", o.ToString());
        string agents = File.ReadAllText(Path.Combine(dir, "AGENTS.md"));
        Assert.Contains("```xml", agents);
        Assert.Contains("lib/a.ks.xml", File.ReadAllText(Path.Combine(dir, ".kscript", "agents", "index.md")));
        Assert.DoesNotContain("lib/a.kscript", File.ReadAllText(Path.Combine(dir, ".kscript", "agents", "index.md")));
    }
}
