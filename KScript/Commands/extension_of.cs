using KScript.Metadata;
namespace KScript.Commands
{
    [KScriptDoc("The extension of a file name, such as .txt.")]
    public class extension_of : KScriptCommand
    {
        private readonly string value;
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
