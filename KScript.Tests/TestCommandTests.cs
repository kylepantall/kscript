using KScript.Testing;

namespace KScript.Tests;

/// <summary>kscript test's options and the less common ways test files pass, fail or can't run.</summary>
public class TestCommandTests
{
    private static (int Code, string Out, string Err) Command(string cwd, CancellationToken cancel, bool? color, params string[] args)
    {
        var o = new StringWriter { NewLine = "\n" };
        var e = new StringWriter { NewLine = "\n" };
        int code = TestCommand.Run(args, o, e, cancel, color, cwd);
        return (code, o.ToString(), e.ToString());
    }

    private static (int Code, string Out, string Err) Command(string cwd, params string[] args) => Command(cwd, default, false, args);

    private static TestResult Single(string body)
    {
        using var site = new TempSite(("t.test.ks.xml", "<kscript>" + body + "</kscript>"), ("child.ks.xml", "<kscript><echo>child</echo></kscript>"));
        TestFileResult file = TestRunner.RunFile(Path.Combine(site.Root, "t.test.ks.xml"), new TestRunSettings { BaseDirectory = site.Root });
        Assert.Null(file.Error);
        return Assert.Single(file.Tests);
    }

    private static string Fails(string check)
    {
        TestResult r = Single($"<fn name=\"f\" value=\"1\" /><test name=\"t\">{check}</test>");
        Assert.Equal(TestOutcome.Failed, r.Outcome);
        return r.Message;
    }

    [Fact]
    public void Options_help_and_the_current_project()
    {
        using var site = new TempSite(
            ("kscript.json", "{ \"name\": \"demo\" }"),
            ("tests/a.test.ks.xml", "<kscript><test name=\"ok\"><assert test=\"true\" /></test></kscript>"),
            ("tests/empty.test.ks.xml", "<kscript></kscript>"));

        var (code, output, _) = Command(site.Root, "--timeout", "5");
        Assert.Equal(0, code);
        Assert.Contains("tests/a.test.ks.xml", output);
        Assert.Contains("(no tests in this file)", output);

        Assert.Contains("Usage: kscript test", Command(site.Root, "--help").Out);
        Assert.Equal(0, Command(site.Root, CancellationToken.None, (bool?)null, Path.Combine(site.Root, "tests", "a.test.ks.xml")).Code);

        string folder = Path.Combine(site.Root, "tests");
        var (junitCode, _, junitError) = Command(site.Root, "--junit", folder);
        Assert.Equal(1, junitCode);
        Assert.Contains("Could not write", junitError);

        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var (stopped, _, stoppedError) = Command(site.Root, cancel.Token, false);
        Assert.Equal(130, stopped);
        Assert.Contains("Stopped.", stoppedError);
    }

    [Fact]
    public void A_broken_project_file_is_reported()
    {
        using var site = new TempSite(("kscript.json", "{ not json"), ("a.test.ks.xml", "<kscript />"));
        var (code, _, error) = Command(site.Root);
        Assert.Equal(1, code);
        Assert.Contains("kscript.json", error);

        TestFileResult file = TestRunner.RunFile(Path.Combine(site.Root, "a.test.ks.xml"), new TestRunSettings { BaseDirectory = site.Root });
        Assert.Contains("kscript.json", file.Error);
        Assert.Single(TestRunner.Discover(new[] { site.Root }));
    }

    [Fact]
    public void Folders_that_cannot_be_read_are_skipped()
    {
        // A folder even root can't list (Linux's /proc/1/map_files); elsewhere there's nothing to check.
        const string unreadable = "/proc/1/map_files";
        try
        {
            Directory.EnumerateFiles(unreadable).ToList();
            return;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
        }

        using var site = new TempSite(("a.test.ks.xml", "<kscript />"));
        Directory.CreateSymbolicLink(Path.Combine(site.Root, "locked"), unreadable);
        Assert.Single(TestRunner.Discover(new[] { site.Root }));
    }

    [Fact]
    public void A_crash_while_reporting_is_the_files_error()
    {
        using var site = new TempSite(("a.test.ks.xml", "<kscript><test name=\"t\" /></kscript>"));
        TestFileResult file = TestRunner.RunFile(Path.Combine(site.Root, "a.test.ks.xml"), new TestRunSettings { BaseDirectory = site.Root }, _ => throw new InvalidOperationException("reporter broke"));
        Assert.Equal("InvalidOperationException: reporter broke", file.Error);
    }

    [Fact]
    public void Checks_explain_their_failures()
    {
        Assert.Equal("@assert() got false, which isn't true.", Fails("<do>@assert(false)</do>"));
        Assert.Equal("custom", Fails("<do>@assert(0, 'custom')</do>"));
        Assert.Contains("fails: ", Fails("<let name=\"m\" value=\"{}\" /><assert test=\"false and $m.a.b\" />"));
        Assert.Contains("isn't a valid regular expression", Fails("<expect value=\"'a'\" matches=\"'['\" />"));
        Assert.Contains("which has no count", Fails("<expect value=\"5\" count=\"1\" />"));
        Assert.Contains("checks a list, text or a map, but 5 is 5", Fails("<expect value=\"5\" contains=\"1\" />"));
        Assert.Contains("It has an extra 'b': 2", Fails("<expect value=\"{ a: 1, b: 2 }\" equals=\"{ a: 1 }\" />"));
        Assert.Equal("Expected [1] to equal ['1'], but it was [1].", Fails("<expect value=\"[1]\" equals=\"['1']\" />"));
        Assert.Equal("Expected { a: 1 } to equal { a: '1' }, but it was { a: 1 }.", Fails("<expect value=\"{ a: 1 }\" equals=\"{ a: '1' }\" />"));
        Assert.Contains("fn f", Fails("<expect value=\"@f\" equals=\"1\" />"));
        Assert.Contains("2026-01-02", Fails("<expect value=\"@date('2026-01-02')\" equals=\"1\" />"));
        Assert.Contains("takes its options as a map", Fails("<do>@run_script('child.ks.xml', 5)</do>"));
    }

    [Fact]
    public void Hooks_loops_and_signals_inside_tests()
    {
        TestResult afterFails = Single("<after-each><throw message=\"cleanup broke\" /></after-each><test name=\"t\"><do>1</do></test>");
        Assert.Equal(TestOutcome.Failed, afterFails.Outcome);
        Assert.Contains("cleanup broke", afterFails.Message);

        TestResult loose = Single("<test name=\"t\"><break /></test>");
        Assert.Equal(TestOutcome.Failed, loose.Outcome);
        Assert.Contains("<break/>", loose.Message);

        using var site = new TempSite(("t.test.ks.xml", "<kscript><test name=\"none {$x}\" each=\"x\" in=\"null\" /></kscript>"), ("u.test.ks.xml", "<kscript><test name=\"t\" each=\"x\" in=\"5\" /></kscript>"));
        Assert.Empty(TestRunner.RunFile(Path.Combine(site.Root, "t.test.ks.xml"), new TestRunSettings { BaseDirectory = site.Root }).Tests);
        Assert.Contains("runs over a list or a map", TestRunner.RunFile(Path.Combine(site.Root, "u.test.ks.xml"), new TestRunSettings { BaseDirectory = site.Root }).Error);
    }
}
