namespace KScript.VariableFunctions
{
    class length : IVariableFunction
    {
        public length(KScriptContainer container, string id) : base(container, id) { }

        public override string Evaluate(params string[] args) => GetDef().Contents.Length.ToString();

        public override bool IsAccepted() => !string.IsNullOrEmpty(GetDef().Contents);
    }
}
