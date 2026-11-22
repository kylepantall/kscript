using KScript.Language.Agents;
using KScript.Language.Outputs;
using KScript.LanguageServer;
using KScript.Projects;
using KScript.Runtime;

namespace KScript.Language.Tests
{
    /// <summary>An output for tests: writes out.txt with the number of scripts, or throws.</summary>
    internal sealed class CountOutput : IProjectOutput
    {
        public Exception Fail;

        public string Name => "count";

        public bool IsEnabled(string root) => File.Exists(Path.Combine(root, "count.on"));

        public IReadOnlyList<string> Write(string root, bool check)
        {
            if (Fail != null) throw Fail;
            string text = Directory.GetFiles(root, "*.kscript").Length.ToString();
            string path = Path.Combine(root, "out.txt");
            if (File.Exists(path) && File.ReadAllText(path) == text) return Array.Empty<string>();
            if (!check) File.WriteAllText(path, text);
            return new[] { "out.txt" };
        }

        public bool Owns(string relative) => relative == "out.txt";
    }

    /// <summary>kscript watch, the project outputs it rebuilds, and the watcher behind it, the editor and kscript serve.</summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public sealed class ProjectWatchTests : IDisposable
    {
        private readonly string dir = Directory.CreateTempSubdirectory("kscript-watch").FullName;
        private readonly CountOutput count = new CountOutput();

        public ProjectWatchTests() => ProjectOutputs.Add(count);

        public void Dispose()
        {
            ProjectOutputs.Remove("count");
            Directory.Delete(dir, true);
        }

        private string Write(string name, string text)
        {
            string path = Path.Combine(dir, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text);
            return path;
        }

        private (int Code, string Out) Watch(CancellationToken stop, params string[] args)
        {
            var o = new StringWriter { NewLine = "\n" };
            return (WatchCommand.Run(args, o, stop, TimeSpan.FromMilliseconds(20)), o.ToString());
        }

        private (int Code, string Out) Watch(params string[] args) => Watch(new CancellationToken(true), args);

        [Fact]
        public void OutputsAreRegisteredByName()
        {
            Assert.Contains(ProjectOutputs.All, o => o.Name == "agents");
            ProjectOutputs.Add(new CountOutput());
            Assert.Single(ProjectOutputs.All, o => o.Name == "count");
            Assert.False(ProjectOutputs.Remove("nothing"));

            Assert.Empty(ProjectOutputs.Enabled(dir));
            Write("count.on", "");
            Assert.Equal(new[] { "count" }, ProjectOutputs.Enabled(dir).Select(o => o.Name));
            AgentDocs.Write(dir);
            Assert.Equal(new[] { "agents", "count" }, ProjectOutputs.Enabled(dir).Select(o => o.Name));
        }

        [Fact]
        public void BuildReportsEachOutputAndKeepsGoingAfterAFailure()
        {
            Write("main.kscript", "fn one() => 1\n");
            var failing = new CountOutput { Fail = new IOException("disk full") };
            IReadOnlyList<OutputResult> results = ProjectOutputs.Build(dir, only: new IProjectOutput[] { failing, new AgentsOutput() });
            Assert.Equal("disk full", results[0].Error);
            Assert.Contains("AGENTS.md", results[1].Changed);
            Assert.Equal(new[] { "count: disk full", "agents: updated " + string.Join(", ", results[1].Changed) }, ProjectOutputs.Describe(results));

            failing.Fail = new KScriptException("bad script");
            Assert.Equal("bad script", ProjectOutputs.Build(dir, only: new[] { failing })[0].Error);
            Assert.Empty(ProjectOutputs.Describe(ProjectOutputs.Build(dir, only: new[] { new AgentsOutput() })));
        }

        [Theory]
        [InlineData("main.kscript", true)]
        [InlineData("lib/tasks.ks.xml", true)]
        [InlineData("lib\\tasks.kscript", true)]
        [InlineData("bin", true)]
        [InlineData("", false)]
        [InlineData(null, false)]
        [InlineData("/", false)]
        [InlineData(".git/index", false)]
        [InlineData(".kscript/agents/index.md", false)]
        [InlineData("lib/.cache/x", false)]
        [InlineData("bin/Debug/x.dll", false)]
        [InlineData("node_modules/a/b.js", false)]
        [InlineData("dist/app.js", false)]
        [InlineData("AGENTS.md", false)]
        [InlineData("CLAUDE.md", false)]
        [InlineData("out.txt", false)]
        public void OnlySourceChangesCount(string relative, bool source) => Assert.Equal(source, ProjectOutputs.IsSource(relative));

        [Fact]
        public void TheWatcherWaitsForTheFilesToGoQuiet()
        {
            Write("count.on", "");
            var lines = new List<string>();
            var watcher = new ProjectWatcher(dir, null, lines.Add) { Delay = TimeSpan.FromHours(1) };
            Assert.False(watcher.BuildIfDue());

            Assert.False(watcher.Changed(Path.Combine(dir, "out.txt")));
            Assert.False(watcher.Changed(Path.Combine(dir, "..", "elsewhere.kscript")));
            Assert.True(watcher.Changed(Write("main.kscript", "fn one() => 1\n")));
            Assert.False(watcher.BuildIfDue());

            var quick = new ProjectWatcher(dir, new[] { count }, lines.Add) { Delay = TimeSpan.Zero };
            Assert.True(quick.Changed(Path.Combine(dir, "main.kscript")));
            Assert.True(quick.BuildIfDue());
            Assert.Equal(1, quick.Builds);
            Assert.Equal(new[] { "count: updated out.txt" }, lines);
            Assert.Equal("1", File.ReadAllText(Path.Combine(dir, "out.txt")));
        }

        [Fact]
        public async Task TheWatcherRebuildsWhenAFileChanges()
        {
            Write("count.on", "");
            // The watcher may be writing out.txt at the moment it is read (on Windows that read fails), so try again.
            string Output()
            {
                try
                {
                    return File.Exists(Path.Combine(dir, "out.txt")) ? File.ReadAllText(Path.Combine(dir, "out.txt")) : null;
                }
                catch (IOException)
                {
                    return null;
                }
            }

            Write("main.kscript", "fn one() => 1\n");
            var lines = new List<string>();
            var watcher = new ProjectWatcher(dir, null, l => { lock (lines) lines.Add(l); }) { Delay = TimeSpan.FromMilliseconds(20) };
            using var stop = new CancellationTokenSource();
            Task running = watcher.RunAsync(stop.Token);
            Thread.Sleep(200);
            Write("two.kscript", "fn two() => 2\n");
            Assert.True(SpinWait.SpinUntil(() => Output() == "2", 10_000));
            File.Move(Path.Combine(dir, "two.kscript"), Path.Combine(dir, "three.kscript"));
            File.Delete(Path.Combine(dir, "three.kscript"));
            Assert.True(SpinWait.SpinUntil(() => Output() == "1", 10_000));
            stop.Cancel();
            await running.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(running.IsCompletedSuccessfully);
            lock (lines) Assert.Contains("count: updated out.txt", lines);
        }

        [Fact]
        public void WatchChecksItsArguments()
        {
            Assert.Equal((0, WatchCommand.Usage + "\n"), Watch("--help"));
            Assert.Equal((0, WatchCommand.Usage + "\n"), Watch("-h"));
            Assert.Equal(2, Watch("--nope").Code);
            Assert.Equal(2, Watch(dir, dir).Code);
            var missing = Watch(Path.Combine(dir, "missing"));
            Assert.Equal(2, missing.Code);
            Assert.Contains("folder not found", missing.Out);

            var nothing = Watch(dir);
            Assert.Equal(2, nothing.Code);
            Assert.Contains("Nothing to build", nothing.Out);
            Assert.Equal(0, Watch(dir, "--check").Code);
        }

        [Fact]
        public void WatchOnceBuildsAndCheckFindsStaleFiles()
        {
            Write("main.kscript", "fn one() => 1\n");
            Write("count.on", "");
            var stale = Watch(dir, "--check");
            Assert.Equal(1, stale.Code);
            Assert.Contains("out.txt: out of date (count)", stale.Out);
            Assert.False(File.Exists(Path.Combine(dir, "out.txt")));

            var once = Watch(dir, "--once");
            Assert.Equal(0, once.Code);
            Assert.Contains("count: updated out.txt", once.Out);
            Assert.Contains("Up to date: count.", once.Out);
            Assert.Equal((0, "Up to date: count.\n"), Watch(dir, "--check"));

            count.Fail = new UnauthorizedAccessException("read only");
            var failed = Watch(dir, "--once");
            Assert.Equal(1, failed.Code);
            Assert.Equal("count: read only\n", failed.Out);
            var failedCheck = Watch(dir, "--check");
            Assert.Equal(1, failedCheck.Code);
            Assert.Contains("count: read only", failedCheck.Out);
        }

        [Fact]
        public void WatchRunsInTheCurrentFolderUntilStopped()
        {
            Write("count.on", "");
            Write("main.kscript", "fn one() => 1\n");
            string before = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(dir);
            try
            {
                using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
                var (code, output) = Watch(stop.Token);
                Assert.Equal(0, code);
                Assert.Contains("count: updated out.txt", output);
                Assert.Contains("for count. Press Ctrl+C to stop.", output);
            }
            finally
            {
                Directory.SetCurrentDirectory(before);
            }
        }

        [Fact]
        public void ABadProjectFileIsReported()
        {
            AgentDocs.Write(dir);
            Write("kscript.json", "{ \"standard\": \"missing-standard\" }");
            var (code, output) = Watch(dir, "--once");
            Assert.Equal(1, code);
            Assert.StartsWith("agents: ", output);
        }
    }
}
