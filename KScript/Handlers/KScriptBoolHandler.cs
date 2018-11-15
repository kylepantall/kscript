namespace KScript.Handlers
{
    public class KScriptBoolHandler
    {
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
                    return false;
                }
            }
            else
            {
                throw new KScriptTypes.KScriptExceptions.KScriptException("bool operator is invalid");
            }
        }
    }
}
