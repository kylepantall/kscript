using System.Linq;

namespace KScript.VariableFunctions
{
    public class increment : IVariableFunction
    {
        public increment(KScriptContainer container, string value) : base(container, value) { }

        public override string Evaluate(params string[] args)
        {
            double val = 0.0;
            if (double.TryParse(GetDef().Contents, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out val))
            {
                val += 1;
                GetDef().Contents = val.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            return GetDef().Contents;
        }

        public override bool IsAccepted() => double.TryParse(GetDef().Contents, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _);
    }
}
