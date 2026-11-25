using System.Text;

namespace KScript.Documents.Text
{
    /// <summary>
    /// The characters XML 1.0 can hold. Text from a log or a spreadsheet can carry others (an escape character, a
    /// vertical tab), and one of them makes a .docx that Word won't open.
    /// </summary>
    internal static class XmlChars
    {
        /// <summary>The text without the characters an XML or HTML file can't hold.</summary>
        public static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
            StringBuilder cleaned = null;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                bool pair = char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]);
                bool ok = pair || c == '\t' || c == '\n' || c == '\r' || (c >= 0x20 && c <= 0xD7FF) || (c >= 0xE000 && c <= 0xFFFD);
                if (!ok)
                {
                    cleaned ??= new StringBuilder(text, 0, i, text.Length);
                    continue;
                }
                if (cleaned != null)
                {
                    cleaned.Append(c);
                    if (pair) cleaned.Append(text[i + 1]);
                }
                if (pair) i++;
            }
            return cleaned?.ToString() ?? text;
        }
    }
}
