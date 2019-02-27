using System.Linq;

namespace KScript.VariableFunctions
{
    internal class not_zero : IVariableFunction
    {
        public not_zero(KScriptContainer container, string value) : base(container, value) { }

        public override string Evaluate(params string[] args)
        {
            int num = int.Parse(GetDef().Contents);

            return ToBoolString(num > 0);
        }

        public override bool IsAccepted() => GetDef().Contents.All(char.IsNumber);
    }
}
