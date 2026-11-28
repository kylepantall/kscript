using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

using KScript.Projects;
using KScript.Runtime.Dependencies;

namespace KScript.Testing
{
    /// <summary>What one test reached when it last ran, and how it went.</summary>
    public sealed class TestRecord
    {
        /// <summary>The fingerprint of the test's own block.</summary>
        public string Hash { get; set; }

        public TestOutcome Outcome { get; set; }

        /// <summary>The parts of files the test reached (path relative to the project, then keys).</summary>
        public Dictionary<string, List<string>> Reached { get; set; } = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        /// <summary>True when something it reached changed in a run that didn't run it, so the next run must.</summary>
        public bool Stale { get; set; }
    }

    /// <summary>What a test file's top-level code reached, and each of its tests.</summary>
    public sealed class TestFileRecord
    {
        /// <summary>The fingerprints of the file's test blocks, in order.</summary>
        public List<string> TestHashes { get; set; } = new List<string>();

        /// <summary>What loading the file and running its top-level code reached. Every test in the file depends on it.</summary>
        public Dictionary<string, List<string>> Reached { get; set; } = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        /// <summary>True when the file couldn't load or its top-level code failed.</summary>
        public bool Error { get; set; }

        public bool Stale { get; set; }

        /// <summary>By the test's full name (groups › name).</summary>
        public Dictionary<string, TestRecord> Tests { get; set; } = new Dictionary<string, TestRecord>(StringComparer.Ordinal);
    }

    /// <summary>
    /// The project's test dependency graph: for each test, the functions, methods, models, types and files it reached
    /// when it last ran, with the fingerprints those parts had then. Kept in <c>.kscript/cache/tests.json</c>, so the
    /// next <c>kscript test</c> runs only the tests a change can affect.
    /// </summary>
    public sealed class TestGraph
    {
        public const int FormatVersion = 1;

        /// <summary>Where the graph is kept, under the project folder.</summary>
        public static string PathFor(string projectRoot) => Path.Combine(projectRoot, ProjectConfig.GeneratedFolder, "cache", "tests.json");

        /// <summary>The kscript build the graph was made by: another build may run code differently, so its graph isn't used.</summary>
        public static string CurrentBuild =>
            typeof(Runtime.Interpreter).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";

        public string Build { get; set; } = CurrentBuild;

        /// <summary>The fingerprints of the project settings (kscript.json, .env files): a change to any of them runs every test.</summary>
        public Dictionary<string, string> Settings { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>The project's scripts (not tests) at the last run: a new one may be loaded by convention, so it runs every test.</summary>
        public List<string> Scripts { get; set; } = new List<string>();

        /// <summary>Each part's fingerprint when it was recorded: path, then key, then hash.</summary>
        public Dictionary<string, Dictionary<string, string>> Fingerprints { get; set; } = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        /// <summary>By test file path, relative to the project.</summary>
        public Dictionary<string, TestFileRecord> Files { get; set; } = new Dictionary<string, TestFileRecord>(StringComparer.Ordinal);

        #region Reading and writing

        /// <summary>The graph saved for a project, or null when there is none or it can't be used.</summary>
        public static TestGraph Load(string projectRoot)
        {
            string path = PathFor(projectRoot);
            try
            {
                if (!File.Exists(path)) return null;
                return Parse(File.ReadAllText(path));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException || ex is InvalidOperationException || ex is FormatException)
            {
                return null;
            }
        }

        internal static TestGraph Parse(string text)
        {
            if (JsonNode.Parse(text) is not JsonObject root || (int?)root["version"] != FormatVersion) return null;
            var graph = new TestGraph
            {
                Build = (string)root["build"],
                Settings = Strings(root["settings"] as JsonObject),
                Scripts = (root["scripts"] as JsonArray)?.Select(n => (string)n).ToList() ?? new List<string>(),
            };
            if (root["fingerprints"] is JsonObject prints)
            {
                foreach (var file in prints) graph.Fingerprints[file.Key] = Strings(file.Value as JsonObject);
            }
            if (root["files"] is JsonObject files)
            {
                foreach (var entry in files)
                {
                    if (entry.Value is not JsonObject f) continue;
                    var record = new TestFileRecord
                    {
                        TestHashes = (f["test_hashes"] as JsonArray)?.Select(n => (string)n).ToList() ?? new List<string>(),
                        Reached = Parts(f["reached"] as JsonObject),
                        Error = (bool?)f["error"] ?? false,
                        Stale = (bool?)f["stale"] ?? false,
                    };
                    if (f["tests"] is JsonObject tests)
                    {
                        foreach (var t in tests)
                        {
                            if (t.Value is not JsonObject o) continue;
                            record.Tests[t.Key] = new TestRecord
                            {
                                Hash = (string)o["hash"],
                                Outcome = Enum.TryParse((string)o["outcome"], true, out TestOutcome outcome) ? outcome : TestOutcome.Failed,
                                Reached = Parts(o["reached"] as JsonObject),
                                Stale = (bool?)o["stale"] ?? false,
                            };
                        }
                    }
                    graph.Files[entry.Key] = record;
                }
            }
            return graph;
        }

        /// <summary>Saves the graph under the project folder. A graph that can't be saved only means the next run runs more.</summary>
        public void Save(string projectRoot)
        {
            string path = PathFor(projectRoot);
            try
            {
                string folder = Path.GetDirectoryName(path);
                Directory.CreateDirectory(folder);
                // The cache is this machine's alone: it ignores itself, so it never shows up in git.
                string ignore = Path.Combine(folder, ".gitignore");
                if (!File.Exists(ignore)) File.WriteAllText(ignore, "# Made by kscript: what each test reached, for kscript test to rerun only affected tests.\n*\n");
                string temp = path + ".tmp";
                File.WriteAllText(temp, ToJson());
                File.Move(temp, path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Read-only checkouts and the like: the next run starts from nothing.
            }
        }

        internal string ToJson()
        {
            var files = new JsonObject();
            foreach (var entry in Files.OrderBy(f => f.Key, StringComparer.Ordinal))
            {
                var tests = new JsonObject();
                foreach (var t in entry.Value.Tests.OrderBy(t => t.Key, StringComparer.Ordinal))
                {
                    var o = new JsonObject
                    {
                        ["hash"] = t.Value.Hash,
                        ["outcome"] = t.Value.Outcome.ToString().ToLowerInvariant(),
                        ["reached"] = PartsJson(t.Value.Reached),
                    };
                    if (t.Value.Stale) o["stale"] = true;
                    tests[t.Key] = o;
                }
                var f = new JsonObject
                {
                    ["test_hashes"] = new JsonArray(entry.Value.TestHashes.Select(h => (JsonNode)JsonValue.Create(h)).ToArray()),
                    ["reached"] = PartsJson(entry.Value.Reached),
                    ["tests"] = tests,
                };
                if (entry.Value.Error) f["error"] = true;
                if (entry.Value.Stale) f["stale"] = true;
                files[entry.Key] = f;
            }
            var prints = new JsonObject();
            foreach (var file in Fingerprints.OrderBy(f => f.Key, StringComparer.Ordinal)) prints[file.Key] = StringsJson(file.Value);
            var root = new JsonObject
            {
                ["version"] = FormatVersion,
                ["build"] = Build,
                ["settings"] = StringsJson(Settings),
                ["scripts"] = new JsonArray(Scripts.OrderBy(s => s, StringComparer.Ordinal).Select(s => (JsonNode)JsonValue.Create(s)).ToArray()),
                ["fingerprints"] = prints,
                ["files"] = files,
            };
            // Relaxed escaping keeps paths and test names readable (a + or an accent stays as it is); the file is never put in a page.
            return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        }

        private static Dictionary<string, string> Strings(JsonObject o)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (o == null) return map;
            foreach (var kv in o) map[kv.Key] = (string)kv.Value;
            return map;
        }

        private static JsonObject StringsJson(Dictionary<string, string> map)
        {
            var o = new JsonObject();
            foreach (var kv in map.OrderBy(k => k.Key, StringComparer.Ordinal)) o[kv.Key] = kv.Value;
            return o;
        }

        private static Dictionary<string, List<string>> Parts(JsonObject o)
        {
            var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            if (o == null) return map;
            foreach (var kv in o) map[kv.Key] = (kv.Value as JsonArray)?.Select(n => (string)n).ToList() ?? new List<string>();
            return map;
        }

        private static JsonObject PartsJson(Dictionary<string, List<string>> map)
        {
            var o = new JsonObject();
            foreach (var kv in map.OrderBy(k => k.Key, StringComparer.Ordinal))
                o[kv.Key] = new JsonArray(kv.Value.Select(k => (JsonNode)JsonValue.Create(k)).ToArray());
            return o;
        }

        #endregion
    }
}
