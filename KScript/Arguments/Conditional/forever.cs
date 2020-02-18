using KScript.KScriptExceptions;

namespace KScript.Arguments
{
    public class forever : KScriptUnconditionalLoop
    {
        public override bool Run() => true;
        public override void Validate() => throw new KScriptNoValidationNeeded(this);
    }
}
