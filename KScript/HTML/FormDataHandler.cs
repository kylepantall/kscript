using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace KScript.HTML
{
    public class FormDataHandler
    {

        public static Dictionary<string, string> GetFormData(string input)
        {
            Dictionary<string, string> values = new Dictionary<string, string>();

            Regex reg = new Regex(@"([^&]+)\=([^&]+)");
            MatchCollection matches = reg.Matches(input);

            foreach (Match m in matches)
            {
                var key = m.Groups[1].Value;
                var value = m.Groups[2].Value;
                values.Add(key, value);
            }

            return values;
        }

    }
}
