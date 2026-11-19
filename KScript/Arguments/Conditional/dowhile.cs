using KScript.Metadata;
using KScript.KScriptExceptions;

namespace KScript.Arguments
{
    [KScriptRetired("Use <while test=\"...\">.", Rename = "while")]
    public class dowhile : KScriptLoopConditional
    {
        public override bool Run() => true;
        public override void Validate() => throw new KScriptNoValidationNeeded(this);
    }
}
