using System.Linq;
using KScript.KScriptExceptions;
using System.Runtime.InteropServices;

namespace KScript.Handlers
{
    [ClassInterface(ClassInterfaceType.None)]
    public class KScriptBoolHandler
    {

        public static bool IsBool(string val) => Global.Booleans.NO_VALUES.Contains(val.ToLower()) || Global.Booleans.YES_VALUES.Contains(val.ToLower());

        public static bool Convert(string val)
        {
            if (string.IsNullOrEmpty(val))
                return false;

            return Global.Booleans.YES_VALUES.Contains(val.ToLower());
        }
    }
}
