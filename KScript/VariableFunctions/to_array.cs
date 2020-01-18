using KScript.Handlers;
using System.Text.RegularExpressions;

namespace KScript.VariableFunctions
{
    class to_array : IVariableFunction
    {
        public to_array(KScriptContainer container, string variable_id) : base(container, variable_id) { }

        public override string Evaluate(params string[] args)
        {
            try
            {
                ParentContainer.ArrayInsert(GetDef().id, new System.Collections.Generic.List<string>(KScriptArraySplitHandler.Split(GetDef().Contents, ",")));
                return ToBoolString(true);
            }
            catch
            {
                return ToBoolString(false);
            }
        }

        public override bool IsAccepted() => Regex.IsMatch(GetDef().Contents, Global.GlobalIdentifiers.ARRAY_CHECK_EXPRESSION) && Regex.Matches(GetDef().Contents, Global.GlobalIdentifiers.ARRAY_CHECK_EXPRESSION).Count >= 1;
    }
}
