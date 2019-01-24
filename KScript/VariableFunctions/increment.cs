using System.Linq;

namespace KScript.VariableFunctions
{
    public class increment : IVariableFunction
    {
        public increment(KScriptContainer container, string value) : base(container, value) { }

        public override string Evaluate(params string[] args)
        {
            double val = 0.0;
            if (double.TryParse(GetDef().Contents, out val))
            {
                val += 1;
                GetDef().Contents = val.ToString();
            }
            return GetDef().Contents;
        }

        public override bool IsAccepted() => GetDef().Contents.All(char.IsNumber);
    }
}
