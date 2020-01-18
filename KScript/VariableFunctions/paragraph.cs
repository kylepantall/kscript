using KScript.Handlers;
using System.Text.RegularExpressions;

namespace KScript.VariableFunctions
{
    class paragraph : IVariableFunction
    {
        public paragraph(KScriptContainer container, string variable_id) : base(container, variable_id) { }

        public override string Evaluate(params string[] args) => KScriptParagraphHandler.Parse(GetDef().Contents);

        public override bool IsAccepted()
        {
            return (GetDef() != null) && Regex.IsMatch(GetDef().Contents, Global.GlobalIdentifiers.PARAGRAPH_EXPRESSION);
        }
    }
}
