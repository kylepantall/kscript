using KScript.KScriptExceptions;

namespace KScript.Arguments
{
    [KScriptObjects.KScriptNoInnerObjects()]
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
