using KScript.Language.Catalog;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Language.Analysis
{
    /// <summary>One <c>@emit_event('note-deleted', data: $note.id)</c> call: the event's name and what it sends.</summary>
    public sealed class EmittedEvent
    {
        public EmittedEvent(string name, TypeShape data, string path, LinePosition at)
        {
            Name = name;
            Data = data;
            Path = path;
            At = at;
        }

        public string Name { get; }

        /// <summary>The shape of the data: argument, or null when the call sends none.</summary>
        public TypeShape Data { get; }

        /// <summary>The file the call is in.</summary>
        public string Path { get; }

        /// <summary>Where the call starts.</summary>
        public LinePosition At { get; }
    }

    /// <summary>
    /// Finds the <c>@emit_event</c> calls of a project, so a listener (<c>on-event="... $event.data ..."
    /// event-name="note-deleted"</c>) knows what <c>$event.data</c> holds. Files that can't emit are skipped without
    /// being analysed, and each file's calls are kept until its text changes.
    /// </summary>
    public static class EmittedEvents
    {
        private const string Command = "emit_event";

        private static readonly object Gate = new object();

        private static readonly Dictionary<string, (string Text, List<EmittedEvent> Calls)> Cache =
            new Dictionary<string, (string, List<EmittedEvent>)>(StringComparer.Ordinal);

        /// <summary>The most files whose calls are kept at once.</summary>
        internal static int CacheLimit = 512;

        /// <summary>True while a file is being analysed to read its calls, so its own listeners don't look again.</summary>
        [ThreadStatic]
        private static bool scanning;

        /// <summary>
        /// The calls that emit <paramref name="eventName"/> (any event when it is null), in the project holding
        /// <paramref name="path"/> or, outside a project, in that file. <paramref name="text"/> is the file's text as
        /// the editor has it. Empty while a scan is already running.
        /// </summary>
        public static List<EmittedEvent> Find(string path, string text, string eventName, IFileSystem files, LanguageCatalog catalog)
        {
            List<EmittedEvent> found = new List<EmittedEvent>();
            if (scanning || path == null)
            {
                return found;
            }
            string full = System.IO.Path.GetFullPath(path);
            string root = ProjectUsage.ProjectRoot(System.IO.Path.GetDirectoryName(full), files);
            IEnumerable<string> candidates = root != null ? ProjectUsage.ProjectFiles(root, recursive: true, files) : Enumerable.Empty<string>();
            foreach (string file in candidates.Select(System.IO.Path.GetFullPath).Append(full).Distinct(StringComparer.Ordinal))
            {
                string fileText = file == full ? text : files.ReadText(file);
                if (fileText == null || fileText.IndexOf(Command, StringComparison.Ordinal) < 0
                    || (eventName != null && fileText.IndexOf(eventName, StringComparison.Ordinal) < 0))
                {
                    continue;
                }
                found.AddRange(CallsIn(file, fileText, files, catalog).Where(c => eventName == null || c.Name == eventName));
            }
            return found;
        }

        /// <summary>Forgets every file's cached calls (for tests).</summary>
        internal static void ClearCache()
        {
            lock (Gate)
            {
                Cache.Clear();
            }
        }

        private static List<EmittedEvent> CallsIn(string file, string text, IFileSystem files, LanguageCatalog catalog)
        {
            lock (Gate)
            {
                if (Cache.TryGetValue(file, out var cached) && cached.Text == text)
                {
                    return cached.Calls;
                }
            }

            List<EmittedEvent> calls = new List<EmittedEvent>();
            scanning = true;
            try
            {
                DocumentAnalysis analysis = Analyzer.Analyze(text, file, files, catalog, kind: null, projectWide: false);
                TypeInference inference = new TypeInference(catalog, analysis);
                foreach (ExpressionRegion region in analysis.Regions)
                {
                    foreach (CommandCallNode call in region.Nodes.SelectMany(n => n.DescendantsAndSelf()).OfType<CommandCallNode>())
                    {
                        if (call.Name != Command || call.Arguments.Count == 0 || call.Arguments[0].Value is not LiteralNode { Kind: LiteralKind.String } name)
                        {
                            continue;
                        }
                        // @emit_event(name, data?, target?): data is the second value, or the one named data.
                        ArgumentNode data = call.Arguments.FirstOrDefault(a => a.Name == "data")
                            ?? (call.Arguments.Count > 1 && call.Arguments[1].Name == null ? call.Arguments[1] : null);
                        TypeShape shape = data != null ? inference.Infer(data.Value, region) : null;
                        calls.Add(new EmittedEvent(name.Value, shape, file, analysis.Source.GetPosition(region.Source.ToSource(call.Span).Start)));
                    }
                }
            }
            finally
            {
                scanning = false;
            }

            lock (Gate)
            {
                if (Cache.Count >= CacheLimit)
                {
                    Cache.Clear();
                }
                Cache[file] = (text, calls);
            }
            return calls;
        }
    }
}
