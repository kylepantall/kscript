using KScript.KScriptExceptions;

namespace KScript
{
    public class KScriptConditional : KScriptObject
    {
        public string condition { get; set; } = "";

        public override bool Run() => true;
        public override void Validate() => throw new KScriptNoValidationNeeded(this);
        public override string UsageInformation() => @"Used to store an array of KScriptObjects.";
    }
}
