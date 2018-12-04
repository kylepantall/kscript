using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace KScript.VariableFunctions
{
    class paragraph : IVariableFunction
    {

        private readonly string paragraph_expression = @"\""(.+)\""";

        public paragraph(KScriptContainer container, string variable_id) : base(container, variable_id) { }

        public override string Evaluate(params string[] args)
        {
            MatchCollection collection = Regex.Matches(GetDef().Contents, paragraph_expression);
            StringBuilder builder = new StringBuilder();
            collection.Cast<Match>().ToList().ForEach(item => builder.AppendLine(item.Groups[1].Value));
            return builder.ToString();
        }

        public override bool IsAccepted() => Regex.IsMatch(GetDef().Contents, paragraph_expression);
    }
}
