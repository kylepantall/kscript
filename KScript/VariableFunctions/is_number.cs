namespace KScript.VariableFunctions
{
    public class is_number : IVariableFunction
    {
        public is_number(KScriptContainer container, string value) : base(container, value) { }
        public override string Evaluate(params string[] args) => ToBoolString(IsNumber(GetDef().Contents));
        public override bool IsAccepted() => true;
    }
}
