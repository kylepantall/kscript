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
            string vA = KScript().StringHandler.Format(ValueA), vB = KScript().StringHandler.Format(ValueB);

            if (Operator.ToLower() == "mt")
            {
                return ToBoolString(int.Parse(vA) > int.Parse(vB));
            }

            if (Operator.ToLower() == "mte")
            {
                return ToBoolString(int.Parse(vA) >= int.Parse(vB));
            }

            if (Operator.ToLower() == "lt")
            {
                return ToBoolString(int.Parse(vA) < int.Parse(vB));
            }

            if (Operator.ToLower() == "lte")
            {
                return ToBoolString(int.Parse(vA) <= int.Parse(vB));
            }
            else
            {
                return ToBoolString(int.Parse(vA) < int.Parse(vB));
            }
        }
    }
}
