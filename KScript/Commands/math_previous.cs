using KScript.Metadata;
using KScript.KScriptExceptions;

namespace KScript.Commands
{
    [KScriptRetired("Write the arithmetic directly and store it with <let> or <set>.")]
    class math_previous : KScriptCommand
    {
        public override string Calculate() => KScript().GetGlobalValue("math", "previous_result");
        public override void Validate() => throw new KScriptNoValidationNeeded(this);
    }
}
