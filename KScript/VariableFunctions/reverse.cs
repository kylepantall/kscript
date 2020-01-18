using System;

namespace KScript.VariableFunctions
{
    class reverse : IVariableFunction
    {
        public reverse(KScriptContainer container, string variable_id) : base(container, variable_id) { }
        public override bool IsAccepted() => true;

        public override string Evaluate(params string[] args)
        {
            char[] arr = GetDef().Contents.ToCharArray();
            Array.Reverse(arr);
            return new string(arr);
        }
    }
}
