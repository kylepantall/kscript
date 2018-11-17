using KScript.Handlers;

namespace KScript.Commands
{
    public class math : KScriptCommand
    {
        public math() { }
        public math(string maths) => this.maths = maths;
        private string maths { get; set; }
        public override string Calculate() => KScriptArithmeticHandler.HandleCalculation(KScriptCommandHandler.HandleCommands(maths, ParentContainer));
    }
}