using KScript.Metadata;
using KScript.KScriptExceptions;

namespace KScript.Arguments
{
    [KScriptObjects.KScriptNoInnerObjects]
    [KScriptDoc("Ends the script.")]
    [KScriptElement(KScriptContentKind.Empty)]
    public class end : KScriptObject
    {
        public override bool Run() => KScript().Stop();
        public override string UsageInformation() => @"Closes the application and halts all computation.";
        public override void Validate() => throw new KScriptNoValidationNeeded(this);
    }
}
