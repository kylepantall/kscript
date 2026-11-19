using KScript.Metadata;
using KScript.KScriptObjects;

namespace KScript.Arguments
{
    [KScriptDoc("Handles an error thrown in the <try> around it.")]
    [KScriptElement(KScriptContentKind.Statements, MustBeInside = "try")]
    [KScriptAttribute("as", KScriptValueKind.DeclareVariable, "A variable that holds the error: $err.type, $err.message, $err.line.")]
    [KScriptAttribute("when", KScriptValueKind.Expression, "Only catch errors for which this is true.")]
    public class @catch : KScriptObjectException
    {
        public override bool Run() => true;

        public override string UsageInformation() => "Used to catch and handle specified exceptions";

        public override void Validate() => new KScriptExceptions.KScriptNoValidationNeeded(this);
    }
}
