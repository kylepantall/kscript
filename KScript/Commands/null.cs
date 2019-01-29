using KScript.Handlers;

namespace KScript.Commands
{
    public class @null : KScriptCommand
    {
        private readonly string value = "";
        public @null(string val) => value = val;
        public @null() { }

        public override string Calculate()
        {
            if (!string.IsNullOrEmpty(value))
            {
                return ToBoolString(KScriptCommandHandler.HandleCommands(value, KScript(), GetBaseObject()) == NULL);
            }
            else
            {
                return NULL;
            }
        }

        public override void Validate() { }
    }
}