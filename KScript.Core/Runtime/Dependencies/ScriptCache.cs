using KScript.Syntax;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Runtime.Dependencies
{
    /// <summary>
    /// Parsed scripts shared by the interpreters of one server, so a request doesn't parse every script again: a
    /// script is parsed once, and again only when its text changes. Each interpreter still makes its own module (with
    /// its own variables, imports and checks) from the shared parse, so a changed file is picked up by the next
    /// request, and so are the files that import it, without parsing those again. Set it as
    /// <see cref="InterpreterOptions.Scripts"/>.
    /// </summary>
    public sealed class ScriptCache
    {
        /// <summary>One script's parse and the caches made from it while it was checked.</summary>
        internal sealed class Entry
        {
            internal string Text { get; init; }
            internal SourceText Source { get; init; }
            internal MarkupDocument Document { get; init; }
            internal Dictionary<object, ParseResult> ParseCache { get; init; }
            internal Dictionary<object, Template> TemplateCache { get; init; }
            internal Dictionary<object, OldSpelling> OldSpellingCache { get; init; }
            internal Dictionary<MarkupElement, DecodedText> ContentCache { get; init; }
        }

        /// <summary>The cache kscript serve's requests share: pages, route files, controllers and the scripts they import.</summary>
        public static ScriptCache Server { get; } = new ScriptCache();

        private readonly object gate = new object();
        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        /// <summary>The most scripts kept; past it the cache starts again.</summary>
        public int Limit { get; init; } = 4000;

        /// <summary>How many times a script was parsed (rather than taken from the cache).</summary>
        public int Parses { get; private set; }

        /// <summary>How many times a script was taken from the cache.</summary>
        public int Hits { get; private set; }

        /// <summary>The parse of the script at <paramref name="path"/>, if it was made from this text.</summary>
        internal Entry Get(string path, string text)
        {
            lock (gate)
            {
                if (entries.TryGetValue(path, out Entry entry) && string.Equals(entry.Text, text, StringComparison.Ordinal))
                {
                    Hits++;
                    return entry;
                }
                Parses++;
                return null;
            }
        }

        /// <summary>Keeps a script that loaded and checked cleanly, so the next interpreter can use its parse.</summary>
        internal void Add(string path, string text, Module module)
        {
            var entry = new Entry
            {
                Text = text,
                Source = module.Source,
                Document = module.Document,
                ParseCache = module.ParseCache,
                TemplateCache = module.TemplateCache,
                OldSpellingCache = module.OldSpellingCache,
                ContentCache = module.ContentCache,
            };
            lock (gate)
            {
                if (entries.Count >= Limit && !entries.ContainsKey(path)) entries.Clear();
                entries[path] = entry;
            }
        }

        /// <summary>Forgets every script.</summary>
        public void Clear()
        {
            lock (gate)
            {
                entries.Clear();
            }
        }
    }
}
