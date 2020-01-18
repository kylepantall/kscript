using KScript.KScriptExceptions;
using System.Runtime.InteropServices;

namespace KScript.Handlers
{
    [ClassInterface(ClassInterfaceType.None)]
    public class KScriptBoolHandler
    {

        public static bool IsBool(string val)
        {
            string _val = val;
            return (_val == "true" || val == "1" || val == "t" || val == "yes" || val == "y") || (_val == "false" || val == "0" || val == "f" || val == "no" || val == "n");
        }

        public static bool Convert(string val)
        {
            if (string.IsNullOrEmpty(val))
                return false;

            string _val = val.ToLower();
            return _val == "true" || val == "1" || val == "t" || val == "yes" || val == "y";
        }
    }
}
