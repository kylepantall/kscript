using KScript.KScriptExceptions;

namespace KScript.Commands
{
    class math_previous : KScriptCommand
    {
        public override string Calculate() => KScript().GetGlobalValue("math", "previous_result");
        public override void Validate() => throw new KScriptNoValidationNeeded(this);
    }
}
