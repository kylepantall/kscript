using KScript.Handlers;

namespace KScript.VariableFunctions
{
    class isbool : IVariableFunction
    {
        public isbool(KScriptContainer container, string id) : base(container, id) { }

        public override string Evaluate(params string[] args) => ToBoolString(KScriptBoolHandler.IsBool(GetDef().Contents));

        public override bool IsAccepted() => true;
    }
}
