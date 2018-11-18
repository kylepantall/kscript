using KScript.Handlers;
using KScript.KScriptTypes.KScriptExceptions;

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
                throw new KScriptValidationFail("Value cannot be NULL");
            }
            else
            {
                return KScriptArithmeticHandler.HandleCalculation(KScriptCommandHandler.HandleCommands(maths, ParentContainer));
            }
        }
    }
}