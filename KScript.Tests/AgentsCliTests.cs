namespace KScript.Tests;

/// <summary>kscript agents and kscript check --output from the command line, and where agent docs are kept out.</summary>
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
        Assert.Contains("warning KS6005", Kscript("check", file, "--output", "text").Out);
        Assert.Contains("\"code\": \"KS6005\"", Kscript("check", "--output", "json", file).Out);
        Assert.Contains("\"code\": \"KS6005\"", Kscript("check", file, "--output=json").Out);
        var bad = Kscript("check", file, "--output", "xml");
        Assert.Equal(2, bad.Code);
        Assert.Contains("Unknown --output 'xml'", bad.Err);
        Assert.Contains("\"errors\": 0", Kscript("check", file, "--warnings-as-errors", "--output", "json").Out);
    }

    [Fact]
    public void AgentsRunsFromTheCommandLine()
    {
        Write("main.kscript", "fn one() => 1\n");
        var (code, output, _) = Kscript("agents", dir);
        Assert.Equal(0, code);
        Assert.Contains("wrote AGENTS.md", output);
        Assert.Contains("--check", Kscript("help", "agents").Out);
        Assert.Contains("kscript agents [folder] [--check] [--print]", Kscript("help", "agents").Out);
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
        Assert.Equal(0, KScript.Conversion.ConvertCommand.Run(new[] { dir, "--syntax", "xml" }, o, new StringWriter()));
        Assert.Contains("AGENTS.md: updated for the converted files", o.ToString());
        string agents = File.ReadAllText(Path.Combine(dir, "AGENTS.md"));
        Assert.Contains("```xml", agents);
        Assert.Contains("lib/a.ks.xml", File.ReadAllText(Path.Combine(dir, ".kscript", "agents", "index.md")));
        Assert.DoesNotContain("lib/a.kscript", File.ReadAllText(Path.Combine(dir, ".kscript", "agents", "index.md")));
    }

    [Fact]
    public void WatchRunsFromTheCommandLine()
    {
        Write("main.kscript", "fn one() => 1\n");
        Assert.Equal(2, Kscript("watch", dir).Code);
        Assert.Equal(0, Kscript("agents", dir).Code);
        Write("main.kscript", "fn one() => 1\nfn two() => 2\n");
        Assert.Equal(1, Kscript("watch", dir, "--check").Code);
        var once = Kscript("watch", dir, "--once");
        Assert.Equal(0, once.Code);
        Assert.Contains("agents: updated", once.Out);
        Assert.Contains("kscript watch [folder] [--once] [--check]", Kscript("help", "watch").Out);

        var o = new StringWriter { NewLine = "\n" };
        Assert.Equal(0, new CommandLine { Out = o, Error = new StringWriter(), Cancellation = new CancellationToken(true) }.Run(new[] { "watch", dir }));
        Assert.Contains("Watching", o.ToString());
    }

    [Fact]
    public async Task ServeKeepsTheAgentDocsUpToDate()
    {
        Write("kscript.json", "{ \"name\": \"site\", \"serve\": { \"port\": 0, \"host\": \"127.0.0.1\" } }");
        Write("index.html", "<h1>Hi</h1>");
        Write("lib/tasks.kscript", "fn one() => 1\n");
        Assert.Equal(0, Kscript("agents", dir).Code);
        string index = Path.Combine(dir, ".kscript", "agents", "index.md");

        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var o = new StringWriter();
        var e = new StringWriter();
        Task<int> serving = Task.Run(() => new CommandLine { Out = TextWriter.Synchronized(o), Error = e, Cancellation = stop.Token }.Run(new[] { "serve", dir }));
        Thread.Sleep(500);
        Write("lib/tasks.kscript", "fn one() => 1\nfn two() => 2\n");
        bool updated = SpinWait.SpinUntil(() => { try { return File.ReadAllText(index).Contains("@two()"); } catch (IOException) { return false; } }, 15_000);
        stop.Cancel();
        Assert.True(updated, o.ToString() + e);
        Assert.Equal(0, await serving);

        // Without agent docs, serve watches nothing.
        Directory.Delete(Path.Combine(dir, ".kscript"), true);
        var cmd = new CommandLine { Out = new StringWriter(), Error = new StringWriter() };
        cmd.WatchOutputs(dir, new CancellationToken(true));
        cmd.WatchOutputs(Path.Combine(dir, "missing"), new CancellationToken(true));
    }
}
