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
            if (val != null)
            {
                string _val = val.ToLower();
                if (_val == "true" || val == "1" || val == "t" || val == "yes" || val == "y")
                {
                    return true;
                }
                else if (_val == "false" || val == "0" || val == "f" || val == "no" || val == "n")
                {
                    return false;
                }
                else
                {
                    throw new KScriptBoolInvalid();
                }
            }
            else
            {
                throw new KScriptBoolInvalid();
            }
        }
    }
}
