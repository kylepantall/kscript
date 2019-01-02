using KScript.KScriptExceptions;

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
                throw new KScriptValidationFail(this, "Value cannot be NULL");
            }

            return str.ToLower();
        }


        public override void Validate() { }
    }
}
