using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace KScript
{
    [ClassInterface(ClassInterfaceType.None)]
    public static class KScriptVariableHandler
    {
        public const string TRALING_STRING = "   ";

        public static bool IsVariable(string val) => Regex.IsMatch(val, Global.GlobalIdentifiers.VARIABLE_NO_POINTERS) || Regex.IsMatch(val, Global.GlobalIdentifiers.VARIABLE_POINTERS);



        public static string ReturnFormattedVariables(KScriptContainer ParentContainer, string Contents)
        {
            string temp_string = string.Format("{0}{1}", TRALING_STRING, Contents);


            //if (Regex.IsMatch(Contents))
            ParentContainer.defs.ToList().ForEach(item => temp_string = Regex.Replace(temp_string, string.Format(@"\$\b{0}\b", item.Key), item.Value.Contents, RegexOptions.IgnoreCase));




            return temp_string.Substring(TRALING_STRING.Length);
        }
    }
}
