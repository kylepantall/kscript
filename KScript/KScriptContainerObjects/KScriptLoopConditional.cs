using KScript.Metadata;
using KScript.KScriptExceptions;

namespace KScript
{
    public class KScriptLoopConditional : KScriptObject
    {
        [KScriptRetired("Use test=\"...\" with an expression.", Rename = "test")]
        public string condition { get; set; } = "";

        public override bool Run() => true;
        public override void Validate() => throw new KScriptNoValidationNeeded(this);
        public override string UsageInformation() => @"Used to store an array of KScriptObjects.";
    }
}
