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
                string value = KScriptCommandHandler.HandleCommands(maths, ParentContainer, GetBaseObject());

                while (KScriptCommandHandler.IsCommand(value, ParentContainer, GetBaseObject()))
                {
                    value = KScriptCommandHandler.HandleCommands(value, ParentContainer, GetBaseObject());
                }
                return KScriptArithmeticHandler.HandleCalculation(value);
            }
        }


        public override void Validate() { }
    }
}