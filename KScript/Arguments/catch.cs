using KScript.KScriptObjects;

namespace KScript.Arguments
{
    public class @catch : KScriptObjectException
    {
        public override bool Run() => true;

        public override string UsageInformation() => "Used to catch and handle specified exceptions";

        public override void Validate() => new KScriptExceptions.KScriptNoValidationNeeded(this);
    }
}
