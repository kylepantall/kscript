using System.Runtime.InteropServices;
using KScript.KScriptExceptions;

namespace KScript.Handlers
{
    [ClassInterface(ClassInterfaceType.None)]
    public class KScriptBoolHandler
    {

        public static bool IsBool(string val)
        {
            try
            {
                bool tmp = Convert(val);
                return true;
            }
            catch (KScriptBoolInvalid)
            {
                return false;
            }
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
                throw new KScriptBoolInvalid();
            }
            else
            {
                throw new KScriptBoolInvalid();
            }
        }
    }
}
