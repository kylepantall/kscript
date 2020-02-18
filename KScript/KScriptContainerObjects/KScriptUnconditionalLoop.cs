using KScript.KScriptExceptions;

namespace KScript
{
    public class KScriptUnconditionalLoop : KScriptObject
    {
        public override bool Run() => true;
        public override void Validate() => throw new KScriptNoValidationNeeded(this);
        public override string UsageInformation() => @"Used to loop unconditionally.";
    }
}
