using KScript.Runtime.Dependencies;
using KScript.Testing;

namespace KScript.Tests;

/// <summary>kscript test runs only the tests a change can affect, from what each test reached when it last ran.</summary>
public sealed class TestSelectionTests : IDisposable
{
    private readonly string ci = Environment.GetEnvironmentVariable("CI");
    private readonly TempSite site;

    public TestSelectionTests()
    {
        Environment.SetEnvironmentVariable("CI", null);
        site = new TempSite(
            ("kscript.json", "{ \"name\": \"demo\" }"),
            ("types/user.kscript", "/// A user.\ntype User {\n  name: string\n\n  fn shout() => $name->upper()\n}\n"),
            ("types/order.kscript", "/// An order.\ntype Order {\n  total: int\n}\n"),
            ("services/greeter.kscript", "import User from \"../types/user.kscript\"\n\nfn greet(user: User) => \"Hello \" + $user.name\n\nfn wave(user: User) => \"Wave \" + $user->shout()\n"),
            ("services/billing.kscript", "import Order from \"../types/order.kscript\"\n\nfn total(order: Order) => $order.total * 2\n"),
            ("tests/greeter.test.kscript",
                "import \"../services/greeter.kscript\"\nimport \"../services/billing.kscript\"\nimport \"../types/user.kscript\"\nimport \"../types/order.kscript\"\n\n"
                + "test(name: \"greets\") {\n  expect(@greet(User(name: 'Ann')), equals: 'Hello Ann')\n}\n\n"
                + "test(name: \"waves\") {\n  expect(@wave(User(name: 'Ann')), equals: 'Wave ANN')\n}\n\n"
                + "test(name: \"bills\") {\n  expect(@total(Order(total: 2)), equals: 4)\n}\n"),
            ("tests/math.test.kscript", "test(name: \"adds\") {\n  expect(1 + 1, equals: 2)\n}\n"));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("CI", ci);
        site.Dispose();
    }

    private (int Code, string Out) Test(params string[] args)
    {
        var o = new StringWriter { NewLine = "\n" };
        var e = new StringWriter { NewLine = "\n" };
        int code = TestCommand.Run(args, o, e, default, false, site.Root);
        return (code, o.ToString() + e.ToString());
    }

    /// <summary>The names of the tests that ran.</summary>
    private static List<string> Ran(string output) =>
        output.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("✓ ", StringComparison.Ordinal) || l.StartsWith("✗ ", StringComparison.Ordinal))
            .Select(l => l.Substring(2).Split(" (")[0]).ToList();

    private void Edit(string path, string from, string to)
    {
        string full = Path.Combine(site.Root, path);
        string text = File.ReadAllText(full);
        Assert.Contains(from, text);
        File.WriteAllText(full, text.Replace(from, to));
    }

    [Fact]
    public void The_first_run_runs_everything_and_the_next_runs_nothing_until_something_changes()
    {
        Assert.Equal(new[] { "greets", "waves", "bills", "adds" }, Ran(Test().Out));
        Assert.True(File.Exists(TestGraph.PathFor(site.Root)));
        Assert.Contains("*", File.ReadAllText(Path.Combine(site.Root, ".kscript", "cache", ".gitignore")));

        var (code, output) = Test();
        Assert.Equal(0, code);
        Assert.Empty(Ran(output));
        Assert.Contains("4 unchanged since the last run", output);
        Assert.Contains("Nothing the tests reach changed since the last run", output);

        Assert.Equal(4, Ran(Test("--all").Out).Count);
    }

    [Fact]
    public void Changing_a_method_reruns_only_the_tests_that_reached_it()
    {
        Test();
        Edit("types/user.kscript", "$name->upper()", "$name->upper() + ''");
        Assert.Equal(new[] { "waves" }, Ran(Test().Out));

        // A field is part of the type: every test that used User runs.
        Edit("types/user.kscript", "  name: string\n", "  name: string\n  age: int = 0\n");
        Assert.Equal(new[] { "greets", "waves" }, Ran(Test().Out));

        Edit("services/billing.kscript", "* 2", "* 2 + 0");
        Assert.Equal(new[] { "bills" }, Ran(Test().Out));
    }

    [Fact]
    public void Failed_tests_run_again_until_they_pass_and_failed_runs_only_them()
    {
        Test();
        Edit("services/billing.kscript", "* 2", "* 3");
        var (code, output) = Test();
        Assert.Equal(1, code);
        Assert.Equal(new[] { "bills" }, Ran(output));

        var (failedCode, failed) = Test("--failed");
        Assert.Equal(1, failedCode);
        Assert.Equal(new[] { "bills" }, Ran(failed));
        Assert.Contains("3 not run (passed last time)", failed);

        Edit("services/billing.kscript", "* 3", "* 2");
        Assert.Equal(new[] { "bills" }, Ran(Test().Out));
        Assert.Contains("No test failed in the last run", Test("--failed").Out);
        Assert.Contains("Use one of --all, --failed and --changed", Test("--all", "--failed").Out);
    }

    [Fact]
    public void A_test_whose_own_block_or_file_changed_runs()
    {
        Test();
        Edit("tests/math.test.kscript", "\"adds\"", "\"adds up\"");
        Assert.Equal(new[] { "adds up" }, Ran(Test().Out));

        // The file's top-level code (an import here) is shared by its tests, so they all run.
        Edit("tests/math.test.kscript", "test(", "import KScript.Math\n\ntest(");
        Assert.Equal(new[] { "adds up" }, Ran(Test().Out));
        Edit("tests/greeter.test.kscript", "import \"../types/order.kscript\"\n", "import \"../types/order.kscript\"\nlimit = 3\n");
        Assert.Equal(new[] { "greets", "waves", "bills" }, Ran(Test().Out));

        // A new test runs on its own.
        File.AppendAllText(Path.Combine(site.Root, "tests/math.test.kscript"), "\ntest(name: \"subtracts\") {\n  expect(2 - 1, equals: 1)\n}\n");
        Assert.Equal(new[] { "subtracts" }, Ran(Test().Out));

        // A new test file runs.
        site.Write("tests/more.test.kscript", "test(name: \"more\") {\n  expect(true, equals: true)\n}\n");
        Assert.Equal(new[] { "more" }, Ran(Test().Out));
    }

    [Fact]
    public void Settings_new_scripts_and_named_files_run_everything()
    {
        Test();
        site.Write("services/extra.kscript", "fn extra() => 1\n");
        var (_, output) = Test();
        Assert.Contains("Running every test: services/extra.kscript is new.", output);
        Assert.Equal(4, Ran(output).Count);

        site.Write("kscript.json", "{ \"name\": \"demo\", \"description\": \"changed\" }");
        Assert.Contains("Running every test: kscript.json changed.", Test().Out);

        site.Write(".env", "APP_NAME=demo\n");
        Assert.Contains("Running every test: .env changed.", Test().Out);

        Assert.Equal(new[] { "adds" }, Ran(Test(Path.Combine(site.Root, "tests", "math.test.kscript")).Out));
        Assert.Equal(new[] { "greets" }, Ran(Test("--filter", "greets").Out));

        // In CI every test runs.
        Environment.SetEnvironmentVariable("CI", "true");
        Assert.Equal(4, Ran(Test().Out).Count);
        Assert.True(TestSelection.InCi(_ => "1"));
        Assert.False(TestSelection.InCi(_ => "false"));
        Assert.False(TestSelection.InCi(_ => null));
    }

    [Fact]
    public void A_test_left_out_by_a_filter_still_runs_once_what_it_reached_changes()
    {
        Test();
        Edit("services/greeter.kscript", "\"Wave \"", "\"Wave  \"");
        Assert.Equal(new[] { "greets" }, Ran(Test("--filter", "greets").Out));
        var (code, output) = Test();
        Assert.Equal(1, code);
        Assert.Equal(new[] { "waves" }, Ran(output));
    }

    [Fact]
    public void A_file_a_test_read_reruns_it_when_it_changes()
    {
        site.Write("data/names.txt", "Ann");
        site.Write("tests/data.test.kscript", "import KScript.IO\n\ntest(name: \"reads\") {\n  expect(@read_text('../data/names.txt'), equals: 'Ann')\n}\n");
        Test();
        site.Write("data/names.txt", "Bob");
        var (code, output) = Test();
        Assert.Equal(1, code);
        Assert.Equal(new[] { "reads" }, Ran(output));
    }

    [Fact]
    public void A_file_that_could_not_load_runs_again()
    {
        site.Write("tests/broken.test.kscript", "test(name: \"x\") {\n");
        Assert.Equal(1, Test().Code);
        var (code, output) = Test();
        Assert.Equal(1, code);
        Assert.Contains("broken.test.kscript", output);
        site.Write("tests/broken.test.kscript", "test(name: \"x\") {\n  expect(1, equals: 1)\n}\n");
        Assert.Equal(new[] { "x" }, Ran(Test().Out));
    }

    [Fact]
    public void Skipped_tests_are_looked_at_again_when_their_file_runs()
    {
        site.Write("tests/only.test.kscript", "test(name: \"one\", only: true) {\n  expect(1, equals: 1)\n}\n\ntest(name: \"two\") {\n  expect(2, equals: 2)\n}\n");
        Test();
        Edit("tests/only.test.kscript", ", only: true", string.Empty);
        Assert.Contains("two", Ran(Test().Out));
    }

    [Fact]
    public void A_graph_from_another_build_or_a_damaged_one_is_not_used()
    {
        Test();
        string path = TestGraph.PathFor(site.Root);
        File.WriteAllText(path, File.ReadAllText(path).Replace(TestGraph.CurrentBuild, "0.0.1"));
        Assert.Contains("Running every test: kscript changed since the last run.", Test().Out);

        File.WriteAllText(path, "{ not json");
        Assert.Null(TestGraph.Load(site.Root));
        Assert.Equal(4, Ran(Test().Out).Count);
        Assert.Null(TestGraph.Parse("{ \"version\": 99 }"));
    }

    private void Git(params string[] args)
    {
        var start = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = site.Root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string a in new[] { "-c", "user.email=test@example.com", "-c", "user.name=test", "-c", "commit.gpgsign=false" }.Concat(args)) start.ArgumentList.Add(a);
        using var git = System.Diagnostics.Process.Start(start)!;
        git.StandardOutput.ReadToEnd();
        string error = git.StandardError.ReadToEnd();
        git.WaitForExit();
        Assert.True(git.ExitCode == 0, error);
    }

    [Fact]
    public void Changed_runs_the_tests_the_git_diff_can_affect()
    {
        Git("init", "-q");
        Git("add", "-A");
        Git("commit", "-q", "-m", "start");
        Test();

        var (none, output) = Test("--changed");
        Assert.Equal(0, none);
        Assert.Contains("git shows no changes", output);

        // Uncommitted: the diff against HEAD, whatever the last run was.
        Edit("types/user.kscript", "$name->upper()", "$name->upper() + ''");
        Test();
        Assert.Equal(new[] { "waves" }, Ran(Test("--changed").Out));
        Assert.Contains("3 not affected by the changes", Test("--changed").Out);

        Git("commit", "-q", "-am", "shout");
        Assert.Contains("git shows no changes", Test("--changed").Out);
        Assert.Equal(new[] { "waves" }, Ran(Test("--changed", "HEAD~1").Out));

        // A test file the diff changes runs its new and changed tests; a new file runs.
        Edit("tests/greeter.test.kscript", "'Hello Ann'", "'Hello' + ' Ann'");
        site.Write("tests/more.test.kscript", "test(name: \"more\") {\n  expect(true, equals: true)\n}\n");
        Assert.Equal(new[] { "greets", "more" }, Ran(Test("--changed").Out));

        Edit("services/billing.kscript", "* 2", "* 2 + 0");
        site.Write("services/unused.kscript", "fn unused() => 1\n");
        Assert.Contains("No test reaches what changed", Test("--changed", "HEAD", Path.Combine(site.Root, "tests", "math.test.kscript")).Out);

        site.Write("kscript.json", "{ \"name\": \"other\" }");
        Assert.Contains("Running every test: kscript.json changed.", Test("--changed").Out);

        var (bad, error) = Test("--changed", "no-such-branch");
        Assert.Equal(2, bad);
        Assert.Contains("git doesn't know no-such-branch", error);
        Assert.Contains("Use one of --all, --failed and --changed", Test("--changed", "--all").Out);
    }

    [Fact]
    public async Task Watch_runs_again_on_a_save_and_only_the_tests_it_can_affect()
    {
        Test();
        TestCommand.WatchQuietMilliseconds = 50;
        var output = new StringWriter { NewLine = "\n" };
        var shared = TextWriter.Synchronized(output);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        Task<int> watching = Task.Run(() => TestCommand.Run(new[] { "--watch" }, shared, shared, stop.Token, false, site.Root));
        try
        {
            await Until(() => output.ToString().Contains("Watching for changes"));
            Edit("services/billing.kscript", "* 2", "* 2 + 0");
            await Until(() => output.ToString().Contains("✓ bills"));
            Assert.DoesNotContain("✓ greets", output.ToString());
            Assert.Contains("Changed: services/billing.kscript", output.ToString());

            // A change no test reaches runs nothing.
            site.Write("notes.txt", "hello");
            await Until(() => output.ToString().Contains("Nothing the tests reach changed"));
        }
        finally
        {
            stop.Cancel();
            TestCommand.WatchQuietMilliseconds = 300;
        }
        Assert.Equal(0, await watching);

        static async Task Until(Func<bool> done)
        {
            for (int i = 0; i < 400 && !done(); i++) await Task.Delay(50);
            Assert.True(done());
        }
    }

    [Fact]
    public void Fingerprints_split_a_script_into_its_declarations_methods_tests_and_the_rest()
    {
        const string text = "import \"x.kscript\"\n\nlimit = 3\n\n/// A user.\ntype User {\n  name: string\n\n  fn shout() => $name->upper()\n}\n\nfn greet() => 'hi'\n\ntest(name: \"t\") {\n  expect(1, equals: 1)\n}\n";
        SourceFingerprint print = SourceFingerprint.Of("a.test.kscript", text);
        Assert.Equal(new[] { "fn:greet", "type:User", "type:User/fn:shout" }, print.Declarations.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Single(print.Tests);

        SourceFingerprint method = SourceFingerprint.Of("a.test.kscript", text.Replace("upper()", "lower()"));
        Assert.Equal(print.RestHash, method.RestHash);
        Assert.Equal(print.Of("type:User"), method.Of("type:User"));
        Assert.NotEqual(print.Of("type:User/fn:shout"), method.Of("type:User/fn:shout"));

        SourceFingerprint rest = SourceFingerprint.Of("a.test.kscript", text.Replace("limit = 3", "limit = 4"));
        Assert.NotEqual(print.RestHash, rest.RestHash);
        Assert.Equal(print.Of("fn:greet"), rest.Of("fn:greet"));

        SourceFingerprint test = SourceFingerprint.Of("a.test.kscript", text.Replace("equals: 1", "equals: 2"));
        Assert.Equal(print.RestHash, test.RestHash);
        Assert.NotEqual(print.Tests[0], test.Tests[0]);

        // A file that isn't a script, or doesn't parse, is one part.
        Assert.Empty(SourceFingerprint.Of("page.html", "<p>hi</p>").Declarations);
        Assert.Empty(SourceFingerprint.Of("bad.kscript", "fn (").Declarations);
        Assert.Null(SourceFingerprint.ForFile(Path.Combine(site.Root, "missing.kscript")));
        Assert.Null(print.Of("fn:missing"));
        Assert.True(SourceFingerprint.IsScript("a.ks.xml"));
    }
}
