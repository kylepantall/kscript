namespace KScript.Syntax.Text
{
    /// <summary>
    /// How the text between an XML element's tags is laid out before it is used: trimmed, and without the
    /// indentation its lines share. Quoted text in a .kscript file is never laid out; it is used as written.
    /// </summary>
    public static class ElementText
    {
        public static string Tidy(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            int start = 0, end = lines.Length - 1;
            while (start <= end && lines[start].Trim().Length == 0) start++;
            while (end >= start && lines[end].Trim().Length == 0) end--;
            if (start > end) return string.Empty;
            if (start == end) return lines[start].Trim();

            // The first line usually follows the opening tag directly, so it isn't counted for the indent.
            int indent = int.MaxValue;
            for (int i = start + 1; i <= end; i++)
            {
                if (lines[i].Trim().Length == 0) continue;
                indent = Math.Min(indent, lines[i].Length - lines[i].TrimStart().Length);
            }
            if (indent == int.MaxValue) indent = 0;
            var result = new List<string> { lines[start].Trim() };
            for (int i = start + 1; i <= end; i++)
            {
                string line = lines[i];
                result.Add((line.Length >= indent ? line.Substring(indent) : line.TrimStart()).TrimEnd());
            }
            return string.Join("\n", result);
        }
    }
}
