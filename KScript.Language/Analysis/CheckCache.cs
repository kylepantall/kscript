using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

using KScript.Projects;
using KScript.Runtime.Dependencies;
using KScript.Syntax;
using KScript.Syntax.Text;

namespace KScript.Language.Analysis
{
    /// <summary>
    /// What kscript check found in each file last time, with everything the analysis looked at (see
    /// <see cref="RecordingFileSystem"/>). Kept in <c>.kscript/cache/check.json</c>, so the next check analyses only
    /// the files whose text changed, or whose analysis read something that changed: a script they import, a
    /// component, a file that now mentions one of their functions. The rest give the problems they gave before.
    /// </summary>
    public sealed class CheckCache
    {
        public const int FormatVersion = 1;

        private sealed class Entry
        {
            public string Text;
            public string Settings;
            public RecordingFileSystem Inputs;
            public List<Diagnostic> Diagnostics;
        }

        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> readNow = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> textNow = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly IFileSystem files;

        private CheckCache(IFileSystem files) => this.files = files ?? DiskFileSystem.Instance;

        /// <summary>How many files were taken from the cache, and how many analysed, in this run.</summary>
        public int Reused { get; private set; }

        public int Analysed { get; private set; }

        /// <summary>The kscript build: another build may check differently, so its cache isn't used.</summary>
        public static string CurrentBuild =>
            typeof(Runtime.Interpreter).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";

        public static string PathFor(string projectRoot) => Path.Combine(projectRoot, ProjectConfig.GeneratedFolder, "cache", "check.json");

        /// <summary>
        /// The problems in a file: from the cache when its text, its settings and everything its last analysis looked
        /// at are the same, otherwise from <paramref name="analyse"/>, which is given a file system to read through.
        /// </summary>
        public List<Diagnostic> Check(string file, string text, string settings, Func<IFileSystem, List<Diagnostic>> analyse)
        {
            string key = Path.GetFullPath(file);
            string hash = SourceFingerprint.Hash(text);
            if (entries.TryGetValue(key, out Entry entry) && entry.Text == hash && entry.Settings == settings && Unchanged(entry.Inputs))
            {
                Reused++;
                return entry.Diagnostics.ToList();
            }
            var inputs = new RecordingFileSystem(files);
            List<Diagnostic> found = analyse(inputs);
            Analysed++;
            entries[key] = new Entry { Text = hash, Settings = settings, Inputs = inputs, Diagnostics = found.ToList() };
            return found;
        }

        /// <summary>True when everything a recorded analysis looked at is as it was.</summary>
        private bool Unchanged(RecordingFileSystem inputs)
        {
            foreach ((string path, string hash) in inputs.Reads)
            {
                if (!readNow.TryGetValue(path, out string now))
                {
                    string text = Text(path);
                    readNow[path] = now = text == null ? null : SourceFingerprint.Hash(text);
                }
                if (now != hash) return false;
            }
            foreach ((string key, bool answer) in inputs.Exists)
            {
                string path = key.Substring(1);
                if ((key[0] == 'f' ? files.FileExists(path) : files.DirectoryExists(path)) != answer) return false;
            }
            foreach ((string key, string hash) in inputs.Lists)
            {
                string[] parts = key.Split('|');
                IEnumerable<string> names = parts[0] == "f" ? files.ListFiles(parts[1], parts[2])
                    : files.ListDirectories(parts[1]).Where(n => Path.GetFileName(n) != ProjectConfig.GeneratedFolder);
                if (RecordingFileSystem.ListHash(names) != hash) return false;
            }
            foreach ((string path, Dictionary<string, bool> names) in inputs.Scans)
            {
                string text = Text(path);
                foreach ((string name, bool found) in names)
                {
                    if ((text != null && text.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) != found) return false;
                }
            }
            return true;
        }

        private string Text(string path)
        {
            if (!textNow.TryGetValue(path, out string text)) textNow[path] = text = files.ReadText(path);
            return text;
        }

        #region Reading and writing

        /// <summary>A cache with nothing in it, so every file is analysed (and the result saved for next time).</summary>
        public static CheckCache Empty(IFileSystem files = null) => new CheckCache(files);

        /// <summary>The cache kept for a project (empty when there is none, it is from another build, or it can't be read).</summary>
        public static CheckCache Load(string projectRoot, IFileSystem files = null)
        {
            var cache = new CheckCache(files);
            try
            {
                string path = PathFor(projectRoot);
                if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject root
                    && (int?)root["version"] == FormatVersion && (string)root["build"] == CurrentBuild && root["files"] is JsonObject saved)
                {
                    foreach (var file in saved)
                    {
                        if (file.Value is JsonObject o) cache.entries[file.Key] = Read(o);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException || ex is InvalidOperationException || ex is FormatException || ex is ArgumentException)
            {
                cache.entries.Clear();
            }
            return cache;
        }

        /// <summary>Saves the cache in the project. A cache that can't be saved only means the next check analyses more.</summary>
        public void Save(string projectRoot)
        {
            var saved = new JsonObject();
            foreach ((string file, Entry e) in entries.OrderBy(e => e.Key, StringComparer.Ordinal)) saved[file] = Write(e);
            var root = new JsonObject { ["version"] = FormatVersion, ["build"] = CurrentBuild, ["files"] = saved };
            string path = PathFor(projectRoot);
            try
            {
                string folder = Path.GetDirectoryName(path);
                Directory.CreateDirectory(folder);
                string ignore = Path.Combine(folder, ".gitignore");
                if (!File.Exists(ignore)) File.WriteAllText(ignore, "# Made by kscript: caches of this machine's runs.\n*\n");
                string temp = path + ".tmp";
                File.WriteAllText(temp, root.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
                File.Move(temp, path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Read-only checkouts and the like: the next check starts from nothing.
            }
        }

        private static Entry Read(JsonObject o)
        {
            var inputs = new RecordingFileSystem();
            foreach (var kv in o["reads"] as JsonObject ?? new JsonObject()) inputs.Reads[kv.Key] = (string)kv.Value;
            foreach (var kv in o["exists"] as JsonObject ?? new JsonObject()) inputs.Exists[kv.Key] = (bool)kv.Value;
            foreach (var kv in o["lists"] as JsonObject ?? new JsonObject()) inputs.Lists[kv.Key] = (string)kv.Value;
            foreach (var kv in o["scans"] as JsonObject ?? new JsonObject())
            {
                var names = new Dictionary<string, bool>(StringComparer.Ordinal);
                foreach (var n in kv.Value as JsonObject ?? new JsonObject()) names[n.Key] = (bool)n.Value;
                inputs.Scans[kv.Key] = names;
            }
            var diagnostics = new List<Diagnostic>();
            foreach (JsonNode d in o["diagnostics"] as JsonArray ?? new JsonArray())
            {
                var fixes = new List<QuickFix>();
                foreach (JsonNode f in d["fixes"] as JsonArray ?? new JsonArray())
                {
                    TextChange[] changes = (f["changes"] as JsonArray ?? new JsonArray())
                        .Select(c => new TextChange(new TextSpan((int)c["start"], (int)c["length"]), (string)c["text"])).ToArray();
                    fixes.Add(new QuickFix((string)f["title"], changes));
                }
                diagnostics.Add(new Diagnostic(new TextSpan((int)d["start"], (int)d["length"]), Enum.Parse<DiagnosticSeverity>((string)d["severity"]),
                    (string)d["code"], (string)d["message"], fixes));
            }
            return new Entry { Text = (string)o["text"], Settings = (string)o["settings"], Inputs = inputs, Diagnostics = diagnostics };
        }

        private static JsonObject Write(Entry e)
        {
            var reads = new JsonObject();
            foreach (var kv in e.Inputs.Reads) reads[kv.Key] = kv.Value;
            var exists = new JsonObject();
            foreach (var kv in e.Inputs.Exists) exists[kv.Key] = kv.Value;
            var lists = new JsonObject();
            foreach (var kv in e.Inputs.Lists) lists[kv.Key] = kv.Value;
            var scans = new JsonObject();
            foreach (var kv in e.Inputs.Scans)
            {
                var names = new JsonObject();
                foreach (var n in kv.Value) names[n.Key] = n.Value;
                scans[kv.Key] = names;
            }
            var diagnostics = new JsonArray();
            foreach (Diagnostic d in e.Diagnostics)
            {
                var fixes = new JsonArray();
                foreach (QuickFix f in d.Fixes ?? Array.Empty<QuickFix>())
                {
                    var changes = new JsonArray();
                    foreach (TextChange c in f.Changes) changes.Add((JsonNode)new JsonObject { ["start"] = c.Span.Start, ["length"] = c.Span.Length, ["text"] = c.NewText });
                    fixes.Add((JsonNode)new JsonObject { ["title"] = f.Title, ["changes"] = changes });
                }
                diagnostics.Add((JsonNode)new JsonObject
                {
                    ["start"] = d.Span.Start, ["length"] = d.Span.Length, ["severity"] = d.Severity.ToString(),
                    ["code"] = d.Code, ["message"] = d.Message, ["fixes"] = fixes,
                });
            }
            return new JsonObject
            {
                ["text"] = e.Text, ["settings"] = e.Settings, ["reads"] = reads, ["exists"] = exists, ["lists"] = lists, ["scans"] = scans, ["diagnostics"] = diagnostics,
            };
        }

        #endregion
    }
}
