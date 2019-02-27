using System.Linq;

namespace KScript.VariableFunctions
{
    internal class is_zero : IVariableFunction
    {
        public is_zero(KScriptContainer container, string value) : base(container, value) { }

        public override string Evaluate(params string[] args)
        {
            double num = double.Parse(GetDef().Contents);
            return ToBoolString(num <= 0);
        }

        public override bool IsAccepted() => GetDef().Contents.All(i => char.IsNumber(i));
    }
}
