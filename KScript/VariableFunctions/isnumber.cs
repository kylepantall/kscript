using System.Linq;

namespace KScript.VariableFunctions
{
    public class isnumber : IVariableFunction
    {
        public isnumber(KScriptContainer container, string value) : base(container, value) { }

        public override string Evaluate(params string[] args) => ToBoolString(GetDef().Contents.All(char.IsNumber));

        public override bool IsAccepted() => true;
    }
}
