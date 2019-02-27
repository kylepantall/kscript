using System;

namespace KScript.VariableFunctions
{
    /// <summary>
    /// This variable pointer determines if a def is of the type URL.
    /// </summary>
    public class is_url : IVariableFunction
    {
        public is_url(KScriptContainer container, string value) : base(container, value) { }

        public override string Evaluate(params string[] args) => ToBoolString(IsUrl(GetDef().Contents));

        public bool IsUrl(string value)
        {
            Uri uriResult;
            bool result = Uri.TryCreate(value, UriKind.Absolute, out uriResult)
                && (uriResult.Scheme != Uri.UriSchemeFile);
            return result;
        }

        public override bool IsAccepted() => true;
    }
}
