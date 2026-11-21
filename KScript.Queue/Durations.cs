using System.Globalization;

using KScript.Projects;
using KScript.Runtime;

namespace KScript.Queueing
{
    /// <summary>Lengths of time in job options and dispatch arguments: seconds, or text such as '30s', '5m', '1h', '2d'.</summary>
    internal static class Durations
    {
        /// <summary>Seconds in a number or in text such as '90', '30s', '5m'. Throws for anything else.</summary>
        public static double Seconds(object value, string what)
        {
            if (value is double d && d >= 0 && !double.IsNaN(d) && !double.IsInfinity(d)) return d;
            string text = Unquote(Values.ToStr(value));
            if (text == "0") return 0;
            if (ProjectConfig.Seconds(text) is double seconds) return seconds;
            throw new KScriptException($"{what} should be a length of time: seconds (30) or text such as '30s', '5m', '1h' or '2d', not '{Values.ToStr(value)}'.", "Argument");
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
