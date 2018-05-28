using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace KScript
{
    public static class KScriptVariableHandler
    {
        public static string ReturnFormattedVariables(KScriptContainer ParentContainer, string contents)
        {
            string trailing_str = "   ";
            contents = trailing_str + contents;
            string pattern = @"\$\w+";
            string temp_string = string.Format("{0}", contents);
            Regex regex = new Regex(pattern, RegexOptions.Multiline);
            foreach (var item in ParentContainer.defs)
            {
                temp_string = temp_string.Replace("$" + item.Key, item.Value.contents);
            }
            return temp_string.Substring(trailing_str.Length);
        }

        public static bool IsVariable(string val) => val.StartsWith("$");
    }
}
