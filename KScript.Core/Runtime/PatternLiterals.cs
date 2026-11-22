namespace KScript.Runtime
{
    /// <summary>Selects a refinement for an unquoted word using its declared pattern.</summary>
    internal static class PatternLiterals
    {
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
