using KScript.Metadata;
using KScript.KScriptExceptions;

namespace KScript.Arguments.Output
{
    [KScriptDoc("Shows a numbered menu and runs the chosen entry.")]
    [KScriptElement(KScriptContentKind.Statements, Context = KScriptElementContext.Script, HtmlWins = true)]
    class menu : KScriptObject
    {
        public override bool Run()
        {
            return true;
        }
        public override string UsageInformation() => "Used to create a menu.";

        public override void Validate() => throw new KScriptNoValidationNeeded(this);
    }
}
