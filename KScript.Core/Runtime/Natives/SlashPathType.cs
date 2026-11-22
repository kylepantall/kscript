using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// <c>slash_path</c>: names joined by /, such as /docs/getting-started or /My Apps/Notes. A path adds names with
    /// + (<c>/docs + 'start'</c>), and a for loop walks its names, each as a one-name path.
    /// </summary>
    public sealed class SlashPathType : NativeType
    {
        public override string Name => "slash_path";

        public override string Description => "Names joined by /, such as /docs/getting-started or /My Apps/Notes: a page, menu or category path rather than a file. It always starts with /, so home is /home. Spaces around each name, empty parts, . and a trailing / are dropped, and .. goes up one.";

        public override string Example => "/docs/getting-started";

        public override RawText RawText => RawText.Slash;

        public override IReadOnlyList<string> StringMethods => NativeTypes.Searchable.Concat(new[] { "split", "lower", "upper", "replace" }).ToArray();

        public override IReadOnlyList<string> Completions => new[] { "/" };

        public override Equality Equality => Equality.ByText;

        public override Ordering Ordering => Ordering.ByText;

        public override Iteration Iteration => Iteration.Of("slash_path", v => Segments(v).Select(name => (object)Of(new List<string> { name })));

        protected override void DeclareOperators(NativeOperators o)
        {
            const string P = "slash_path";
            o.Binary("+", P, "string", P, "The path with more names added and tidied: /docs + 'guides/../start' is /docs/start.", (a, b) => Join(a, (string)b))
             .Binary("+", P, P, P, "The second path's names added to the first: /docs + /guides is /docs/guides.", (a, b) => Join(a, ((NativeText)b).Text));
        }

        protected override void DeclareListMembers(NativeListMembers l)
        {
        }

        protected override void DeclareMembers(NativeMembers m)
        {
            const string S = KType.String, N = KType.Number, B = KType.Bool, L = KType.List;
            m.Field("segments", L, "The names in order: ['My Apps', 'Notes'] for /My Apps/Notes.", (t, _) => Segments(t).Cast<object>().ToList(), "<let name=\\\"x\\\" type=\\\"slash_path\\\" value=\\\"'/My Apps/Notes/Today'\\\" />\\n<echo>{ $x.segments }</echo>");
            m.Field("name", S, "The last name: Notes in /My Apps/Notes, or '' for /.", (t, _) => Segments(t).LastOrDefault() ?? string.Empty, "<let name=\\\"x\\\" type=\\\"slash_path\\\" value=\\\"'/My Apps/Notes/Today'\\\" />\\n<echo>{ $x.name }</echo>");
            m.Field("parent", "slash_path", "The path one up: /My Apps for /My Apps/Notes, / for /My Apps, and null for /.", (t, _) =>
            {
                List<string> parts = Segments(t);
                return parts.Count == 0 ? null : Of(parts.Take(parts.Count - 1).ToList());
            }, "<let name=\\\"x\\\" type=\\\"slash_path\\\" value=\\\"'/My Apps/Notes/Today'\\\" />\\n<echo>{ $x.parent }</echo>");
            m.Field("depth", N, "How many names it has: 2 for /My Apps/Notes, 0 for /.", (t, _) => (double)Segments(t).Count, "<let name=\\\"x\\\" type=\\\"slash_path\\\" value=\\\"'/My Apps/Notes/Today'\\\" />\\n<echo>{ $x.depth }</echo>");
            m.Field("is_root", B, "True for / itself.", (t, _) => Segments(t).Count == 0, "<let name=\\\"x\\\" type=\\\"slash_path\\\" value=\\\"'/My Apps/Notes/Today'\\\" />\\n<echo>{ $x.is_root }</echo>");
            m.Method("join", "more: string", "The path with more names added on the end and tidied: /docs joined with 'guides/../start' is /docs/start. A / at the start of more is ignored.", "slash_path", (t, c) =>
                Join(t, Values.ToStr(c.Arg(0))), "<let name=\\\"x\\\" type=\\\"slash_path\\\" value=\\\"'/My Apps/Notes/Today'\\\" />\\n<echo>{ $x->join('../Tomorrow') }</echo>");
            m.Method("is_within", "other: slash_path", "True when it is the other path or under it: /docs/guides/start is within /docs. Names are compared exactly, case included.", B, (t, c) =>
            {
                List<string> mine = Segments(t), theirs = Segments(Coerce(c.Arg(0)) ?? throw new KScriptException($"is_within needs a slash_path, such as /docs, not {Values.Describe(c.Arg(0))}.", "Type"));
                return theirs.Count <= mine.Count && theirs.SequenceEqual(mine.Take(theirs.Count), StringComparer.Ordinal);
            }, "<let name=\\\"x\\\" type=\\\"slash_path\\\" value=\\\"'/My Apps/Notes/Today'\\\" />\\n<echo>{ $x->is_within('/My Apps') }</echo>");
        }

        private static List<string> Segments(object t) => ((List<object>)((NativeText)t).Part("segments")).Cast<string>().ToList();

        private object Join(object path, string more)
        {
            (object joined, string why) = Parse(((NativeText)path).Text + "/" + more);
            return joined ?? throw new KScriptException($"{((NativeText)path).Text} + '{more}' isn't a slash_path: {why}.", "Value");
        }

        /// <summary>
        /// Names joined by /: /docs/getting-started or /My Apps/Notes. It always starts with /, so home is /home and '' is /.
        /// Spaces around each name and runs of spaces inside one are tidied, empty parts (//) and . are dropped,
        /// .. goes up one (never above /), and a trailing / is dropped. Case is kept.
        /// </summary>
        public override (object Value, string Reason) Parse(string s)
        {
            s = s.Trim();
            if (s.Contains('\\')) return (null, "names are joined by /, not \\");
            if (s.Any(char.IsControl)) return (null, "it has a control character, such as a new line or a tab, in it");
            var parts = new List<string>();
            foreach (string raw in s.Split('/'))
            {
                string part = string.Join(' ', raw.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                if (part.Length == 0 || part == ".") continue;
                if (part == "..")
                {
                    if (parts.Count == 0) return (null, "its .. goes above /");
                    parts.RemoveAt(parts.Count - 1);
                    continue;
                }
                parts.Add(part);
            }
            return (Of(parts), null);
        }

        internal static NativeText Of(List<string> parts) =>
            new NativeText("slash_path", "/" + string.Join("/", parts), new Dictionary<string, object> { ["segments"] = parts.Cast<object>().ToList() });
    }
}
