using System.Diagnostics;
using System.Globalization;
using System.Xml.Linq;

using KScript.Projects;

namespace KScript.Testing
{
    /// <summary>
    /// <c>kscript test [file-or-folder]... [--filter text] [--timeout seconds] [--junit file]</c>: runs test files and
    /// prints each result. With no paths it runs the tests of the current project (the folder of the nearest
    /// kscript.json), or of the current folder. The exit code is 0 when every test passed and 1 otherwise.
    /// </summary>
    public static class TestCommand
    {
        public const string Usage = "kscript test [file-or-folder]... [--filter <text>] [--timeout <seconds>] [--junit <file>]";

        public static int Run(string[] args, TextWriter output, TextWriter error, CancellationToken cancellation = default, bool? color = null, string workingDirectory = null)
        {
            string cwd = workingDirectory ?? Directory.GetCurrentDirectory();
            // test.env in kscript.json (such as "test") is the environment the tests run in, unless one is already set.
            string env = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KSCRIPT_ENV")) ? TestEnvironment(args, cwd) : null;
            if (env == null) return RunTests(args, output, error, cancellation, color, cwd);
            Environment.SetEnvironmentVariable("KSCRIPT_ENV", env);
            try
            {
                return RunTests(args, output, error, cancellation, color, cwd);
            }
            finally
            {
                Environment.SetEnvironmentVariable("KSCRIPT_ENV", null);
            }
        }

        /// <summary>test.env of the project around the first path given (or the current folder).</summary>
        private static string TestEnvironment(string[] args, string cwd)
        {
            string first = args.FirstOrDefault(a => !a.StartsWith('-'));
            string start = first == null ? cwd : Path.GetFullPath(Path.Combine(cwd, first));
            if (File.Exists(start)) start = Path.GetDirectoryName(start);
            try
            {
                return Directory.Exists(start) ? ProjectConfig.FindAndLoad(start)?.Test.Env : null;
            }
            catch (ProjectConfigException)
            {
                return null;
            }
        }

        private static int RunTests(string[] args, TextWriter output, TextWriter error, CancellationToken cancellation, bool? color, string cwd)
        {
            var paths = new List<string>();
            var settings = new TestRunSettings { Cancellation = cancellation, BaseDirectory = cwd };
            string junit = null;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                string Value()
                {
                    if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal)) return null;
                    return args[++i];
                }

                switch (arg)
                {
                    case "--filter":
                    case "-f":
                        settings.Filter = Value();
                        if (settings.Filter == null) return Fail(error, "--filter needs some text, e.g. --filter \"slugify\"");
                        break;
                    case "--timeout":
                        string t = Value();
                        if (t == null || !double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) || seconds < 0)
                            return Fail(error, "--timeout needs a number of seconds, e.g. --timeout 30 (0 for no limit)");
                        settings.Timeout = seconds;
                        break;
                    case "--junit":
                        junit = Value();
                        if (junit == null) return Fail(error, "--junit needs a file to write, e.g. --junit test-results.xml");
                        break;
                    case "--help":
                    case "-h":
                        output.WriteLine("Usage: " + Usage);
                        output.WriteLine("Runs *.test.kscript and *.test.ks.xml files (or the files test.include names in kscript.json). With no paths, runs the current project's tests.");
                        return 0;
                    default:
                        if (arg.StartsWith("--", StringComparison.Ordinal)) return Fail(error, $"Unknown option '{arg}'. Usage: {Usage}");
                        paths.Add(arg);
                        break;
                }
            }

            if (paths.Count == 0)
            {
                ProjectConfig project = null;
                try
                {
                    project = ProjectConfig.FindAndLoad(cwd);
                }
                catch (ProjectConfigException ex)
                {
                    return Fail(error, ex.Message);
                }
                paths.Add(project?.Directory ?? cwd);
            }

            var missing = new List<string>();
            List<string> files = TestRunner.Discover(paths, missing);
            foreach (string m in missing) error.WriteLine($"There is no file or folder at '{m}'.");
            if (missing.Count > 0) return 1;
            if (files.Count == 0)
            {
                error.WriteLine($"No test files found in {string.Join(", ", paths)}. Test files are named like greet.test.kscript (or greet.test.ks.xml); create one with kscript make test greet.");
                return 1;
            }

            var style = new Style(color ?? UseColor());
            var clock = Stopwatch.StartNew();
            var results = new List<TestFileResult>();
            foreach (string file in files)
            {
                bool headed = false;
                string display = Path.GetRelativePath(settings.BaseDirectory, file).Replace('\\', '/');
                void Head()
                {
                    if (headed) return;
                    headed = true;
                    output.WriteLine(style.Bold(display.StartsWith("..", StringComparison.Ordinal) ? file : display));
                }

                TestFileResult result;
                try
                {
                    result = TestRunner.RunFile(file, settings, r =>
                    {
                        Head();
                        PrintResult(output, style, r);
                    });
                }
                catch (OperationCanceledException)
                {
                    error.WriteLine("Stopped.");
                    return 130;
                }
                results.Add(result);

                if (result.Error != null)
                {
                    Head();
                    output.WriteLine($"  {style.Red("✗")} {style.Red("Could not run the file:")} {Indent(result.Error, 4).TrimStart()}");
                    if (!string.IsNullOrEmpty(result.Output)) output.WriteLine(style.Dim($"    Output:\n{Indent(result.Output, 6)}"));
                }
                else if (result.Tests.Count == 0)
                {
                    // With --filter, files without a match are left out of the output.
                    if (!string.IsNullOrEmpty(settings.Filter)) continue;
                    Head();
                    output.WriteLine(style.Dim("  (no tests in this file)"));
                }
                output.WriteLine();
            }
            clock.Stop();

            List<TestResult> all = results.SelectMany(r => r.Tests).ToList();
            int passed = all.Count(t => t.Outcome == TestOutcome.Passed);
            int failed = all.Count(t => t.Outcome == TestOutcome.Failed);
            int skipped = all.Count(t => t.Outcome == TestOutcome.Skipped);
            int brokenFiles = results.Count(r => r.Error != null);

            var parts = new List<string>();
            if (failed > 0) parts.Add(style.Red($"{failed} failed"));
            if (passed > 0) parts.Add(style.Green($"{passed} passed"));
            if (skipped > 0) parts.Add(style.Yellow($"{skipped} skipped"));
            parts.Add($"{all.Count} total");
            output.WriteLine($"Tests: {string.Join(", ", parts)}");
            int failedFiles = results.Count(r => r.Failed);
            output.WriteLine($"Files: {(failedFiles > 0 ? style.Red($"{failedFiles} failed") + ", " : string.Empty)}{results.Count} total{(brokenFiles > 0 ? $" ({brokenFiles} could not run)" : string.Empty)}");
            output.WriteLine($"Time:  {clock.Elapsed.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture)}s");

            if (junit != null)
            {
                try
                {
                    WriteJUnit(junit, results, clock.Elapsed);
                    output.WriteLine($"Wrote {junit}");
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    error.WriteLine($"Could not write {junit}: {ex.Message}");
                    return 1;
                }
            }

            if (all.Count == 0 && brokenFiles == 0)
            {
                error.WriteLine(string.IsNullOrEmpty(settings.Filter) ? "No tests found." : $"No tests match '{settings.Filter}'.");
                return 1;
            }
            return failed == 0 && brokenFiles == 0 ? 0 : 1;
        }

        private static void PrintResult(TextWriter output, Style style, TestResult r)
        {
            string time = style.Dim($" ({FormatDuration(r.Duration)})");
            switch (r.Outcome)
            {
                case TestOutcome.Passed:
                    output.WriteLine($"  {style.Green("✓")} {r.FullName}{time}");
                    break;
                case TestOutcome.Skipped:
                    output.WriteLine($"  {style.Yellow("○")} {r.FullName} {style.Dim(string.IsNullOrEmpty(r.Message) ? "(skipped)" : $"(skipped: {r.Message})")}");
                    break;
                default:
                    output.WriteLine($"  {style.Red("✗")} {r.FullName}{time}");
                    output.WriteLine(style.Red(Indent(r.Message, 6)));
                    if (r.Location != null) output.WriteLine(style.Dim($"      at {r.Location}"));
                    if (!string.IsNullOrEmpty(r.Output)) output.WriteLine(style.Dim($"      Output:\n{Indent(r.Output, 8)}"));
                    break;
            }
        }

        private static string FormatDuration(TimeSpan t) =>
            t.TotalMilliseconds < 1000 ? $"{Math.Max(0, (int)t.TotalMilliseconds)} ms" : $"{t.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture)} s";

        private static string Indent(string text, int spaces)
        {
            string pad = new string(' ', spaces);
            return string.Join("\n", (text ?? string.Empty).Replace("\r\n", "\n").Split('\n').Select(l => pad + l));
        }

        private static int Fail(TextWriter error, string message)
        {
            error.WriteLine(message);
            return 1;
        }

        private static bool UseColor() =>
            Environment.GetEnvironmentVariable("FORCE_COLOR") != null
            || (!Console.IsOutputRedirected && Environment.GetEnvironmentVariable("NO_COLOR") == null);

        /// <summary>Writes the results as JUnit XML, which most CI systems (GitHub Actions, GitLab, Jenkins, Azure) can show.</summary>
        public static void WriteJUnit(string path, IReadOnlyList<TestFileResult> results, TimeSpan total)
        {
            static string Seconds(TimeSpan t) => t.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture);

            var suites = new XElement("testsuites",
                new XAttribute("name", "kscript test"),
                new XAttribute("tests", results.Sum(r => r.Tests.Count + (r.Error != null ? 1 : 0))),
                new XAttribute("failures", results.Sum(r => r.Tests.Count(t => t.Outcome == TestOutcome.Failed && t.ErrorType == TestPackage.AssertionType))),
                new XAttribute("errors", results.Sum(r => r.Tests.Count(t => t.Outcome == TestOutcome.Failed && t.ErrorType != TestPackage.AssertionType) + (r.Error != null ? 1 : 0))),
                new XAttribute("skipped", results.Sum(r => r.Tests.Count(t => t.Outcome == TestOutcome.Skipped))),
                new XAttribute("time", Seconds(total)));

            foreach (TestFileResult file in results)
            {
                var suite = new XElement("testsuite",
                    new XAttribute("name", file.DisplayPath),
                    new XAttribute("file", file.DisplayPath),
                    new XAttribute("tests", file.Tests.Count + (file.Error != null ? 1 : 0)),
                    new XAttribute("failures", file.Tests.Count(t => t.Outcome == TestOutcome.Failed && t.ErrorType == TestPackage.AssertionType)),
                    new XAttribute("errors", file.Tests.Count(t => t.Outcome == TestOutcome.Failed && t.ErrorType != TestPackage.AssertionType) + (file.Error != null ? 1 : 0)),
                    new XAttribute("skipped", file.Tests.Count(t => t.Outcome == TestOutcome.Skipped)),
                    new XAttribute("time", Seconds(file.Duration)));

                if (file.Error != null)
                {
                    suite.Add(new XElement("testcase", new XAttribute("name", "(load)"), new XAttribute("classname", file.DisplayPath), new XAttribute("time", "0"),
                        new XElement("error", new XAttribute("message", FirstLine(file.Error)), file.Error)));
                }

                foreach (TestResult t in file.Tests)
                {
                    var testcase = new XElement("testcase",
                        new XAttribute("name", t.FullName),
                        new XAttribute("classname", file.DisplayPath),
                        new XAttribute("file", file.DisplayPath),
                        new XAttribute("line", t.Line),
                        new XAttribute("time", Seconds(t.Duration)));
                    if (t.Outcome == TestOutcome.Skipped)
                    {
                        testcase.Add(new XElement("skipped", string.IsNullOrEmpty(t.Message) ? null : new XAttribute("message", t.Message)));
                    }
                    else if (t.Outcome == TestOutcome.Failed)
                    {
                        string details = t.Location != null ? $"{t.Message}\nat {t.Location}" : t.Message;
                        testcase.Add(new XElement(t.ErrorType == TestPackage.AssertionType ? "failure" : "error",
                            new XAttribute("message", FirstLine(t.Message)), new XAttribute("type", t.ErrorType ?? "Error"), details));
                    }
                    if (!string.IsNullOrEmpty(t.Output)) testcase.Add(new XElement("system-out", t.Output));
                    suite.Add(testcase);
                }
                suites.Add(suite);
            }

            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            new XDocument(new XDeclaration("1.0", "utf-8", null), suites).Save(path);
        }

        private static string FirstLine(string text) => (text ?? string.Empty).Replace("\r\n", "\n").Split('\n')[0];

        private sealed class Style
        {
            private readonly bool on;

            public Style(bool on) => this.on = on;

            private string Wrap(string code, string text) => on ? $"\u001b[{code}m{text}\u001b[0m" : text;

            public string Bold(string t) => Wrap("1", t);
            public string Dim(string t) => Wrap("2", t);
            public string Red(string t) => Wrap("31", t);
            public string Green(string t) => Wrap("32", t);
            public string Yellow(string t) => Wrap("33", t);
        }
    }
}
