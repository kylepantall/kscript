using System.Linq;
using System.Text;
using System;

namespace KScript.Commands
{
    public class tie : KScriptCommand
    {
        public tie(params string[] inputs) => this.inputs = inputs;

        private readonly string[] inputs;

        public override string Calculate()
        {
            var values = this.inputs.Select(i => "<tie>" + i + "</tie>").ToArray();
            return "<ties index=\"" + "1" + "\">" + string.Join(string.Empty, values) + "</ties>";
        }


        public override void Validate() { }
    }
}
