using KScript.KScriptExceptions;
using KScript.KScriptObjects;

namespace KScript.Arguments
{
    public class @else : KScriptObject
    {
        public override bool Run()
        {
            var ifResult = ToBool(GetBaseObject().GetValueStore()["result"].ToString());
            return !ifResult;

        }

        public override string UsageInformation() => "Used to only run the inner KScriptObjects if the condition is true.";

        public override void Validate() => throw new KScriptNoValidationNeeded(this);
    }
}
