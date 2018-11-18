using KScript.KScriptTypes.KScriptExceptions;

namespace KScript.Commands
{
    public class length : KScriptCommand
    {
        private string str = "";
        public length(string str) => this.str = str;
        public override string Calculate()
        {
            if (string.IsNullOrEmpty(str))
            {
                throw new KScriptValidationFail("Value cannot be NULL");
            }
            else
            {
                return str.Length.ToString();
            }
        }
    }
}
