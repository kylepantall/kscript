using KScript.KScriptTypes.KScriptExceptions;

namespace KScript.Arguments
{
    [KScriptObjects.KScriptNoInnerObjects]
    public class end : KScriptObject
    {
        public override bool Run() => ParentContainer.Stop();
        public override string UsageInformation() => @"Closes the application and halts all computation.";
        public override void Validate() => throw new KScriptNoValidationNeeded();
    }
}
