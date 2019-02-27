using KScript.KScriptExceptions;

namespace KScript.Arguments
{
    class afor : KScriptArrayLoop
    {
        public override bool Run() => true;
        public override void Validate() => throw new KScriptNoValidationNeeded(this);
    }
}
