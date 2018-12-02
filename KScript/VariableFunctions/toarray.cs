using System.Text.RegularExpressions;
using KScript.Handlers;

namespace KScript.VariableFunctions
{
    class toarray : IVariableFunction
    {
        public toarray(KScriptContainer container, string variable_id) : base(container, variable_id) { }

        public override string Evaluate(params string[] args)
        {
            ParentContainer.ArrayInsert(GetDef().id, new System.Collections.Generic.List<string>(KScriptArraySplitHandler.Split(GetDef().Contents, ",")));
            return string.Empty;
        }

        public override bool IsAccepted() => Regex.IsMatch(GetDef().Contents, Global.GlobalIdentifiers.ARRAY_CHECK_EXPRESSION) && Regex.Matches(GetDef().Contents, Global.GlobalIdentifiers.ARRAY_CHECK_EXPRESSION).Count >= 1;
    }
}
