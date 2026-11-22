using KScript.Syntax;

namespace KScript.Language.Features
{
    /// <summary>
    /// The order suggestions are offered in: what a script uses every day comes first, the rest follows by name. The
    /// lists are short on purpose, so the first screen of suggestions is the one a learner needs.
    /// </summary>
    internal static class Ranking
    {
        /// <summary>The statements most scripts are made of, in the order they are offered on an empty line.</summary>
        internal static readonly string[] Statements = { "echo", "if", "for", "fn", "while", "return", "match", "try", "import", "const" };

        /// <summary>In a test file, the test statements come before those.</summary>
        internal static readonly string[] TestStatements = { "test", "expect", "describe", "expect_error" };

        /// <summary>The methods reached for first on each type of value.</summary>
        internal static readonly Dictionary<string, string[]> Methods = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [KType.String] = new[] { "length", "upper", "lower", "trim", "contains", "replace", "split", "starts_with", "ends_with", "slice" },
            [KType.List] = new[] { "count", "map", "filter", "first", "last", "join", "sort", "push", "contains", "each" },
            [KType.Map] = new[] { "keys", "values", "has", "get", "set", "merge" },
            [KType.Number] = new[] { "round", "format", "abs", "floor", "ceil" },
            [KType.Date] = new[] { "format", "add_days", "add_months", "iso" },
        };

        /// <summary>A sort key that puts the names of <paramref name="common"/> first, in its order, and the rest after by name.</summary>
        internal static string Key(IReadOnlyList<string> common, string name)
        {
            int index = common == null ? -1 : IndexOf(common, name);
            return (index >= 0 ? index.ToString("D2") : "50") + name;
        }

        internal static string Statement(string name, bool testFile)
        {
            if (testFile && IndexOf(TestStatements, name) is int test and >= 0)
            {
                return "0" + test + name;
            }
            return "1" + Key(Statements, name);
        }

        internal static string Method(string type, string name) => Key(Methods.TryGetValue(type ?? string.Empty, out string[] common) ? common : null, name);

        private static int IndexOf(IReadOnlyList<string> names, string name)
        {
            for (int i = 0; i < names.Count; i++)
            {
                if (names[i] == name)
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
