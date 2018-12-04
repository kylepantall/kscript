namespace KScript.VariableFunctions
{
    public class trim : IVariableFunction
    {
        public trim(KScriptContainer container, string variable_id) : base(container, variable_id) { }
        public override string Evaluate(params string[] args) => GetDef().Contents.Trim();
        public override bool IsAccepted() => true;
    }
}
