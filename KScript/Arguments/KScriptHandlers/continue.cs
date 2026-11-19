using KScript.Metadata;
using KScript.KScriptExceptions;

namespace KScript.Arguments
{
    [KScriptObjects.KScriptNoInnerObjects()]
    [KScriptDoc("Skips to the next turn of the innermost loop.")]
    [KScriptElement(KScriptContentKind.Empty)]
    class @continue : KScriptObject
    {
        public override bool Run()
        {
            return true;
        }

        public override string UsageInformation()
        {
            return "Used within for loops to skip current iterations.";
        }

        public override void Validate()
        {
            throw new KScriptNoValidationNeeded(this);
        }
    }
}
