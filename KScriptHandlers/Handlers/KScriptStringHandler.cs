using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace KScript.Handlers
{
    public class KScriptStringHandler
    {
        private KScriptContainer ParentContainer { get; }
        private KScriptStringHandler() { }
        public KScriptStringHandler(KScriptContainer container) => ParentContainer = container;

        public string Format(string val, params string[] args)
        {
            string tmp_string = val;
            tmp_string = String.Format(val, args);
            tmp_string = Regex.Replace(tmp_string, @"\\n", Environment.NewLine);
            tmp_string = KScriptVariableHandler.ReturnFormattedVariables(ParentContainer, tmp_string);
            return tmp_string;
        }

        public string Format(string val)
        {
            if (!string.IsNullOrWhiteSpace(val))
            {
                string tmp_string = val;
                tmp_string = String.Format(val);
                tmp_string = Regex.Replace(tmp_string, @"\\n", Environment.NewLine);
                tmp_string = KScriptVariableHandler.ReturnFormattedVariables(ParentContainer, tmp_string);
                return tmp_string;
            } else
            {
                return string.Empty;
            }
        }
    }
}
