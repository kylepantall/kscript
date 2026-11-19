using KScript.Metadata;
using KScript.KScriptExceptions;

namespace KScript.Arguments
{
    [KScriptObjects.KScriptNoInnerObjects()]
    [KScriptDoc("Leaves the innermost loop.")]
    [KScriptElement(KScriptContentKind.Empty)]
    class @break : KScriptObject
    {
        public override bool Run()
        {
            KScript().StopConditionalLoops();
            return true;
        }

        public override string UsageInformation()
        {
            return "Used within loops to break conditional loops.";
        }

        public override void Validate()
        {
            throw new KScriptNoValidationNeeded(this);
        }
    }
}
