using KScript.KScriptTypes.KScriptExceptions;

namespace KScript.Commands
{
    public class to_lower : KScriptCommand
    {
        private string str;
        public to_lower(string str) => this.str = str;

        public override string Calculate()
        {
            if (string.IsNullOrEmpty(str))
            {
                throw new KScriptValidationFail("Value cannot be NULL");
            }

            return str.ToLower();
        }
    }
}
