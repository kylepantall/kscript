using KScript.KScriptExceptions;

namespace KScript.Arguments
{
    public class @for : KScriptObjectLoop
    {
        public override bool Run() => true;
        public override void Validate() => throw new KScriptNoValidationNeeded(this);
    }
}
