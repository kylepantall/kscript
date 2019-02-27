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
            return "Used within dowhile loops to break conditional loops.";
        }

        public override void Validate()
        {
            throw new KScriptNoValidationNeeded(this);
        }
    }
}
