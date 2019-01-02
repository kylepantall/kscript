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

        public string Format(string val, params string[] args)
        {
            string tmp_string = val;
            tmp_string = string.Format(val, args);
            tmp_string = Regex.Replace(tmp_string, @"\\n", Environment.NewLine);

            if (tmp_string.StartsWith("'") && tmp_string.EndsWith("'"))
            {
                tmp_string = tmp_string.Substring(1, tmp_string.Length - 1);
            }

            tmp_string = KScriptVariableHandler.ReturnFormattedVariables(ParentContainer, tmp_string);
            return tmp_string;
        }

        public string Format(string val, bool ignore_value_conditions = false)
        {
            if (!string.IsNullOrEmpty(val))
            {
                string tmp_string = val;

                tmp_string = Regex.Replace(tmp_string, @"\\n", Environment.NewLine);

                if (tmp_string.StartsWith("'") && tmp_string.EndsWith("'"))
                {
                    tmp_string = tmp_string.Substring(1, tmp_string.Length - 1);
                }

                tmp_string = KScriptVariableHandler.ReturnFormattedVariables(ParentContainer, tmp_string);
                return tmp_string;
            }
            else
            {
                return string.Empty;
            }
        }
    }
}
