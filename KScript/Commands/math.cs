using KScript.Handlers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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