namespace KScript.CommandHandler
{
    public class IValue : ICommand
    {
        private readonly KScriptContainer Container;
        public IValue(string value, KScriptContainer kScriptContainer) : base(value)
        {
            Value = value;
            Container = kScriptContainer;
        }

        public override string CalculateValue()
        {
            if (KScriptVariableHandler.IsVariable(Value))
            {
                string val = KScriptVariableHandler.ReturnFormattedVariables(Container, Value);

                if (val.Trim().StartsWith("'") && val.Trim().EndsWith("'"))
                {
                    return val.Trim(char.Parse("'"));
                }
                else
                {
                    return val;
                }
            }
            else
            {
                if (Value.Trim().StartsWith("'") && Value.Trim().EndsWith("'"))
                {
                    return Value.Trim(char.Parse("'"));
                }
                else
                {
                    return Value;
                }
            }

        }
    }
}
