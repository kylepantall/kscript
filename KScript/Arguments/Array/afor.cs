using KScript.Metadata;
using KScript.KScriptExceptions;

namespace KScript.Arguments
{
    [KScriptRetired("Use <for each=\"item\" in=\"$list\">.", Rename = "for")]
    public class afor : KScriptArrayLoop
    {
        public override bool Run() => true;
        public override void Validate() => throw new KScriptNoValidationNeeded(this);
    }
}
