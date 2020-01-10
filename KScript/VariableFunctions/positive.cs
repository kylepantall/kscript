using System.Linq;

namespace KScript.VariableFunctions
{
    internal class positive : IVariableFunction
    {
        public positive(KScriptContainer container, string value) : base(container, value) { }

        public override string Evaluate(params string[] args)
        {
            return ToBoolString(int.Parse(GetDef().Contents) > -1);
        }

        public override bool IsAccepted() => GetDef().Contents.All(char.IsNumber);
    }
}
