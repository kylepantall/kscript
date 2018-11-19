using System.Text.RegularExpressions;

namespace KScript.VariableFunctions
{
    class upper : IVariableFunction
    {
        public string Evaluate(string value) => value.ToUpper();
        public bool IsAccepted(string value) => new Regex("^[a-zA-Z0-9]*$").IsMatch(value);
    }
}
