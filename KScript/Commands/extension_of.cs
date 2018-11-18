namespace KScript.Commands
{
    public class extension_of : KScriptCommand
    {
        private string value;
        public extension_of(string value) => this.value = value;
        public override string Calculate()
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new KScriptTypes.KScriptExceptions.KScriptValidationFail("Value cannot be NULL");
            }
            else
            {
                return value.Substring(value.LastIndexOf('.') + 1);
            }
        }
    }
}
