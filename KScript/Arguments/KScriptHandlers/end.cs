using KScript.KScriptExceptions;

namespace KScript.Arguments
{
    [KScriptObjects.KScriptNoInnerObjects]
    public class end : KScriptObject
    {
        public override bool Run() => KScript().Stop();
        public override string UsageInformation() => @"Closes the application and halts all computation.";
        public override void Validate() => throw new KScriptNoValidationNeeded(this);
    }
}
