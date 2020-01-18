namespace KScript.VariableFunctions
{
    public class upper : IVariableFunction
    {
        public upper(KScriptContainer container, string variable_id) : base(container, variable_id) { }
        public override string Evaluate(params string[] args) => GetDef().Contents.ToUpper();
        public override bool IsAccepted() => true;
    }
}
