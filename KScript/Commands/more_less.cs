using KScript.KScriptExceptions;

namespace KScript.Commands
{
    public class more_less : KScriptCommand
    {
        private readonly string ValueA = "";
        private readonly string ValueB = "";
        private string Operator = "<";

        public more_less(string a, string b) { ValueA = a; ValueB = b; }

        //[KScriptObjects.KScriptAcceptedOptions("mt", "mte", "lt", "lte")]
        public more_less(string a, string b, string Operator) : this(a, b) => this.Operator = Operator;

        public override string Calculate()
        {

            if (string.IsNullOrEmpty(ValueA) || string.IsNullOrEmpty(ValueB) || string.IsNullOrEmpty(Operator))
            {
                throw new KScriptValidationFail(this, "Value cannot be NULL");
            }

            string vA = KScript().GetStringHandler().Format(ValueA), vB = KScript().GetStringHandler().Format(ValueB);

            if (Operator.ToLower() == "mt")
            {
                return ToBoolString(double.Parse(vA) > double.Parse(vB));
            }

            if (Operator.ToLower() == "mte")
            {
                return ToBoolString(double.Parse(vA) >= double.Parse(vB));
            }

            if (Operator.ToLower() == "lt")
            {
                return ToBoolString(double.Parse(vA) < double.Parse(vB));
            }

            if (Operator.ToLower() == "lte")
            {
                return ToBoolString(double.Parse(vA) <= double.Parse(vB));
            }
            else
            {
                return ToBoolString(double.Parse(vA) < double.Parse(vB));
            }
        }


        public override void Validate() { }
    }
}
