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
                throw new KScriptExceptions.KScriptValidationFail(this);
            }
            else
            {
                return value.Substring(value.LastIndexOf('.') + 1);
            }
        }


        public override void Validate() { }
    }
}
