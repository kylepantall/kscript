using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace KScript.Handlers
{
    public static class KScriptParagraphHandler
    {
        /// <summary>
        /// Used to parse input as a KScript Paragraph.
        /// </summary>
        /// <param name="value">Value to parse as a KScript Paragraph</param>
        /// <returns>Formatted Paragraph</returns>
        public static string Parse(string value)
        {
            MatchCollection collection = Regex.Matches(value, Global.GlobalIdentifiers.PARAGRAPH_EXPRESSION);
            StringBuilder builder = new StringBuilder();
            collection.Cast<Match>().ToList().ForEach(item => builder.AppendLine(item.Groups[1].Value));
            return builder.ToString();
        }
    }
}
