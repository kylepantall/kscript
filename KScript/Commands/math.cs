using KScript.Handlers;
using KScript.KScriptExceptions;

namespace KScript.Commands
{
    public class math : KScriptCommand
    {
        public math() { }
        public math(string maths) => this.maths = maths;
        private string maths { get; set; }
        public override string Calculate()
        {
            if (string.IsNullOrEmpty(maths))
            {
                throw new KScriptValidationFail(this, "Value cannot be NULL");
            }
            else
            {
                string value = KScriptCommandHandler.HandleCommands(maths, KScript(), GetBaseObject());

                // Commands may return further commands; resolve them, but never loop forever.
                for (int depth = 0; depth < 32 && KScriptCommandHandler.IsCommand(value); depth++)
                {
                    value = KScriptCommandHandler.HandleCommands(value, KScript(), GetBaseObject());
                }
                return KScriptArithmeticHandler.HandleCalculation(value);
            }
        }


        public override void Validate() { }
    }
}