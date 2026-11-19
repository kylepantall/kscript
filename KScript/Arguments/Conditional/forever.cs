using KScript.Metadata;
using KScript.KScriptExceptions;

namespace KScript.Arguments
{
    [KScriptRetired("Use <while test=\"true\">.", Rename = "while")]
    public class forever : KScriptUnconditionalLoop
    {
        public override bool Run() => true;
        public override void Validate() => throw new KScriptNoValidationNeeded(this);
    }
}
