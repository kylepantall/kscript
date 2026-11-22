using System.Globalization;

using KScript.Projects;
using KScript.Runtime;

namespace KScript.Queueing
{
    /// <summary>Lengths of time in job options and dispatch arguments: durations such as 30s, 5m, 1h, 2d (a plain number is seconds).</summary>
    internal static class Durations
    {
        /// <summary>Seconds in a duration, in text such as '30s', or in a plain number. Throws for anything else.</summary>
        public static double Seconds(object value, string what)
        {
            // One reader for every length of time (KScript.Runtime.Duration): 30s, 1h30m, text that reads as one, or seconds.
            object given = value is string quoted ? Unquote(quoted) : value;
            double seconds = Duration.Seconds(given, what);
            if (seconds < 0) throw new KScriptException($"{what} should be a length of time of 0 or more, such as 30s or 5m, not {Values.ToStr(value)}.", "Argument");
            return seconds;
        }

        /// <summary>A short way to write seconds: 45s, 10m, 2h, 3d.</summary>
        public static string Describe(double seconds)
        {
            if (seconds < 1) return seconds.ToString("0.##", CultureInfo.InvariantCulture) + "s";
            if (seconds < 120) return Math.Round(seconds).ToString(CultureInfo.InvariantCulture) + "s";
            if (seconds < 7200) return Math.Round(seconds / 60).ToString(CultureInfo.InvariantCulture) + "m";
            if (seconds < 172800) return Math.Round(seconds / 3600).ToString(CultureInfo.InvariantCulture) + "h";
            return Math.Round(seconds / 86400).ToString(CultureInfo.InvariantCulture) + "d";
        }

        /// <summary>The items of a list written as text: <c>[a, 'b']</c>, <c>a, b</c> or <c>a | b</c>.</summary>
        public static List<string> Items(string text)
        {
            if (text == null) return new List<string>();
            string t = text.Trim();
            if (t.StartsWith('[') && t.EndsWith(']')) t = t.Substring(1, t.Length - 2);
            return t.Split(',', '|').Select(i => Unquote(i.Trim())).Where(i => i.Length > 0).ToList();
        }

        /// <summary>Text without the quotes around it: 'mail' and "mail" are mail.</summary>
        public static string Unquote(string text)
        {
            string t = text?.Trim() ?? string.Empty;
            if (t.Length >= 2 && ((t[0] == '\'' && t[^1] == '\'') || (t[0] == '"' && t[^1] == '"'))) return t.Substring(1, t.Length - 2);
            return t;
        }
    }
}
