using KScript.Handlers;

namespace KScript.VariableFunctions
{
    class is_bool : IVariableFunction
    {
        public is_bool(KScriptContainer container, string id) : base(container, id) { }

        public override string Evaluate(params string[] args)
        {
            return ToBoolString(KScriptBoolHandler.IsBool(GetDef().Contents));
        }

        public override bool IsAccepted() => true;
    }
}
