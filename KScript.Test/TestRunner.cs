using System.Diagnostics;

using KScript.Projects;
using KScript.Runtime;

namespace KScript.Testing
{
    public enum TestOutcome
    {
        Passed,
        Failed,
        Skipped,
    }

    /// <summary>What happened to one test.</summary>
    public sealed class TestResult
    {
        public string File { get; init; }
        public string Name { get; init; }
        public IReadOnlyList<string> Groups { get; init; } = Array.Empty<string>();
        public string FullName => string.Join(" › ", Groups.Append(Name));
        public TestOutcome Outcome { get; init; }

        /// <summary>Why it failed, or why it was skipped (may be empty).</summary>
        public string Message { get; init; }

        /// <summary>The error type for a failure: Assertion for a failed check, otherwise the error that stopped the test.</summary>
        public string ErrorType { get; init; }

        /// <summary>Where it failed: file:line:column.</summary>
        public string Location { get; init; }

        /// <summary>What the test printed.</summary>
        public string Output { get; init; }

        public TimeSpan Duration { get; init; }
        public int Line { get; init; }
    }

    /// <summary>The tests of one file, or the error that stopped the file before its tests ran.</summary>
    public sealed class TestFileResult
    {
        public string Path { get; init; }

        /// <summary>The path to show: relative to the folder kscript test was run in.</summary>
        public string DisplayPath { get; init; }

        public List<TestResult> Tests { get; } = new List<TestResult>();

        /// <summary>An error loading the file or running its top-level code, or null.</summary>
        public string Error { get; set; }

        /// <summary>What the file's top-level code printed.</summary>
        public string Output { get; set; }

        public TimeSpan Duration { get; set; }

        public bool Failed => Error != null || Tests.Any(t => t.Outcome == TestOutcome.Failed);
    }

    /// <summary>Settings for a run of kscript test.</summary>
    public sealed class TestRunSettings
    {
        /// <summary>Only run tests whose full name contains this text (any case).</summary>
        public string Filter { get; set; }

        /// <summary>Seconds a test may run. Overrides test.timeout in kscript.json; a test's own timeout="..." wins over both.</summary>
        public double? Timeout { get; set; }

        /// <summary>Paths are shown relative to this folder.</summary>
        public string BaseDirectory { get; set; } = KScript.Runtime.WorkingFolder.Current;

        public CancellationToken Cancellation { get; set; }

        public PackageRegistry Packages { get; set; }
    }

    /// <summary>
    /// Finds test files and runs them. Each file runs in its own interpreter: its top-level code runs once and
    /// collects the &lt;test&gt; elements, then each test runs in a fresh scope with the &lt;before-each&gt; and
    /// &lt;after-each&gt; blocks around it, so a test sees the file's variables and functions but not another test's.
    /// </summary>
    public static class TestRunner
    {
        private static readonly string[] SkippedFolders = { "node_modules", ".git", "bin", "obj" };

        /// <summary>
        /// The test files in <paramref name="paths"/>: files as given, and the test files in folders (by test.include and
        /// test.exclude in the folder's kscript.json, or named *.test.ks.xml / *.test.xml). Missing paths go in <paramref name="missing"/>.
        /// </summary>
        public static List<string> Discover(IEnumerable<string> paths, List<string> missing = null)
        {
            var found = new List<string>();
            var seen = new HashSet<string>(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
            foreach (string path in paths)
            {
                string full = System.IO.Path.GetFullPath(path);
                if (File.Exists(full))
                {
                    if (seen.Add(full)) found.Add(full);
                }
                else if (Directory.Exists(full))
                {
                    ProjectConfig project = TryLoadProject(full);
                    foreach (string file in Walk(full, project).OrderBy(f => f, StringComparer.Ordinal))
                    {
                        if (seen.Add(file)) found.Add(file);
                    }
                }
                else
                {
                    missing?.Add(path);
                }
            }
            return found;
        }

        private static IEnumerable<string> Walk(string folder, ProjectConfig project)
        {
            var pending = new Stack<string>();
            pending.Push(folder);
            while (pending.Count > 0)
            {
                string dir = pending.Pop();
                IEnumerable<string> files, dirs;
                try
                {
                    files = Directory.EnumerateFiles(dir).ToList();
                    dirs = Directory.EnumerateDirectories(dir).ToList();
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    continue;
                }
                foreach (string file in files)
                {
                    bool inProject = project?.FilePath != null && project.Relative(file) != null;
                    if (inProject ? project.IsTestIncluded(file) : TestSettings.IsTestFileName(file)) yield return file;
                }
                foreach (string sub in dirs)
                {
                    string name = System.IO.Path.GetFileName(sub);
                    if (name.StartsWith(".", StringComparison.Ordinal) || SkippedFolders.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                    if (project?.FilePath != null && project.IsTestExcluded(sub)) continue;
                    pending.Push(sub);
                }
            }
        }

        private static ProjectConfig TryLoadProject(string start)
        {
            try
            {
                return ProjectConfig.FindAndLoad(start);
            }
            catch (ProjectConfigException)
            {
                return null;
            }
        }

        /// <summary>Runs one test file. <paramref name="onResult"/> is called as each test finishes.</summary>
        public static TestFileResult RunFile(string path, TestRunSettings settings, Action<TestResult> onResult = null)
        {
            settings ??= new TestRunSettings();
            string full = System.IO.Path.GetFullPath(path);
            var result = new TestFileResult { Path = full, DisplayPath = Display(full, settings.BaseDirectory) };
            var clock = Stopwatch.StartNew();

            // Scripts run on a thread with a large stack, as `kscript script.xml` does, so deep recursion behaves the same.
            Exception crash = null;
            var thread = new Thread(() =>
            {
                try { RunFileHere(full, settings, result, onResult); }
                catch (Exception ex) { crash = ex; }
            }, 256 * 1024 * 1024) { Name = "kscript test " + System.IO.Path.GetFileName(full) };
            thread.Start();
            thread.Join();
            // Stopped (Ctrl+C): the whole run stops, rather than this file reporting that it couldn't run.
            settings.Cancellation.ThrowIfCancellationRequested();
            if (crash != null) result.Error ??= $"{crash.GetType().Name}: {crash.Message}";
            result.Duration = clock.Elapsed;
            return result;
        }

        private static void RunFileHere(string full, TestRunSettings settings, TestFileResult result, Action<TestResult> onResult)
        {
            KScriptHost host = null;
            try
            {
                RunFileIn(full, settings, result, onResult, h => host = h);
            }
            finally
            {
                foreach (Action done in host?.AfterFile ?? new List<Action>()) done();
            }
        }

        private static void RunFileIn(string full, TestRunSettings settings, TestFileResult result, Action<TestResult> onResult, Action<KScriptHost> made)
        {
            ProjectConfig project;
            try
            {
                project = ProjectConfig.FindAndLoad(System.IO.Path.GetDirectoryName(full));
            }
            catch (ProjectConfigException ex)
            {
                result.Error = ex.Message;
                return;
            }

            double defaultTimeout = settings.Timeout ?? project?.Test.Timeout ?? TestSettings.DefaultTimeout;
            var options = new InterpreterOptions();
            if (project != null)
            {
                options.DefaultPackages.AddRange(project.Packages);
                options.DefaultPackages.AddRange(project.Test.Packages);
            }
            options.DefaultPackages.Add("KScript.Test");

            var fileOutput = new StringWriter();
            var host = new KScriptHost(fileOutput, fileOutput, new StringReader(string.Empty))
            {
                WorkingDirectory = System.IO.Path.GetDirectoryName(full),
                TestFile = full,
            };
            made(host);
            var session = new TestSession();
            session.Attach(host);
            var interpreter = new Interpreter(host, settings.Packages ?? PackageRegistry.Default, options);

            // Load and collect: the file's top-level code runs once, and each <test> it reaches is noted.
            Module module;
            using (var limit = Limit(settings.Cancellation, defaultTimeout))
            {
                options.Cancellation = limit.Token;
                try
                {
                    module = interpreter.Load(full);
                    interpreter.Run(module);
                }
                catch (KScriptException ex)
                {
                    result.Error = Describe(ex, settings, limit, settings.Cancellation, defaultTimeout, "the file's top-level code");
                    result.Output = TestPackage.Tidy(fileOutput.ToString());
                    return;
                }
            }
            result.Output = TestPackage.Tidy(fileOutput.ToString());

            List<TestCase> tests = session.Tests;
            bool anyOnly = tests.Any(t => t.Only);
            foreach (TestCase test in tests)
            {
                settings.Cancellation.ThrowIfCancellationRequested();
                if (!string.IsNullOrEmpty(settings.Filter) && test.FullName.IndexOf(settings.Filter, StringComparison.OrdinalIgnoreCase) < 0) continue;

                if (test.Skip != null || (anyOnly && !test.Only))
                {
                    Report(new TestResult
                    {
                        File = full,
                        Name = test.Name,
                        Groups = test.Groups,
                        Outcome = TestOutcome.Skipped,
                        Message = test.Skip ?? "not marked only",
                        Line = test.Line,
                    });
                    continue;
                }
                Report(RunTest(test, interpreter, session, settings, test.Timeout ?? defaultTimeout, full));
            }

            void Report(TestResult r)
            {
                result.Tests.Add(r);
                onResult?.Invoke(r);
            }
        }

        private static TestResult RunTest(TestCase test, Interpreter interpreter, TestSession session, TestRunSettings settings, double timeout, string file)
        {
            var output = new StringWriter();
            var clock = Stopwatch.StartNew();
            KScriptException failure = null;
            string message = null;
            var scope = test.Scope.Child();
            List<TestGroup> chain = test.Group.Chain().ToList();

            using (var limit = Limit(settings.Cancellation, timeout))
            using (interpreter.Host.CaptureOutput(output))
            {
                interpreter.Options.Cancellation = limit.Token;
                session.Running = test;
                interpreter.Host.TestRunning = true;
                try
                {
                    try
                    {
                        foreach (TestGroup group in chain)
                        {
                            foreach (TestHook hook in group.BeforeEach) interpreter.ExecuteChildren(hook.Element, scope, hook.Module);
                        }
                        interpreter.ExecuteChildren(test.Element, scope, test.Module);
                    }
                    finally
                    {
                        // <after-each> runs even when the test failed; the test's own failure is the one reported.
                        foreach (TestGroup group in Enumerable.Reverse(chain))
                        {
                            foreach (TestHook hook in group.AfterEach)
                            {
                                try
                                {
                                    interpreter.ExecuteChildren(hook.Element, scope, hook.Module);
                                }
                                catch (KScriptException ex)
                                {
                                    failure ??= ex;
                                }
                            }
                        }
                    }
                }
                catch (KScriptException ex)
                {
                    failure = ex;
                }
                catch (ExitSignal exit)
                {
                    message = $"The test called <exit> (code {exit.Code}). Use @run_script() to test a script that exits.";
                }
                catch (ControlSignal signal)
                {
                    message = signal.Message;
                }
                finally
                {
                    session.Running = null;
                    interpreter.Host.TestRunning = false;
                    KScriptException ending = EndTest(interpreter.Host);
                    failure ??= ending;
                }

                if (failure != null) message = Describe(failure, settings, limit, settings.Cancellation, timeout, "the test");
            }
            clock.Stop();

            return new TestResult
            {
                File = file,
                Name = test.Name,
                Groups = test.Groups,
                Outcome = message == null ? TestOutcome.Passed : TestOutcome.Failed,
                Message = message,
                ErrorType = message == null ? null : failure?.Type ?? "Error",
                Location = failure != null && failure.Line > 0 ? $"{Display(failure.File ?? file, settings.BaseDirectory)}:{failure.Line}:{failure.Column}" : $"{Display(file, settings.BaseDirectory)}:{test.Line}",
                Output = TestPackage.Tidy(output.ToString()),
                Duration = clock.Elapsed,
                Line = test.Line,
            };
        }

        /// <summary>Runs what packages left to do when the test ended (newest first), such as undoing its database changes.</summary>
        internal static KScriptException EndTest(KScriptHost host)
        {
            KScriptException failure = null;
            List<Action> work = host.AfterTest.ToList();
            host.AfterTest.Clear();
            for (int i = work.Count - 1; i >= 0; i--)
            {
                try
                {
                    work[i]();
                }
                catch (KScriptException ex)
                {
                    failure ??= ex;
                }
            }
            return failure;
        }

                private static CancellationTokenSource Limit(CancellationToken outer, double seconds)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(outer);
            if (seconds > 0) cts.CancelAfter(TimeSpan.FromSeconds(seconds));
            return cts;
        }

        private static string Describe(KScriptException ex, TestRunSettings settings, CancellationTokenSource limit, CancellationToken outer, double timeout, string what)
        {
            if (ex.Type == "Cancelled" && limit.IsCancellationRequested && !outer.IsCancellationRequested)
                return $"Timed out: {what} ran for more than {timeout:0.###} seconds. Set timeout=\"...\" on the <test>, test.timeout in kscript.json, or --timeout.";
            if (ex.Type == TestPackage.AssertionType) return ex.Message;
            // A file that can't load has no test location to show, so its error carries its own.
            string where = what != "the test" && ex.Line > 0 ? $"{Display(ex.File, settings.BaseDirectory)}:{ex.Line}:{ex.Column}: " : string.Empty;
            return $"{where}{ex.Type}: {ex.Message}";
        }

        private static string Display(string path, string baseDirectory)
        {
            if (string.IsNullOrEmpty(path) || !System.IO.Path.IsPathRooted(path)) return path;
            string rel = System.IO.Path.GetRelativePath(baseDirectory ?? KScript.Runtime.WorkingFolder.Current, path);
            return (rel.StartsWith("..", StringComparison.Ordinal) ? path : rel).Replace('\\', '/');
        }
    }
}
