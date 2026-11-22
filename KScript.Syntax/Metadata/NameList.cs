using System;
using System.Collections.Generic;
using System.Linq;

namespace KScript.Metadata
{
    /// <summary>
    /// Reads a list of names written in an attribute: an array literal, <c>[log, cors, throttle:30]</c> (the usual
    /// form in .kscript files), or the older comma separated text, <c>"log, cors, throttle:30"</c>. Both mean the same.
    /// </summary>
    public static class NameList
    {
        /// <summary>Each name with its offset in <paramref name="text"/>, without brackets, commas or spaces around it.</summary>
        public static IEnumerable<(string Name, int Start)> Split(string text)
        {
            if (string.IsNullOrEmpty(text)) yield break;
            int i = 0;
            while (i < text.Length)
            {
                while (i < text.Length && IsSeparator(text[i]))
                {
                    i++;
                }
                int start = i;
                char quote = '\0';
                while (i < text.Length && (quote != '\0' || (text[i] != ',' && text[i] != '[' && text[i] != ']')))
                {
                    if (quote != '\0') { if (text[i] == quote) quote = '\0'; }
                    else if (text[i] == '\'' || text[i] == '"') quote = text[i];
                    i++;
                }
                string name = text.Substring(start, i - start).TrimEnd();
                if (name.Length >= 2 && (name[0] == '\'' || name[0] == '"') && name[name.Length - 1] == name[0])
                {
                    // ['paper.pdf', 'paper.docx']: text in a list is quoted, and the name is what is inside.
                    name = name.Substring(1, name.Length - 2);
                    start++;
                }
                if (name.Length > 0) yield return (name, start);
            }
        }

        /// <summary>The names alone.</summary>
        public static List<string> Names(string text) => Split(text).Select(n => n.Name).ToList();

        /// <summary>True when the text is written as an array literal: <c>[a, b]</c>.</summary>
        public static bool IsArray(string text)
        {
            string t = text?.Trim() ?? string.Empty;
            return t.StartsWith("[", StringComparison.Ordinal) && t.EndsWith("]", StringComparison.Ordinal);
        }

        private static bool IsSeparator(char c) => char.IsWhiteSpace(c) || c == ',' || c == '[' || c == ']';
    }
}
