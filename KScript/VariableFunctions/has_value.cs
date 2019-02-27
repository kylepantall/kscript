namespace KScript.VariableFunctions
{
    class has_value : IVariableFunction
    {
        public has_value(KScriptContainer container, string variable_ID) : base(container, variable_ID) { }

        public override string Evaluate(params string[] args) => ToBoolString(!string.IsNullOrEmpty(GetDef().Contents));

        public override bool IsAccepted() => true;
    }
}
