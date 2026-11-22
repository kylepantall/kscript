using System.Text.RegularExpressions;

using KScript.Syntax;

namespace KScript.Runtime
{
    /// <summary>
    /// Which declared type a bare word is, where no type is declared for it: <c>key = passkey_abc</c> for
    /// <c>type PasskeySessionKey with [string] { pattern: /^passkey_[a-z]+$/ }</c>. A word that names nothing
    /// (not a variable, function, type or import) is read as the one type built on text whose own <c>pattern</c>
    /// matches the whole word. The type's other rules (normalize, min, validate) then check it as usual. Built-in
    /// types aren't guessed: their values overlap too much (GB is a country and a locale, every a.b looks like a
    /// domain), so they still need a declared type (<c>v: semver = 1.4.2</c>).
    /// The runtime and the editor both decide here, from the type names and pattern text each can see.
    /// </summary>
    public static class BareWordTypes
    {
        /// <summary>A type that could take bare words: its name and the pattern it declares itself (<c>/^a+$/i</c> or plain text).</summary>
        public readonly record struct Candidate(string Name, string Pattern);

        private static readonly Dictionary<string, Regex> Whole = new Dictionary<string, Regex>(StringComparer.Ordinal);

        /// <summary>True if <paramref name="pattern"/> matches all of <paramref name="word"/>, not only part of it.</summary>
        public static bool Matches(string pattern, string word)
        {
            if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(word)) return false;
            Regex whole;
            lock (Whole)
            {
                if (!Whole.TryGetValue(pattern, out whole))
                {
                    whole = RegexValue.TryParse(pattern.Trim(), out RegexValue parsed, out _)
                        ? new Regex(@"\A(?:" + parsed.Regex + @")\z", parsed.Regex.Options, TimeSpan.FromMilliseconds(250))
                        : null;
                    Whole[pattern] = whole;
                }
            }
            if (whole == null) return false;
            try
            {
                return whole.IsMatch(word);
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }

        /// <summary>The names of the candidates whose pattern matches all of <paramref name="word"/>, each name once, in order.</summary>
        public static List<string> Matching(string word, IEnumerable<Candidate> candidates) =>
            candidates.Where(c => Matches(c.Pattern, word)).Select(c => c.Name).Distinct(StringComparer.Ordinal).ToList();

        /// <summary>
        /// The bare word <paramref name="node"/> is, or null: a name (<c>passkey_abc</c>), or words and numbers joined
        /// by <c>-</c>, <c>+</c>, <c>/</c> or <c>.</c> with no spaces (<c>my-post</c>, <c>corp.example.com</c>,
        /// <c>1.4.2</c>), where no part is a name <paramref name="known"/> says means something. Arithmetic on numbers
        /// alone (<c>1+2</c>) is never a word. <paramref name="source"/> is the text the node's span is in.
        /// </summary>
        public static string WordOf(ExprNode node, string source, Func<string, bool> known)
        {
            if (node is IdentifierNode id) return id.Name == "_" || known(id.Name) ? null : id.Name;
            if (node is not (BinaryNode { Operator: "-" or "+" or "/" or "." } or MemberNode { NullSafe: false })) return null;
            if (!Interpreter.IsRawWord(node, "word", null, null)) return null;
            List<IdentifierNode> names = node.DescendantsAndSelf().OfType<IdentifierNode>().ToList();
            bool dottedNumbers = names.Count == 0 && node is BinaryNode { Operator: "." };
            if (!dottedNumbers && (names.Count == 0 || names.Any(n => known(n.Name)))) return null;
            if (source == null || node.Span.Start < 0 || node.Span.End > source.Length) return null;
            string word = source.Substring(node.Span.Start, node.Span.Length);
            return word.Any(char.IsWhiteSpace) ? null : word;
        }

        /// <summary>The problem when a bare word fits more than one type.</summary>
        public static string Ambiguous(string word, IReadOnlyList<string> types) =>
            $"{word} fits more than one type ({string.Join(" and ", types)}), so KScript can't tell which you mean. "
            + $"Declare it, as in key: {types[0]} = {word}, or convert it: '{word}' as {types[0]}.";

        /// <summary>
        /// The candidates in <paramref name="scope"/> at run time: each refinement type built on text that declares a
        /// pattern itself, nearest scope first (a script's own type hides a project type of the same name).
        /// </summary>
        internal static List<Candidate> InScope(Scope scope)
        {
            var found = new List<Candidate>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (Scope s = scope; s != null; s = s.Parent)
            {
                foreach (KeyValuePair<string, object> v in s.OwnVariables)
                {
                    if (!seen.Add(v.Key)) continue;
                    if (v.Value is RefinedType refined && refined.Rules.PatternText is string pattern && ReadsWords(refined))
                        found.Add(new Candidate(refined.Name, pattern));
                }
            }
            return found;
        }

        private static bool ReadsWords(RefinedType type)
        {
            string root = type.Root();
            return string.Equals(root, "string", StringComparison.OrdinalIgnoreCase)
                || (KScript.Metadata.NativeTypes.Get(root)?.RawKind == "word");
        }
    }
}
