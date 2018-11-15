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
        public const string TRALING_STRING = "   ";
        public static bool IsVariable(string val) => Regex.IsMatch(val, @"\$\b\S+\b");
        public static string ReturnFormattedVariables(KScriptContainer ParentContainer, string Contents)
        {
            string temp_string = string.Format("{0}{1}", TRALING_STRING, Contents);
            ParentContainer.defs.ToList().ForEach(item => temp_string = Regex.Replace(temp_string, string.Format(@"\$\b{0}\b", item.Key), item.Value.Contents, RegexOptions.IgnoreCase));
            return temp_string.Substring(TRALING_STRING.Length);
        }
    }
}
