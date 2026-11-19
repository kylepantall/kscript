namespace KScript.Language.Analysis
{
    /// <summary>"Did you mean" suggestions by edit distance.</summary>
    public static class Suggestions
    {
        public static string Closest(string name, IEnumerable<string> candidates)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            int limit = Math.Max(1, Math.Min(3, name.Length / 3));
            string best = null;
            int bestDistance = int.MaxValue;
            foreach (string candidate in candidates.Distinct(StringComparer.Ordinal))
            {
                if (string.Equals(candidate, name, StringComparison.Ordinal))
                {
                    continue;
                }
                int d = string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase) ? 0 : Distance(name.ToLowerInvariant(), candidate.ToLowerInvariant());
                if (d <= limit && d < bestDistance)
                {
                    best = candidate;
                    bestDistance = d;
                }
            }
            return best;
        }

        /// <summary>Damerau-Levenshtein (optimal string alignment) distance, so swapped letters ("ehco") count as one edit.</summary>
        public static int Distance(string a, string b)
        {
            int[,] d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                    if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    {
                        d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
                    }
                }
            }
            return d[a.Length, b.Length];
        }
    }
}
