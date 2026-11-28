using KScript.Projects;
using KScript.Runtime.Dependencies;
using KScript.Syntax.Markup;

namespace KScript.Testing
{
    /// <summary>What to do with one test file in a run.</summary>
    public sealed class TestFilePlan
    {
        public static readonly TestFilePlan All = new TestFilePlan();

        /// <summary>True when nothing the file's tests reached has changed, so it isn't even loaded.</summary>
        public bool Skip { get; init; }

        /// <summary>Which of the file's tests to run, or null for all of them.</summary>
        public Func<TestCase, bool> ShouldRun { get; init; }
    }

    /// <summary>Which tests a run runs: every test, the tests a change can affect, or the tests that failed last time.</summary>
    public enum TestSelectionMode
    {
        All,
        Affected,
        Failed,
    }

    /// <summary>
    /// Picks the tests a run needs from the project's <see cref="TestGraph"/>, and updates the graph with what the run
    /// found. A test runs when it is new, its own block changed, it failed (or its file couldn't load) last time, or a
    /// part of a file it reached changed: a function, method, model, type, enum, error or job it used, or the rest of
    /// a script it loaded (imports, constants, top-level code), or a file read for it (a page, config, test data).
    /// Every test runs when there is no graph yet, the kscript build changed, kscript.json or a .env file changed, or
    /// a new script appeared (it may be loaded by convention, such as an extension or a route file).
    /// </summary>
    public sealed class TestSelection
    {
        private readonly Dictionary<string, SourceFingerprint> current = new Dictionary<string, SourceFingerprint>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> settings;
        private readonly List<string> scripts;

        private TestSelection(string projectRoot, TestGraph graph, TestSelectionMode mode)
        {
            ProjectRoot = Path.GetFullPath(projectRoot);
            Graph = graph;
            Mode = mode;
            settings = SettingsFingerprints(ProjectRoot);
            scripts = ProjectScripts(ProjectRoot);
            if (mode == TestSelectionMode.All) return;
            FullRunReason = graph == null ? "there is no record of an earlier run"
                : graph.Build != TestGraph.CurrentBuild ? "kscript changed since the last run"
                : mode == TestSelectionMode.Failed ? null
                : ChangedSetting(graph) is string setting ? $"{setting} changed"
                : scripts.Except(graph.Scripts, StringComparer.Ordinal).FirstOrDefault() is string added ? $"{added} is new"
                : null;
        }

        /// <summary>The project's folder: paths in the graph are relative to it.</summary>
        public string ProjectRoot { get; }

        /// <summary>The graph from the last run, or null.</summary>
        public TestGraph Graph { get; }

        public TestSelectionMode Mode { get; }

        /// <summary>Why every test runs although only affected ones were asked for (no graph yet, settings changed...), or null.</summary>
        public string FullRunReason { get; }

        /// <summary>True when the run picks tests rather than running them all.</summary>
        public bool Selective => Mode != TestSelectionMode.All && FullRunReason == null;

        /// <summary>Reads the project's graph and works out what changed since.</summary>
        public static TestSelection For(string projectRoot, TestSelectionMode mode) =>
            new TestSelection(projectRoot, TestGraph.Load(projectRoot), mode);

        internal static TestSelection For(string projectRoot, TestGraph graph, TestSelectionMode mode) => new TestSelection(projectRoot, graph, mode);

        /// <summary>True when the CI variable says this is a CI run, where every test runs.</summary>
        public static bool InCi(Func<string, string> env = null)
        {
            string ci = (env ?? Environment.GetEnvironmentVariable)("CI");
            return !string.IsNullOrEmpty(ci) && !ci.Equals("false", StringComparison.OrdinalIgnoreCase) && ci != "0";
        }

        #region Planning

        /// <summary>What to do with a test file.</summary>
        public TestFilePlan PlanFor(string file)
        {
            if (!Selective) return TestFilePlan.All;
            string rel = Relative(file);
            if (!Graph.Files.TryGetValue(rel, out TestFileRecord record)) return Mode == TestSelectionMode.Failed ? new TestFilePlan { Skip = true } : TestFilePlan.All;
            if (record.Error) return TestFilePlan.All;

            if (Mode == TestSelectionMode.Failed)
            {
                if (!record.Tests.Values.Any(t => t.Outcome == TestOutcome.Failed)) return new TestFilePlan { Skip = true };
                return new TestFilePlan { ShouldRun = test => record.Tests.TryGetValue(test.FullName, out TestRecord t) && t.Outcome == TestOutcome.Failed };
            }

            if (record.Stale || Changed(record.Reached)) return TestFilePlan.All;
            bool sameTests = Current(rel)?.Tests is IReadOnlyList<string> hashes && hashes.SequenceEqual(record.TestHashes);
            if (sameTests && !record.Tests.Values.Any(NeedsRun)) return new TestFilePlan { Skip = true };
            return new TestFilePlan
            {
                // A skipped test is looked at again whenever its file runs: it may run now that another test lost only.
                ShouldRun = test => !record.Tests.TryGetValue(test.FullName, out TestRecord t)
                    || t.Hash != SourceFingerprint.TestHash(test.Element, test.Module.Source)
                    || t.Outcome == TestOutcome.Skipped
                    || NeedsRun(t),
            };
        }

        /// <summary>True when a test must run although its own block is the same.</summary>
        private bool NeedsRun(TestRecord test) => test.Outcome == TestOutcome.Failed || test.Stale || Changed(test.Reached);

        /// <summary>True when any part of any file in <paramref name="reached"/> has a different fingerprint now.</summary>
        private bool Changed(Dictionary<string, List<string>> reached)
        {
            foreach ((string file, List<string> keys) in reached)
            {
                Graph.Fingerprints.TryGetValue(file, out Dictionary<string, string> then);
                SourceFingerprint now = Current(file);
                foreach (string key in keys)
                {
                    string before = then != null && then.TryGetValue(key, out string h) ? h : null;
                    if (before == null || before != now?.Of(key)) return true;
                }
            }
            return false;
        }

        private string ChangedSetting(TestGraph graph)
        {
            foreach (string name in settings.Keys.Union(graph.Settings.Keys).OrderBy(n => n, StringComparer.Ordinal))
            {
                settings.TryGetValue(name, out string now);
                graph.Settings.TryGetValue(name, out string then);
                if (now != then) return name;
            }
            return null;
        }

        #endregion

        #region Updating

        /// <summary>
        /// The graph after a run: what each file and test that ran reached, the other records kept (marked to run next
        /// time when something they reached changed), and the fingerprints of every part they name.
        /// </summary>
        public TestGraph Update(IEnumerable<TestFileResult> results)
        {
            var graph = new TestGraph
            {
                Settings = new Dictionary<string, string>(settings, StringComparer.Ordinal),
                Scripts = scripts.ToList(),
            };
            bool sameBuild = Graph != null && Graph.Build == TestGraph.CurrentBuild;
            Dictionary<string, TestFileResult> ran = results.Where(r => r.Collected != null || r.Error != null)
                .GroupBy(r => Relative(r.Path)).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);

            // Records from earlier runs that this one didn't redo stay, and run next time if they are out of date now.
            if (sameBuild)
            {
                foreach ((string rel, TestFileRecord old) in Graph.Files)
                {
                    if (ran.ContainsKey(rel) || !File.Exists(Absolute(rel))) continue;
                    graph.Files[rel] = old;
                    if (old.Stale || Changed(old.Reached)) old.Stale = true;
                    foreach (TestRecord t in old.Tests.Values)
                    {
                        if (Changed(t.Reached)) t.Stale = true;
                    }
                }
            }

            foreach ((string rel, TestFileResult result) in ran)
            {
                var record = new TestFileRecord
                {
                    TestHashes = Current(rel)?.Tests.ToList() ?? new List<string>(),
                    Reached = Parts(result.Reached),
                    Error = result.Error != null,
                };
                TestFileRecord old = sameBuild && Graph.Files.TryGetValue(rel, out TestFileRecord o) ? o : null;
                foreach ((string name, string hash) in result.Collected ?? new List<(string, string)>())
                {
                    TestResult test = result.Tests.LastOrDefault(t => t.FullName == name);
                    if (test != null)
                    {
                        record.Tests[name] = new TestRecord { Hash = hash, Outcome = test.Outcome, Reached = Parts(test.Reached) };
                    }
                    else if (old != null && old.Tests.TryGetValue(name, out TestRecord kept) && kept.Hash == hash)
                    {
                        // Not run (another test was asked for, or nothing it reached changed): what it reached last time still holds.
                        if (Changed(kept.Reached)) kept.Stale = true;
                        record.Tests[name] = kept;
                    }
                }
                graph.Files[rel] = record;
            }

            foreach (string file in graph.Files.Values.SelectMany(f => f.Reached.Keys.Concat(f.Tests.Values.SelectMany(t => t.Reached.Keys))).Distinct(StringComparer.Ordinal))
            {
                if (Current(file) is not SourceFingerprint print) continue;
                var keys = new Dictionary<string, string>(StringComparer.Ordinal) { [SourceFingerprint.Rest] = print.RestHash };
                foreach (var d in print.Declarations) keys[d.Key] = d.Value;
                graph.Fingerprints[file] = keys;
            }
            return graph;
        }

        private Dictionary<string, List<string>> Parts(DependencySet set)
        {
            var parts = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            if (set == null) return parts;
            foreach ((string path, IReadOnlyCollection<string> keys) in set.Files)
            {
                string rel = Relative(path);
                if (rel.StartsWith(ProjectConfig.GeneratedFolder + "/", StringComparison.Ordinal)) continue;
                parts[rel] = keys.ToList();
            }
            return parts;
        }

        #endregion

        #region Files

        private SourceFingerprint Current(string rel)
        {
            if (!current.TryGetValue(rel, out SourceFingerprint print))
            {
                current[rel] = print = SourceFingerprint.ForFile(Absolute(rel));
            }
            return print;
        }

        /// <summary>A path as the graph keeps it: relative to the project, with forward slashes.</summary>
        internal string Relative(string path) => Path.GetRelativePath(ProjectRoot, Path.GetFullPath(path)).Replace('\\', '/');

        private string Absolute(string rel) => Path.GetFullPath(Path.Combine(ProjectRoot, rel));

        private static readonly string[] SkippedFolders = { "bin", "obj", "node_modules" };

        /// <summary>kscript.json and the .env files beside it, by name.</summary>
        private static Dictionary<string, string> SettingsFingerprints(string root)
        {
            var prints = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                foreach (string file in Directory.EnumerateFiles(root))
                {
                    string name = Path.GetFileName(file);
                    if (name != ProjectConfig.FileName && name != ".env" && !name.StartsWith(".env.", StringComparison.Ordinal)) continue;
                    prints[name] = SourceFingerprint.Hash(File.ReadAllText(file));
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Unreadable settings count as none.
            }
            return prints;
        }

        /// <summary>The project's scripts other than tests, relative to it.</summary>
        private static List<string> ProjectScripts(string root)
        {
            var found = new List<string>();
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                string dir = pending.Pop();
                try
                {
                    foreach (string file in Directory.EnumerateFiles(dir))
                    {
                        if (ScriptFiles.IsScript(file) && !TestSettings.IsTestFileName(file))
                            found.Add(Path.GetRelativePath(root, file).Replace('\\', '/'));
                    }
                    foreach (string sub in Directory.EnumerateDirectories(dir))
                    {
                        string name = Path.GetFileName(sub);
                        if (name.StartsWith(".", StringComparison.Ordinal) || SkippedFolders.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                        pending.Push(sub);
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // An unreadable folder is skipped, as test discovery skips it.
                }
            }
            found.Sort(StringComparer.Ordinal);
            return found;
        }

        #endregion
    }
}
