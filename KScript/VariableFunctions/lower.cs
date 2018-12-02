using System.Text.RegularExpressions;

namespace KScript.VariableFunctions
{
    internal class lower : IVariableFunction
    {
        public lower(KScriptContainer container, string variable_id) : base(container, variable_id) { }
        public override string Evaluate(params string[] args) => GetDef().Contents.ToLower();
        public override bool IsAccepted() => new Regex("^.+$").IsMatch(GetDef().Contents);
    }
}
