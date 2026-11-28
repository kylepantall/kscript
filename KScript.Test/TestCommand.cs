using System.Diagnostics;
using System.Globalization;
using System.Xml.Linq;
using KScript.Output;

using KScript.Projects;

namespace KScript.Testing
{
    /// <summary>
    /// <c>kscript test [file-or-folder]... [--all | --failed] [--filter text] [--timeout seconds] [--junit file]</c>: runs
    /// test files and prints each result. With no paths it runs the tests of the current project (the folder of the
    /// nearest kscript.json), or of the current folder. In a project it runs only the tests that a change since the last
    /// run can affect (see <see cref="TestSelection"/>), unless --all is given, files or --filter are named, or CI is
    /// set. The exit code is 0 when every test that ran passed and 1 otherwise.
    /// </summary>
    public static class TestCommand
    {
        public const string Usage = "kscript test [file-or-folder]... [--all | --failed] [--filter <text>] [--timeout <seconds>] [--junit <file>]";

        public static int Run(string[] args, TextWriter output, TextWriter error, CancellationToken cancellation = default, bool? color = null, string workingDirectory = null)
        {
            string cwd = workingDirectory ?? KScript.Runtime.WorkingFolder.Current;
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
            // The first path, not the text after --filter, --timeout or --junit (--all and --failed take nothing).
            string first = null;
            for (int i = 0; i < args.Length && first == null; i++)
            {
                if (args[i] is "--filter" or "-f" or "--timeout" or "--junit") i++;
                else if (!args[i].StartsWith('-')) first = args[i];
            }
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
            bool runAll = false, failedOnly = false;

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
                    case "--all":
                        runAll = true;
                        break;
                    case "--failed":
                        failedOnly = true;
                        break;
                    case "--help":
                    case "-h":
                        output.WriteLine("Usage: " + Usage);
                        output.WriteLine("Runs *.test.kscript and *.test.ks.xml files (or the files test.include names in kscript.json). With no paths, runs the current project's tests.");
                        output.WriteLine("In a project, runs only the tests a change since the last run can affect. --all runs every test; --failed runs the tests that failed last time.");
                        return 0;
                    default:
                        if (arg.StartsWith("--", StringComparison.Ordinal)) return Fail(error, $"Unknown option '{arg}'. Usage: {Usage}");
                        paths.Add(arg);
                        break;
                }
            }

            if (runAll && failedOnly) return Fail(error, "Use --all or --failed, not both.");
            bool namedFiles = paths.Any(p => File.Exists(Path.Combine(cwd, p)));

            if (paths.Count == 0)
            {
                ProjectConfig project = null;
                try
                {
                    project = ProjectConfig.FindAndLoad(cwd);
                }
                catch (ProjectConfigException ex)
                {
                    return Fail(error, ex);
                }
                paths.Add(project?.Directory ?? cwd);
            }

            // A kscript.json that can't be used is a settings error, not a test that failed.
            foreach (string given in paths.Where(p => Directory.Exists(p) || File.Exists(p)))
            {
                try
                {
                    ProjectConfig.FindAndLoad(Directory.Exists(given) ? given : Path.GetDirectoryName(Path.GetFullPath(given)));
                }
                catch (ProjectConfigException ex)
                {
                    return Fail(error, ex);
                }
            }

            var missing = new List<string>();
            List<string> files = TestRunner.Discover(paths, missing);
            foreach (string m in missing) new CliOutput(error).Error($"There is no file or folder at '{m}'.");
            if (missing.Count > 0) return ExitCodes.Usage;
            if (files.Count == 0)
            {
                new CliOutput(error).Error($"No test files found in {string.Join(", ", paths)}. Test files are named like greet.test.kscript (or greet.test.ks.xml); create one with kscript make test greet.");
                return 1;
            }

            var cli = new CliOutput(output, color);
            var style = cli.Style;
            settings.Selection = Selection(files, cwd, paths, runAll, failedOnly, namedFiles || !string.IsNullOrEmpty(settings.Filter));
            if (settings.Selection?.Mode != TestSelectionMode.All && settings.Selection?.FullRunReason is string reason && settings.Selection.Graph != null)
            {
                output.WriteLine(style.Dim($"  Running every test: {reason}."));
                output.WriteLine();
            }
            var clock = Stopwatch.StartNew();
            var results = new List<TestFileResult>();
            foreach (string file in files)
            {
                string display = Path.GetRelativePath(settings.BaseDirectory, file).Replace('\\', '/');
                if (display.StartsWith("..", StringComparison.Ordinal)) display = file;

                // A file's results are collected, then shown under a PASS or FAIL label, as Pest and artisan test do.
                var lines = new StringWriter { NewLine = output.NewLine };
                TestFileResult result;
                try
                {
                    result = TestRunner.RunFile(file, settings, r => PrintResult(lines, style, r));
                }
                catch (OperationCanceledException)
                {
                    output.Write(lines.ToString());
                    error.WriteLine("Stopped.");
                    return 130;
                }
                results.Add(result);
                if (result.NotRun) continue;

                if (result.Error != null)
                {
                    lines.WriteLine($"  {style.Red("✗")} {style.Red("Could not run the file:")} {Indent(result.Error, 4).TrimStart()}");
                    if (!string.IsNullOrEmpty(result.Output)) lines.WriteLine(style.Dim($"    Output:\n{Indent(result.Output, 6)}"));
                }
                else if (result.Tests.Count == 0)
                {
                    // With --filter, files without a match are left out of the output, as are files whose tests all stayed the same.
                    if (!string.IsNullOrEmpty(settings.Filter) || result.Unchanged > 0) continue;
                    lines.WriteLine(style.Dim("  (no tests in this file)"));
                }

                if (result.Failed || result.Error != null) cli.Label("FAIL", CliOutput.ErrorCode, display);
                else if (result.Tests.Count > 0 && result.Tests.All(t => t.Outcome == TestOutcome.Skipped)) cli.Label("SKIP", CliOutput.WarnCode, display);
                else cli.Label("PASS", CliOutput.DoneCode, display);
                output.Write(lines.ToString());
                output.WriteLine();
            }
            clock.Stop();
            if (settings.Selection != null) settings.Selection.Update(results).Save(settings.Selection.ProjectRoot);
            int unchanged = results.Sum(r => r.Unchanged);

            List<TestResult> all = results.SelectMany(r => r.Tests).ToList();
            int passed = all.Count(t => t.Outcome == TestOutcome.Passed);
            int failed = all.Count(t => t.Outcome == TestOutcome.Failed);
            int skipped = all.Count(t => t.Outcome == TestOutcome.Skipped);
            int brokenFiles = results.Count(r => r.Error != null);

            var parts = new List<string>();
            if (failed > 0) parts.Add(style.Apply($"{failed} failed", "1;31"));
            if (passed > 0) parts.Add(style.Apply($"{passed} passed", "1;32"));
            if (skipped > 0) parts.Add(style.Apply($"{skipped} skipped", "1;33"));
            if (unchanged > 0) parts.Add(style.Dim(settings.Selection?.Mode == TestSelectionMode.Failed ? $"{unchanged} not run (passed last time)" : $"{unchanged} unchanged since the last run"));
            parts.Add($"{all.Count + unchanged} total");
            output.WriteLine($"  {style.Gray("Tests:")}    {string.Join(", ", parts)}");
            int failedFiles = results.Count(r => r.Failed);
            int notRun = results.Count(r => r.NotRun);
            output.WriteLine($"  {style.Gray("Files:")}    {(failedFiles > 0 ? style.Apply($"{failedFiles} failed", "1;31") + ", " : string.Empty)}{(notRun > 0 ? style.Dim($"{notRun} unchanged") + ", " : string.Empty)}{results.Count} total{(brokenFiles > 0 ? $" ({brokenFiles} could not run)" : string.Empty)}");
            output.WriteLine($"  {style.Gray("Duration:")} {clock.Elapsed.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture)}s");

            if (junit != null)
            {
                try
                {
                    WriteJUnit(junit, results, clock.Elapsed);
                    cli.Info($"Wrote the JUnit report to [{junit}].");
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    KScript.Runtime.ErrorReport.Write(error, $"Could not write {junit}.", ex);
                    return 1;
                }
            }

            if (all.Count == 0 && brokenFiles == 0 && settings.Selection?.Selective == true)
            {
                output.WriteLine();
                cli.Info(settings.Selection.Mode == TestSelectionMode.Failed
                    ? "No test failed in the last run. Use --all to run every test."
                    : "Nothing the tests reach changed since the last run. Use --all to run every test.");
                return 0;
            }
            if (all.Count == 0 && brokenFiles == 0)
            {
                new CliOutput(error).Warn(string.IsNullOrEmpty(settings.Filter) ? "No tests found." : $"No tests match '{settings.Filter}'.");
                return 1;
            }
            return failed == 0 && brokenFiles == 0 ? 0 : 1;
        }

        /// <summary>
        /// How the run picks its tests: in a project, only the affected ones (or --failed ones), unless --all is given,
        /// files or --filter were named, or this is a CI run; those run everything but still record what each test
        /// reached. Null (no recording) outside a project, or when the files aren't all in it.
        /// </summary>
        private static TestSelection Selection(List<string> files, string cwd, List<string> paths, bool runAll, bool failedOnly, bool named)
        {
            ProjectConfig project;
            try
            {
                string start = Path.GetFullPath(Path.Combine(cwd, paths[0]));
                project = ProjectConfig.FindAndLoad(Directory.Exists(start) ? start : Path.GetDirectoryName(start));
            }
            catch (ProjectConfigException)
            {
                return null;
            }
            if (project?.FilePath == null) return null;
            string root = Path.GetFullPath(project.Directory);
            if (files.Any(f => Path.GetRelativePath(root, f).StartsWith("..", StringComparison.Ordinal))) return null;
            TestSelectionMode mode = failedOnly ? TestSelectionMode.Failed
                : runAll || named || TestSelection.InCi() ? TestSelectionMode.All
                : TestSelectionMode.Affected;
            return TestSelection.For(root, mode);
        }

        private static void PrintResult(TextWriter output, CliStyle style, TestResult r)
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

        /// <summary>A kscript.json that can't be used.</summary>
        private static int Fail(TextWriter error, Exception ex)
        {
            KScript.Runtime.ErrorReport.Write(error, ex);
            return ExitCodes.Usage;
        }

        /// <summary>A command line that can't be run as written.</summary>
        private static int Fail(TextWriter error, string message)
        {
            new KScript.Output.CliOutput(error).Error(message);
            return ExitCodes.Usage;
        }

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
                    new XAttribute("name", Clean(file.DisplayPath)),
                    new XAttribute("file", Clean(file.DisplayPath)),
                    new XAttribute("tests", file.Tests.Count + (file.Error != null ? 1 : 0)),
                    new XAttribute("failures", file.Tests.Count(t => t.Outcome == TestOutcome.Failed && t.ErrorType == TestPackage.AssertionType)),
                    new XAttribute("errors", file.Tests.Count(t => t.Outcome == TestOutcome.Failed && t.ErrorType != TestPackage.AssertionType) + (file.Error != null ? 1 : 0)),
                    new XAttribute("skipped", file.Tests.Count(t => t.Outcome == TestOutcome.Skipped)),
                    new XAttribute("time", Seconds(file.Duration)));

                if (file.Error != null)
                {
                    suite.Add(new XElement("testcase", new XAttribute("name", "(load)"), new XAttribute("classname", Clean(file.DisplayPath)), new XAttribute("time", "0"),
                        new XElement("error", new XAttribute("message", FirstLine(file.Error)), Clean(file.Error))));
                }

                foreach (TestResult t in file.Tests)
                {
                    var testcase = new XElement("testcase",
                        new XAttribute("name", Clean(t.FullName)),
                        new XAttribute("classname", Clean(file.DisplayPath)),
                        new XAttribute("file", Clean(file.DisplayPath)),
                        new XAttribute("line", t.Line),
                        new XAttribute("time", Seconds(t.Duration)));
                    if (t.Outcome == TestOutcome.Skipped)
                    {
                        testcase.Add(new XElement("skipped", string.IsNullOrEmpty(t.Message) ? null : new XAttribute("message", Clean(t.Message))));
                    }
                    else if (t.Outcome == TestOutcome.Failed)
                    {
                        string details = t.Location != null ? $"{t.Message}\nat {t.Location}" : t.Message;
                        testcase.Add(new XElement(t.ErrorType == TestPackage.AssertionType ? "failure" : "error",
                            new XAttribute("message", FirstLine(t.Message)), new XAttribute("type", Clean(t.ErrorType ?? "Error")), Clean(details)));
                    }
                    if (!string.IsNullOrEmpty(t.Output)) testcase.Add(new XElement("system-out", Clean(t.Output)));
                    suite.Add(testcase);
                }
                suites.Add(suite);
            }

            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            new XDocument(new XDeclaration("1.0", "utf-8", null), suites).Save(path);
        }

        private static string FirstLine(string text) => Clean((text ?? string.Empty).Replace("\r\n", "\n").Split('\n')[0]);

        /// <summary>
        /// The text without the characters an XML file can't hold (such as the escape character of coloured output),
        /// so what a test printed can never leave a report that CI can't read.
        /// </summary>
        private static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var clean = new System.Text.StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) clean.Append(c).Append(text[++i]);
                else if (!char.IsSurrogate(c) && System.Xml.XmlConvert.IsXmlChar(c)) clean.Append(c);
            }
            return clean.ToString();
        }
    }
}
