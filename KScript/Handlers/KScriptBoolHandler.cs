using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KScript.Handlers
{
    public class KScriptBoolHandler
    {
        public static bool Convert(string val)
        {
            string _val = val.ToLower();
            if (_val == "true" || val == "1" || val == "t" || val == "yes")
                return true;
            else if (_val == "false" || val == "0" || val == "f" || val == "no")
                return false;
            else
                throw new KScriptTypes.KScriptExceptions.KScriptException("bool operator is invalid");
        }
    }
}
