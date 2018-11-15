using KScript.KScriptTypes.KScriptExceptions;

namespace KScript.Arguments
{
    public class dowhile : KScriptConditional
    {
        public override bool Run() => true;
        public override void Validate() => throw new KScriptNoValidationNeeded();
    }
}
