using KScript.KScriptExceptions;

namespace KScript.Arguments
{
    [KScriptObjects.KScriptNoInnerObjects()]
    class @break : KScriptObject
    {
        public override bool Run()
        {
            ParentContainer.StopConditionalLoops();
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
