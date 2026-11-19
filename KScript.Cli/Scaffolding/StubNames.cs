using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace KScript.Scaffolding
{
    /// <summary>Case conversions for names given to <c>kscript make</c>: HeroBanner, hero-banner, hero_banner, …</summary>
    public static class StubNames
    {
        /// <summary>Splits "HeroBanner", "hero-banner", "hero_banner", "HTMLParser2" or "hero banner" into words.</summary>
        public static List<string> Words(string name)
        {
            var words = new List<string>();
            var current = new StringBuilder();
            string s = name ?? "";

            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (!char.IsLetterOrDigit(c))
                {
                    Flush();
                    continue;
                }

                if (current.Length > 0)
                {
                    char prev = current[current.Length - 1];
                    bool lowerToUpper = char.IsUpper(c) && !char.IsUpper(prev);
                    // The end of an acronym: "HTMLParser" splits before the P.
                    bool acronymEnd = char.IsUpper(c) && char.IsUpper(prev) && i + 1 < s.Length && char.IsLower(s[i + 1]);
                    if ((lowerToUpper && !char.IsDigit(prev)) || acronymEnd || (char.IsDigit(prev) && char.IsLetter(c) && char.IsUpper(c)))
                        Flush();
                }
                current.Append(c);
            }
            Flush();
            return words;

            void Flush()
            {
                if (current.Length > 0) words.Add(current.ToString());
                current.Clear();
            }
        }

        /// <summary>Route segments like [id] are kept exactly as written.</summary>
        private static bool IsRouteParam(string s) => s.Length > 2 && s[0] == '[' && s[^1] == ']';

        public static string Kebab(string s) => IsRouteParam(s) ? s : string.Join("-", Words(s).Select(w => w.ToLowerInvariant()));

        public static string Snake(string s) => IsRouteParam(s) ? s : string.Join("_", Words(s).Select(w => w.ToLowerInvariant()));

        public static string Pascal(string s) => IsRouteParam(s) ? s : string.Concat(Words(s).Select(Capitalise));

        public static string Camel(string s)
        {
            if (IsRouteParam(s)) return s;
            var words = Words(s);
            return string.Concat(words.Select((w, i) => i == 0 ? w.ToLowerInvariant() : Capitalise(w)));
        }

        /// <summary>"heroBanner" becomes "Hero Banner"; acronyms such as "FAQ" stay upper case.</summary>
        public static string Title(string s) =>
            IsRouteParam(s) ? s : string.Join(" ", Words(s).Select(w => w.All(char.IsUpper) && w.Length > 1 ? w : Capitalise(w)));

        /// <summary>The KScript variable an attribute becomes inside a component: sub-title is $sub_title.</summary>
        public static string Var(string s) => s.Trim().Replace('-', '_');

        private static string Capitalise(string w) =>
            w.Length == 0 ? w : char.ToUpper(w[0], CultureInfo.InvariantCulture) + w.Substring(1).ToLowerInvariant();
    }
}
