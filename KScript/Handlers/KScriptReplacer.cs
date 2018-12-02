using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace KScript.Handlers
{
    public class KScriptReplacer
    {
        /// <summary>
        /// Used to replace values using KScript Replacer variables - e.g. $myKey replace with myvalue
        /// </summary>
        /// <param name="input">String to validate and parse.</param>
        /// <param name="keyValuePairs">Keys and values to use for replacing.</param>
        /// <returns>String with replaced keys with values.</returns>
        public static string Replace(string input, params KeyValuePair<string, string>[] keyValuePairs)
        {
            string temp = input;
            string key_expression = @"\$\b{0}\b";
            keyValuePairs.ToList().ForEach(i => temp = Regex.Replace(temp, string.Format(key_expression, i.Key), i.Value));
            return temp;
        }
    }
}
