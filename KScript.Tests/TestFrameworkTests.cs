using System.Xml.Linq;

using KScript.Runtime;
using KScript.Scaffolding;
using KScript.Testing;

namespace KScript.Tests;

/// <summary>KScript.Test: test files, the checks, and kscript test.</summary>
public class TestFrameworkTests
{
    /// <summary>Writes a test file (the body goes inside &lt;kscript&gt;) and runs it.</summary>
    private static TestFileResult RunTests(string body, TestRunSettings settings = null, params (string Path, string Content)[] files)
    {
        using var site = new TempSite(files);
        site.Write("example.test.ks.xml", "<kscript>\n" + body + "\n</kscript>");
        return TestRunner.RunFile(Path.Combine(site.Root, "example.test.ks.xml"), settings ?? new TestRunSettings { BaseDirectory = site.Root });
    }

    private static TestResult Only(string body)
    {
        TestFileResult file = RunTests(body);
        Assert.Null(file.Error);
        return Assert.Single(file.Tests);
    }

    private static string Failure(string body)
    {
        TestResult r = Only($"<test name=\"t\">{body}</test>");
        Assert.Equal(TestOutcome.Failed, r.Outcome);
        return r.Message;
    }

    private static void Passes(string body)
    {
        TestResult r = Only($"<test name=\"t\">{body}</test>");
        Assert.True(r.Outcome == TestOutcome.Passed, r.Message);
    }

    [Fact]
    public void TestsPassAndFailIndependently()
    {
        TestFileResult file = RunTests("""
            <let name="shared" value="1" />
            <test name="passes"><expect value="$shared" equals="1" /></test>
            <test name="fails"><expect value="$shared" equals="2" /></test>
            <test name="still runs"><assert test="$shared eq 1" /></test>
            """);
        Assert.Null(file.Error);
        Assert.Equal(new[] { TestOutcome.Passed, TestOutcome.Failed, TestOutcome.Passed }, file.Tests.Select(t => t.Outcome));
        TestResult failed = file.Tests[1];
        Assert.Equal("Expected $shared to equal 2, but it was 1.", failed.Message);
        Assert.Equal("Assertion", failed.ErrorType);
        Assert.Equal("example.test.ks.xml:4:28", failed.Location);
    }

    [Fact]
    public void DescribeNamesNestAndHooksRunAroundEachTest()
    {
        TestFileResult file = RunTests("""
            <let name="log" value="[]" />
            <before_each><set name="log" value="$log->push('outer before')" /></before_each>
            <after_each><set name="log" value="$log->push('outer after')" /></after_each>
            <describe name="cart">
              <before_each><set name="log" value="$log->push('inner before')" /></before_each>
              <after_each><set name="log" value="$log->push('inner after')" /></after_each>
              <describe name="when empty">
                <test name="has nothing"><set name="log" value="$log->push('test')" /></test>
              </describe>
            </describe>
            <test name="sees the order">
              <expect value="$log" equals="['outer before', 'inner before', 'test', 'inner after', 'outer after', 'outer before']" />
            </test>
            """);
        Assert.Equal(new[] { "cart › when empty › has nothing", "sees the order" }, file.Tests.Select(t => t.FullName));
        Assert.All(file.Tests, t => Assert.True(t.Outcome == TestOutcome.Passed, t.Message));
    }

    [Fact]
    public void VariablesDeclaredInATestStayInThatTest()
    {
        TestFileResult file = RunTests("""
            <before_each><let name="fresh" value="[]" /></before_each>
            <test name="one"><let name="mine" value="1" /><expect value="$fresh" count="0" /></test>
            <test name="two"><expect value="$mine ?? 'gone'" equals="'gone'" /></test>
            """);
        Assert.All(file.Tests, t => Assert.True(t.Outcome == TestOutcome.Passed, t.Message));
    }

    [Fact]
    public void ATestInAForLoopRunsPerItemWithItsOwnName()
    {
        TestFileResult file = RunTests("""
            <fn name="double" params="n" value="$n * 2" />
            <for each="case" at="i" in="[{ n: 1, want: 2 }, { n: 2, want: 4 }, { n: 3, want: 7 }]">
              <test name="doubles {$case.n} (#{$i})">
                <expect value="@double($case.n)" equals="$case.want" />
              </test>
            </for>
            <for each="entry" in="{ a: 1 }"><test name="{$entry.key}"><expect value="$entry.value" equals="1" /></test></for>
            """);
        Assert.Equal(new[] { "doubles 1 (#0)", "doubles 2 (#1)", "doubles 3 (#2)", "a" }, file.Tests.Select(t => t.Name));
        Assert.Equal(new[] { TestOutcome.Passed, TestOutcome.Passed, TestOutcome.Failed, TestOutcome.Passed }, file.Tests.Select(t => t.Outcome));
    }

    [Fact]
    public void SkipAndOnly()
    {
        TestFileResult file = RunTests("""
            <test name="a" skip="needs a network"><do>@fail()</do></test>
            <describe name="group" skip="yes"><test name="b"><do>@fail()</do></test></describe>
            <test name="c" skip="no"><assert test="true" /></test>
            """);
        Assert.Equal(new[] { TestOutcome.Skipped, TestOutcome.Skipped, TestOutcome.Passed }, file.Tests.Select(t => t.Outcome));
        Assert.Equal("needs a network", file.Tests[0].Message);

        file = RunTests("""
            <test name="a"><do>@fail()</do></test>
            <describe name="focus" only="yes"><test name="b"><assert test="true" /></test></describe>
            <test name="c" only="yes"><assert test="true" /></test>
            """);
        Assert.Equal(new[] { TestOutcome.Skipped, TestOutcome.Passed, TestOutcome.Passed }, file.Tests.Select(t => t.Outcome));
    }

    [Fact]
    public void FilterMatchesTheFullName()
    {
        TestFileResult file = RunTests("""
            <describe name="Cart"><test name="adds"><assert test="true" /></test></describe>
            <test name="other"><assert test="true" /></test>
            """, new TestRunSettings { Filter = "cart › ADD" });
        Assert.Equal("Cart › adds", Assert.Single(file.Tests).FullName);
    }

    [Fact]
    public void AssertShowsBothSidesOfAComparison()
    {
        Assert.Equal("Expected $total eq 30 to be true.\n  $total is 25", Failure("<let name=\"total\" value=\"25\" /><assert test=\"$total eq 30\" />"));
        Assert.Equal("Expected $a->count() gt $b to be true.\n  $a->count() is 1\n  $b is 2",
            Failure("<let name=\"a\" value=\"['x']\" /><let name=\"b\" value=\"2\" /><assert test=\"$a->count() gt $b\" />"));
        Assert.Equal("Expected $name->starts_with('J') to be true.\n  $name->starts_with('J') is false",
            Failure("<let name=\"name\" value=\"'Ellie'\" /><assert test=\"$name->starts_with('J')\" />"));
        Assert.Equal("The cart should have 2 items", Failure("<let name=\"n\" value=\"1\" /><assert test=\"$n eq 2\" message=\"The cart should have { $n + 1 } items\" />"));
    }

    [Fact]
    public void ExpectChecks()
    {
        Passes("""
            <expect value="'3'" equals="3" />
            <expect value="[1, { a: 2 }]" equals="[1, { a: 2 }]" />
            <expect value="1" not_equals="2" />
            <expect value="3" same="3" />
            <expect value="['a', 'b']" contains="'b'" />
            <expect value="'hello world'" contains="'lo w'" />
            <expect value="{ name: 'Joan' }" contains="'name'" />
            <expect value="['a']" not_contains="'z'" />
            <expect value="'abc-123'" matches="'^[a-z]+-\d+$'" />
            <expect value="[1, 2]" type="list" />
            <expect value="null" type="null" />
            <expect value="'four'" count="4" />
            <expect value="{ a: 1, b: 2 }" count="2" />
            """);

        Assert.Equal("Expected '3' to be 3 (number), but it was '3' (string).", Failure("<expect value=\"'3'\" same=\"3\" />"));
        Assert.Equal("Expected $p to equal { name: 'Joan', age: 82 }, but it was { name: 'Joan', age: 81 }.\n  'age' is 81, not 82",
            Failure("<let name=\"p\" value=\"{ name: 'Joan', age: 81 }\" /><expect value=\"$p\" equals=\"{ name: 'Joan', age: 82 }\" />"));
        Assert.Equal("Expected [1, 2] to equal [1, 2, 3], but it was [1, 2].\n  It is missing item 2: 3", Failure("<expect value=\"[1, 2]\" equals=\"[1, 2, 3]\" />"));
        Assert.Equal("Expected ['a'] to contain 'b', but it was ['a'].", Failure("<expect value=\"['a']\" contains=\"'b'\" />"));
        Assert.Equal("Expected 'x1' to match /^\\d+$/, but it was 'x1'.", Failure("<expect value=\"'x1'\" matches=\"'^\\d+$'\" />"));
        Assert.Equal("Expected 5 to be a string, but it was 5, a number.", Failure("<expect value=\"5\" type=\"string\" />"));
        Assert.Equal("Expected [1] to have 2 items, but it has 1: [1].", Failure("<expect value=\"[1]\" count=\"2\" />"));
        Assert.Equal("Nope", Failure("<expect value=\"1\" equals=\"2\" message=\"Nope\" />"));
    }

    [Fact]
    public void ExpectNeedsExactlyOneCheck()
    {
        Assert.Contains("<expect> needs one check", Failure("<expect value=\"1\" />"));
        Assert.Contains("<expect> takes one check, but has equals and contains", Failure("<expect value=\"1\" equals=\"1\" contains=\"1\" />"));
    }

    [Fact]
    public void ExpectError()
    {
        Passes("""
            <expect_error type="Validation" contains="positive" as="err">
              <throw type="Validation" message="Age must be positive" />
            </expect_error>
            <expect value="$err.message" equals="'Age must be positive'" />
            <expect_error><echo>{ 1 / 'x' }</echo></expect_error>
            """);
        Assert.Equal("Expected an error, but the body ran without one.", Failure("<expect_error><echo>fine</echo></expect_error>"));
        Assert.Equal("Expected a Validation error, but got Type: oops", Failure("<expect_error type=\"Validation\"><throw type=\"Type\" message=\"oops\" /></expect_error>"));
        Assert.Equal("Expected the error message to contain 'x', but it was: oops", Failure("<expect_error contains=\"x\"><throw message=\"oops\" /></expect_error>"));
        // A failed check inside the body is still a failure, not the expected error.
        Assert.Equal("Expected 1 to equal 2, but it was 1.", Failure("<expect_error><expect value=\"1\" equals=\"2\" /></expect_error>"));
    }

    [Fact]
    public void CaptureKeepsOutputAndGivesInput()
    {
        TestFileResult file = RunTests("""
            <import package="KScript.Terminal" />
            <fn name="ask">
              <input into="name">Name? </input>
              <echo>Hi {$name}</echo>
            </fn>
            <test name="captures">
              <capture into="out" input="['Joan']"><call fn="ask" /></capture>
              <expect value="$out" contains="'Hi Joan'" />
              <capture into="two"><echo>a</echo><echo>b</echo></capture>
              <expect value="$two->lines()" equals="['a', 'b']" />
            </test>
            """);
        Assert.True(file.Tests[0].Outcome == TestOutcome.Passed, file.Tests[0].Message);
        Assert.Equal(string.Empty, file.Tests[0].Output);
    }

    [Fact]
    public void FailingTestsShowWhatTheyPrinted()
    {
        TestResult r = Only("<test name=\"t\"><echo>step 1</echo><do>@fail('broke')</do></test>");
        Assert.Equal("broke", r.Message);
        Assert.Equal("step 1", r.Output);
    }

    [Fact]
    public void ErrorsInATestAreReportedWithTheirType()
    {
        TestResult r = Only("<test name=\"t\">\n<echo>{ $missing }</echo></test>");
        Assert.Equal(TestOutcome.Failed, r.Outcome);
        Assert.Equal("Name", r.ErrorType);
        Assert.StartsWith("Name: $missing is not defined", r.Message);
        Assert.Equal("example.test.ks.xml:3:9", r.Location);
    }

    [Fact]
    public void ExitInATestFails()
    {
        Assert.Contains("The test called <exit> (code 2)", Failure("<exit code=\"2\" />"));
    }

    [Fact]
    public void SlowTestsTimeOut()
    {
        TestFileResult file = RunTests("""
            <test name="forever" timeout="0.2"><while test="true"><do>@assert(true)</do></while></test>
            <test name="next one still runs"><assert test="true" /></test>
            """);
        Assert.Equal(TestOutcome.Failed, file.Tests[0].Outcome);
        Assert.StartsWith("Timed out: the test ran for more than 0.2 seconds.", file.Tests[0].Message);
        Assert.Equal(TestOutcome.Passed, file.Tests[1].Outcome);
    }

    [Fact]
    public void AFileThatDoesNotLoadIsReported()
    {
        TestFileResult file = RunTests("<test name=\"t\"><nope /></test>");
        Assert.Empty(file.Tests);
        Assert.Contains("Unknown element <nope>", file.Error);
        Assert.True(file.Failed);

        file = RunTests("<echo>before</echo><throw message=\"setup broke\" /><test name=\"t\"><assert test=\"true\" /></test>");
        Assert.Contains("Error: setup broke", file.Error);
        Assert.Equal("before", file.Output);
    }

    [Fact]
    public void TestElementsNeedKScriptTest()
    {
        var e = ScriptRunner.Fails("<test name=\"t\"><assert test=\"true\" /></test>", "KScript.Test");
        Assert.Contains("<test> only works in a test file run by kscript test", e.Message);

        TestResult nested = Only("<test name=\"outer\"><test name=\"inner\" /></test>");
        Assert.Contains("<test> can't be inside a test", nested.Message);
    }

    [Fact]
    public void ChecksWorkOutsideTestsToo()
    {
        // <expect> and <assert> stop any script with an Assertion error, so a script can check itself.
        var e = ScriptRunner.Fails("<expect value=\"1\" equals=\"2\" />", "KScript.Test");
        Assert.Equal("Assertion", e.Type);
        Assert.Equal("ok\n", ScriptRunner.RunBody("<capture into=\"out\"><echo>ok</echo></capture><echo>$out</echo>", "", "KScript.Test"));
    }

    [Fact]
    public void RunScriptRunsAScriptOnItsOwn()
    {
        TestFileResult file = RunTests("""
            <test name="greets">
              <let name="r" value="@run_script('greet.ks.xml', options: { args: ['Joan'], input: 'yes' })" />
              <expect value="$r.output->lines()->first()" equals="'Hello Joan'" />
              <expect value="$r.output" contains="'yes'" />
              <expect value="$r.code" equals="4" />
              <expect value="$r.error" same="null" />
            </test>
            <test name="errors">
              <let name="r" value="@run_script('broken.ks.xml')" />
              <expect value="$r.code" equals="3" />
              <expect value="$r.error.message" contains="'bad'" />
            </test>
            <test name="bad option">
              <expect_error type="Argument" contains="no option 'arg'"><do>@run_script('greet.ks.xml', options: { arg: [] })</do></expect_error>
            </test>
            """, null,
            ("greet.ks.xml", "<kscript><import package=\"KScript.Terminal\" /><echo>Hello {$args[0]}</echo><input into=\"a\">Sure? </input><echo newline=\"no\">{$a}</echo><exit code=\"4\" /></kscript>"),
            ("broken.ks.xml", "<kscript><throw message=\"bad\" /></kscript>"));
        Assert.All(file.Tests, t => Assert.True(t.Outcome == TestOutcome.Passed, t.FullName + ": " + t.Message));
    }

    [Fact]
    public void ProjectPackagesAndTimeoutApply()
    {
        using var site = new TempSite(
            ("kscript.json", """{ "packages": ["KScript.Math"], "test": { "packages": ["KScript.IO"], "timeout": 0.2 } }"""),
            ("tests/a.test.ks.xml", """
                <kscript>
                  <test name="uses packages"><expect value="@sqrt(9)" equals="3" /><expect value="@exists('nothing-here')" equals="false" /></test>
                  <test name="times out"><while test="true"><do>@sqrt(1)</do></while></test>
                </kscript>
                """));
        TestFileResult file = TestRunner.RunFile(Path.Combine(site.Root, "tests", "a.test.ks.xml"), new TestRunSettings { BaseDirectory = site.Root });
        Assert.True(file.Tests[0].Outcome == TestOutcome.Passed, file.Tests[0].Message);
        Assert.StartsWith("Timed out: the test ran for more than 0.2 seconds", file.Tests[1].Message);
    }

    [Fact]
    public void DiscoveryFollowsKScriptJson()
    {
        using var site = new TempSite(
            ("kscript.json", """{ "test": { "include": ["tests/**/*.test.ks.xml", "spec/*.ks.xml"], "exclude": ["tests/slow"] } }"""),
            ("tests/a.test.ks.xml", "<kscript />"),
            ("tests/deep/b.test.ks.xml", "<kscript />"),
            ("tests/slow/c.test.ks.xml", "<kscript />"),
            ("spec/d.ks.xml", "<kscript />"),
            ("lib/e.test.ks.xml", "<kscript />"),
            ("node_modules/f.test.ks.xml", "<kscript />"));
        IEnumerable<string> Rel(IEnumerable<string> files) => files.Select(f => Path.GetRelativePath(site.Root, f).Replace('\\', '/'));

        Assert.Equal(new[] { "spec/d.ks.xml", "tests/a.test.ks.xml", "tests/deep/b.test.ks.xml" }, Rel(TestRunner.Discover(new[] { site.Root })).OrderBy(x => x, StringComparer.Ordinal));
        // A file named on the command line always runs.
        Assert.Equal(new[] { "lib/e.test.ks.xml" }, Rel(TestRunner.Discover(new[] { Path.Combine(site.Root, "lib", "e.test.ks.xml") })));

        var missing = new List<string>();
        Assert.Empty(TestRunner.Discover(new[] { Path.Combine(site.Root, "nope") }, missing));
        Assert.Single(missing);
    }

    [Fact]
    public void DiscoveryWithoutAProjectUsesTheFileNames()
    {
        using var site = new TempSite(
            ("a.test.ks.xml", "<kscript />"),
            ("b.test.xml", "<kscript />"),
            ("c.ks.xml", "<kscript />"),
            (".hidden/d.test.ks.xml", "<kscript />"));
        Assert.Equal(new[] { "a.test.ks.xml", "b.test.xml" }, TestRunner.Discover(new[] { site.Root }).Select(Path.GetFileName));
    }

    private static (int Code, string Out, string Err) Command(params string[] args)
    {
        var o = new StringWriter { NewLine = "\n" };
        var e = new StringWriter { NewLine = "\n" };
        int code = TestCommand.Run(args, o, e, color: false);
        return (code, o.ToString(), e.ToString());
    }

    [Fact]
    public void CommandPrintsResultsAndSetsTheExitCode()
    {
        using var site = new TempSite(
            ("good.test.ks.xml", "<kscript><describe name=\"math\"><test name=\"adds\"><expect value=\"1 + 1\" equals=\"2\" /></test></describe><test name=\"later\" skip=\"yes\" /></kscript>"),
            ("bad.test.ks.xml", "<kscript><test name=\"breaks\"><echo>log line</echo><expect value=\"1\" equals=\"2\" /></test></kscript>"));

        var (code, output, _) = Command(Path.Combine(site.Root, "good.test.ks.xml"));
        Assert.Equal(0, code);
        Assert.Contains("  ✓ math › adds (", output);
        Assert.Contains("  ○ later (skipped)", output);
        Assert.Contains("Tests:    1 passed, 1 skipped, 2 total", output);

        (code, output, _) = Command(site.Root);
        Assert.Equal(1, code);
        Assert.Contains("  ✗ breaks (", output);
        Assert.Contains("      Expected 1 to equal 2, but it was 1.\n      at ", output);
        Assert.Contains("bad.test.ks.xml:1:", output);
        Assert.Contains("      Output:\n        log line", output);
        Assert.Contains("Tests:    1 failed, 1 passed, 1 skipped, 3 total", output);
        Assert.Contains("Files:    1 failed, 2 total", output);

        (code, _, string err) = Command(site.Root, "--filter", "nothing like this");
        Assert.Equal(1, code);
        Assert.Contains("No tests match 'nothing like this'.", err);

        (code, _, err) = Command(Path.Combine(site.Root, "missing"));
        Assert.Equal(1, code);
        Assert.Contains("There is no file or folder at", err);

        (code, _, err) = Command(site.Root, "--timeout", "soon");
        Assert.Equal(1, code);
        Assert.Contains("--timeout needs a number of seconds", err);
    }

    [Fact]
    public void CommandSaysWhenThereAreNoTests()
    {
        using var site = new TempSite(("script.ks.xml", "<kscript />"));
        var (code, _, err) = Command(site.Root);
        Assert.Equal(1, code);
        Assert.Contains("No test files found", err);
        Assert.Contains("kscript make test", err);
    }

    [Fact]
    public void JUnitReport()
    {
        using var site = new TempSite(
            ("a.test.ks.xml", "<kscript><test name=\"ok\"><assert test=\"true\" /></test><test name=\"no\"><assert test=\"false\" /></test><test name=\"err\"><echo>{ $x }</echo></test><test name=\"s\" skip=\"later\" /></kscript>"),
            ("b.test.ks.xml", "<kscript><bad /></kscript>"));
        string report = Path.Combine(site.Root, "out", "junit.xml");
        var (code, output, _) = Command(site.Root, "--junit", report);
        Assert.Equal(1, code);
        Assert.Contains("Wrote ", output);

        XElement suites = XDocument.Load(report).Root;
        Assert.Equal("5", suites.Attribute("tests").Value);
        Assert.Equal("1", suites.Attribute("failures").Value);
        Assert.Equal("2", suites.Attribute("errors").Value);
        Assert.Equal("1", suites.Attribute("skipped").Value);
        XElement a = suites.Elements("testsuite").First(s => s.Attribute("name").Value.EndsWith("a.test.ks.xml"));
        Assert.Equal("Expected false to be true.", a.Elements("testcase").First(t => t.Attribute("name").Value == "no").Element("failure").Attribute("message").Value);
        Assert.NotNull(a.Elements("testcase").First(t => t.Attribute("name").Value == "err").Element("error"));
        Assert.Equal("later", a.Elements("testcase").First(t => t.Attribute("name").Value == "s").Element("skipped").Attribute("message").Value);
    }

    [Fact]
    public void TheSampleTestsPass()
    {
        string samples = ScriptRunner.RepoFile("samples", "kscript2");
        List<string> files = TestRunner.Discover(new[] { samples });
        Assert.NotEmpty(files);
        foreach (string f in files)
        {
            TestFileResult r = TestRunner.RunFile(f, new TestRunSettings { BaseDirectory = samples });
            Assert.Null(r.Error);
            Assert.NotEmpty(r.Tests);
            Assert.All(r.Tests, t => Assert.True(t.Outcome == TestOutcome.Passed, $"{f}: {t.FullName}: {t.Message}"));
        }
    }

    [Fact]
    public void MakeTestWritesToTheProjectTestsFolder()
    {
        using var site = new TempSite(("kscript.json", """{ "serve": { "root": "site" } }"""), ("site/index.html", "<p>hi</p>"));
        var result = Scaffolder.Make(new MakeRequest { Type = "test", Name = "cart/Checkout", Root = Path.Combine(site.Root, "site") });
        Assert.Equal("tests/cart/checkout.test.kscript", result.RelativePath);
        Assert.Equal(Path.Combine(site.Root, "tests", "cart", "checkout.test.kscript"), result.FullPath);
        Assert.Contains("describe(name: \"Checkout\")", result.Content);

        TestFileResult run = TestRunner.RunFile(result.FullPath, new TestRunSettings());
        Assert.Null(run.Error);
        Assert.Equal(2, run.Tests.Count);
        Assert.All(run.Tests, t => Assert.Equal(TestOutcome.Passed, t.Outcome));

        // A project can ask for XML instead.
        File.WriteAllText(Path.Combine(site.Root, "kscript.json"), """{ "serve": { "root": "site" }, "make": { "format": "xml" } }""");
        var xml = Scaffolder.Make(new MakeRequest { Type = "test", Name = "cart/Checkout", Root = Path.Combine(site.Root, "site") });
        Assert.Equal("tests/cart/checkout.test.ks.xml", xml.RelativePath);
        Assert.Contains("<describe name=\"Checkout\">", xml.Content);
        run = TestRunner.RunFile(xml.FullPath, new TestRunSettings());
        Assert.Null(run.Error);
        Assert.Equal(2, run.Tests.Count);
        Assert.All(run.Tests, t => Assert.Equal(TestOutcome.Passed, t.Outcome));
    }

    [Fact]
    public void TheProjectStarterHasPassingTests()
    {
        string parent = Path.Combine(Path.GetTempPath(), "kscript-proj-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        try
        {
            var o = new StringWriter();
            Assert.Equal(0, MakeCommand.Run(new[] { "project", "demo", "--dir", parent }, o, new StringWriter()));
            string project = Path.Combine(parent, "demo");
            List<string> files = TestRunner.Discover(new[] { project });
            Assert.Equal(new[] { "greeting.test.kscript", "tasks.test.kscript" }, files.Select(Path.GetFileName).OrderBy(f => f, StringComparer.Ordinal));
            var results = files.Select(f => TestRunner.RunFile(f, new TestRunSettings())).ToList();
            Assert.All(results, r => Assert.Null(r.Error));
            Assert.Equal(14, results.Sum(r => r.Tests.Count));
            Assert.All(results.SelectMany(r => r.Tests), t => Assert.True(t.Outcome == TestOutcome.Passed, t.FullName + ": " + t.Message));
        }
        finally
        {
            try { Directory.Delete(parent, recursive: true); } catch (IOException) { }
        }
    }
}

public class HostCaptureTests
{
    [Fact]
    public void CapturesNestAndInputIsRestored()
    {
        var outer = new StringWriter();
        var host = new KScriptHost(outer, input: new StringReader("from stdin\n"));
        var a = new StringWriter();
        var b = new StringWriter();
        using (host.CaptureOutput(a))
        {
            host.WriteLine("one");
            using (host.CaptureOutput(b)) host.Write("two");
            using (host.ProvideInput(new StringReader("given\n"))) Assert.Equal("given", host.ReadLine());
        }
        host.WriteLine("three");
        Assert.Equal("from stdin", host.ReadLine());
        Assert.Equal("one" + Environment.NewLine, a.ToString());
        Assert.Equal("two", b.ToString());
        Assert.Equal("three" + Environment.NewLine, outer.ToString());
    }
}
