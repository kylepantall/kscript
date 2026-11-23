using KScript.Syntax;

namespace KScript.Runtime
{
    /// <summary>Selects a refinement for an unquoted word using its declared pattern.</summary>
    internal static class PatternLiterals
    {
        /// <summary>
        /// True for the roots a pattern type reads words for: string, and the native types written as words
        /// (<c>type CorpHost with [domain] { pattern: ... }</c>). Types with a literal form of their own (an email, a URL,
        /// a UUID, a regex, a slash path, a duration), handles, passwords and numbers keep their own forms.
        /// </summary>
        public static bool ReadsWords(string root) =>
            root == "string" || (root != null && !OwnLiteralForms.Contains(root) && KScript.Metadata.NativeTypes.Get(root)?.RawKind == "word");

        private static readonly HashSet<string> OwnLiteralForms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "email", "url", "uuid", "regex", "slash_path", "duration", "password", "handle", "port",
        };

        /// <summary>
        /// The word a run of bare words is, or null: names and numbers joined by <c>-</c>, <c>+</c>, <c>/</c> or
        /// <c>.</c> with no spaces (<c>my-post</c>, <c>api.corp.com</c>, <c>1.4.2</c>), where every name could be a
        /// literal (<paramref name="free"/>). A single name is an <see cref="IdentifierNode"/> and is read where it is
        /// evaluated. Arithmetic on numbers alone (<c>1+2</c>) is never a word. <paramref name="source"/> is the text the
        /// node's span is in.
        /// </summary>
        public static string WordOf(ExprNode node, string source, Func<string, bool> free)
        {
            if (node is not (BinaryNode { Operator: "-" or "+" or "/" or "." } or MemberNode { NullSafe: false })) return null;
            if (!Interpreter.IsRawWord(node, "word", null, null)) return null;
            List<IdentifierNode> names = node.DescendantsAndSelf().OfType<IdentifierNode>().ToList();
            bool dottedNumbers = names.Count == 0 && node is BinaryNode { Operator: "." };
            if (!dottedNumbers && (names.Count == 0 || !names.All(n => free(n.Name)))) return null;
            if (source == null || node.Span.Start < 0 || node.Span.End > source.Length) return null;
            string word = source.Substring(node.Span.Start, node.Span.Length);
            return word.Any(char.IsWhiteSpace) ? null : word;
        }

        public static T Resolve<T>(string word, IEnumerable<(T Type, string Name, RegexValue Pattern)> candidates) where T : class
        {
            T first = null;
            string firstName = null;
            List<string> conflicts = null;
            foreach (var candidate in candidates)
            {
                if (candidate.Pattern == null || !candidate.Pattern.MatchesWholeWord(word)) continue;
                if (first == null)
                {
                    first = candidate.Type;
                    firstName = candidate.Name;
                    continue;
                }

                conflicts ??= new List<string> { firstName };
                conflicts.Add(candidate.Name);
            }

            if (conflicts == null) return first;
            conflicts.Sort(StringComparer.Ordinal);
            throw new KScriptException($"'{word}' matches more than one literal type: {string.Join(", ", conflicts)}. "
                + $"Choose a type explicitly, for example '{word}' as {conflicts[0]}.", "Type");
        }
    }
}
