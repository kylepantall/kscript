namespace KScript.Runtime
{
    /// <summary>
    /// A regular expression, written <c>/^[a-z]+$/i</c>. Text converts to one too: <c>'^[a-z]+$'</c>, or
    /// <c>'/^[a-z]+$/i'</c> with flags. Flags: i (ignore case), m (^ and $ at each line), s (. matches new lines)
    /// and x (spaces and # comments in the pattern are ignored).
    /// </summary>
    public sealed class RegexValue : ISemanticText
    {
        private RegexValue(string pattern, string flags, System.Text.RegularExpressions.Regex regex)
        {
            Pattern = pattern;
            Flags = flags;
            Regex = regex;
        }

        public string Pattern { get; }
        public string Flags { get; }
        public System.Text.RegularExpressions.Regex Regex { get; }

        private System.Text.RegularExpressions.Regex wholeWord;

        internal bool MatchesWholeWord(string word) =>
            Run(this, regex => (wholeWord ??= WholeWordPattern(regex)).IsMatch(word));

        private static System.Text.RegularExpressions.Regex WholeWordPattern(System.Text.RegularExpressions.Regex regex)
        {
            // Anchor outside the alternatives, so /a|abc/ can match the entire word "abc".
            string start = @"\A(?:" + regex;
            try
            {
                return new System.Text.RegularExpressions.Regex(start + @")\z", regex.Options, regex.MatchTimeout);
            }
            catch (System.Text.RegularExpressions.RegexParseException error)
                when (error.Error == System.Text.RegularExpressions.RegexParseError.InsufficientClosingParentheses)
            {
                // In x mode a final # comment can consume the closing group we just appended.
                return new System.Text.RegularExpressions.Regex(start + "\n" + @")\z", regex.Options, regex.MatchTimeout);
            }
        }

        /// <summary>The literal: /pattern/flags.</summary>
        public string Text => "/" + Pattern + "/" + Flags;
        public string TypeName => SemanticTypes.Regex;

        /// <summary>
        /// The regex an argument stands for: a regex value (/^a+$/i, with its flags), or text that is a pattern. Text
        /// that isn't a valid pattern fails with the reason.
        /// </summary>
        public static RegexValue From(object pattern, string where)
        {
            if (Values.Unwrap(pattern) is RegexValue regex) return regex;
            string text = Values.ToStr(pattern);
            if (TryParse(text, out regex, out string reason)) return regex;
            throw new KScriptException($"'{text}' isn't a regular expression for {where}: {reason}.", "Value");
        }

        /// <summary>Runs a match; one that takes too long (catastrophic backtracking) stops with an error instead of hanging.</summary>
        public static T Run<T>(RegexValue regex, Func<System.Text.RegularExpressions.Regex, T> work)
        {
            try
            {
                return work(regex.Regex);
            }
            catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
            {
                throw new KScriptException($"The regular expression {regex.Text} took longer than a second to match, so it was stopped. Simplify it, such as by removing nested repeats like (a+)+.", "Timeout");
            }
        }

        /// <summary>A literal <c>/pattern/flags</c>, or a bare pattern.</summary>
        public static bool TryParse(string text, out RegexValue regex, out string reason)
        {
            regex = null;
            string s = text ?? string.Empty;
            string pattern = s;
            string flags = string.Empty;
            int last = s.LastIndexOf('/');
            if (s.Length >= 2 && s[0] == '/' && last > 0 && s.Substring(last + 1).All(char.IsAsciiLetterLower))
            {
                pattern = s.Substring(1, last - 1);
                flags = s.Substring(last + 1);
            }
            var options = System.Text.RegularExpressions.RegexOptions.CultureInvariant;
            foreach (char f in flags)
            {
                switch (f)
                {
                    case 'i': options |= System.Text.RegularExpressions.RegexOptions.IgnoreCase; break;
                    case 'm': options |= System.Text.RegularExpressions.RegexOptions.Multiline; break;
                    case 's': options |= System.Text.RegularExpressions.RegexOptions.Singleline; break;
                    case 'x': options |= System.Text.RegularExpressions.RegexOptions.IgnorePatternWhitespace; break;
                    default:
                        reason = $"'{f}' isn't a regex flag (use i, m, s and x)";
                        return false;
                }
            }
            if (flags.Distinct().Count() != flags.Length)
            {
                reason = "a flag is given twice";
                return false;
            }
            try
            {
                regex = new RegexValue(pattern, flags, new System.Text.RegularExpressions.Regex(pattern, options, TimeSpan.FromSeconds(1)));
                reason = null;
                return true;
            }
            catch (ArgumentException ex)
            {
                reason = ex.Message.Replace($"Invalid pattern '{pattern}' at offset", "the pattern is invalid at character").TrimEnd('.');
                return false;
            }
        }

        /// <summary>
        /// The capturing groups in the order they open in the pattern. .NET numbers unnamed groups before named
        /// ones, so captures would otherwise come out of order when the two are mixed.
        /// </summary>
        internal static List<string> GroupOrder(System.Text.RegularExpressions.Regex regex)
        {
            string pattern = regex.ToString();
            bool explicitOnly = (regex.Options & System.Text.RegularExpressions.RegexOptions.ExplicitCapture) != 0;
            var order = new List<string>();
            int unnamed = 0;
            bool inClass = false;
            for (int i = 0; i < pattern.Length; i++)
            {
                char c = pattern[i];
                if (c == '\\') { i++; continue; }
                if (inClass) { inClass = c != ']'; continue; }
                if (c == '[') { inClass = true; continue; }
                if (c != '(') continue;
                if (i + 1 < pattern.Length && pattern[i + 1] == '?')
                {
                    // (?<name>...) or (?'name'...) is a named group; (?<= and (?<! look behind.
                    if (i + 2 < pattern.Length && (pattern[i + 2] == '<' || pattern[i + 2] == '\'') && i + 3 < pattern.Length && pattern[i + 3] != '=' && pattern[i + 3] != '!')
                    {
                        char close = pattern[i + 2] == '<' ? '>' : '\'';
                        int end = pattern.IndexOf(close, i + 3);
                        if (end > 0) order.Add(pattern.Substring(i + 3, end - i - 3));
                    }
                    continue;
                }
                if (!explicitOnly) order.Add((++unnamed).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            order.RemoveAll(k => regex.GroupNumberFromName(k) < 0);
            // Anything the scan missed keeps .NET's order at the end.
            foreach (int n in regex.GetGroupNumbers().Where(n => n > 0))
            {
                string key = regex.GroupNameFromNumber(n);
                if (!order.Contains(key)) order.Add(key);
            }
            return order;
        }

        /// <summary>A match as a map: text, index, groups (by name) and captures (by number, 1 up).</summary>
        public static Dictionary<string, object> MatchMap(System.Text.RegularExpressions.Regex regex, System.Text.RegularExpressions.Match m)
        {
            var groups = new Dictionary<string, object>(StringComparer.Ordinal);
            var captures = new List<object>();
            foreach (string name in regex.GetGroupNames().Where(n => !int.TryParse(n, out _)))
            {
                groups[name] = m.Groups[name].Success ? m.Groups[name].Value : null;
            }
            foreach (string key in GroupOrder(regex))
            {
                System.Text.RegularExpressions.Group g = m.Groups[key];
                captures.Add(g.Success ? g.Value : null);
            }
            return new Dictionary<string, object>
            {
                ["text"] = m.Value,
                ["index"] = (double)m.Index,
                ["groups"] = groups,
                ["captures"] = captures,
            };
        }

        public bool TryGetMember(string name, out object value)
        {
            value = null;
            return false;
        }

        public bool TryCallMethod(CallContext call, string name, out object result)
        {
            result = null;
            return false;
        }

        public string ToText() => Text;

        public override bool Equals(object obj) => obj is RegexValue r && r.Text == Text;

        public override int GetHashCode() => Text.GetHashCode(StringComparison.Ordinal);

        public override string ToString() => Text;
    }
}
