using System;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace KScript.Handlers
{
    [ClassInterface(ClassInterfaceType.None)]
    public class KScriptStringHandler
    {
        private KScriptContainer ParentContainer { get; }
        private KScriptStringHandler() { }
        public KScriptStringHandler(KScriptContainer container) => ParentContainer = container;

        /// <summary>
        /// Removes one pair of surrounding single quotes, e.g. 'text' becomes text.
        /// Strings such as 'a' and 'b' are left alone, since the quotes do not surround the whole value.
        /// </summary>
        public static string StripSurroundingQuotes(string value)
        {
            if (value != null && value.Length >= 2 && value[0] == '\'' && value[value.Length - 1] == '\''
                && value.IndexOf('\'', 1, value.Length - 2) < 0)
            {
                return value.Substring(1, value.Length - 2);
            }
            return value;
        }

        public string Format(string val, params string[] args)
        {
            // Only apply string.Format when arguments are supplied - otherwise literal braces in scripts would throw.
            string tmp_string = args != null && args.Length > 0 ? string.Format(val, args) : val;
            tmp_string = Regex.Replace(tmp_string, @"\\n", Environment.NewLine);

            tmp_string = StripSurroundingQuotes(tmp_string);

            tmp_string = KScriptVariableHandler.ReturnFormattedVariables(ParentContainer, tmp_string);
            return tmp_string;
        }

        public string Format(string val, bool ignore_value_conditions = false)
        {
            if (!string.IsNullOrEmpty(val))
            {
                string tmp_string = val;

                tmp_string = Regex.Replace(tmp_string, @"\\n", Environment.NewLine);

                tmp_string = StripSurroundingQuotes(tmp_string);

                tmp_string = KScriptVariableHandler.ReturnFormattedVariables(ParentContainer, tmp_string);
                return tmp_string;
            }
            return val;
        }
    }
}
