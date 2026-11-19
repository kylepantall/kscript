using KScript.Metadata;
using KScript.KScriptExceptions;
using KScript.KScriptObjects;

namespace KScript.Arguments
{
    [KScriptDoc("Runs its body when the test is true. Follow it with <elif> and <else>.", Example = "<if test=\"$score ge 90\">\n  <echo>A</echo>\n</if>\n<else>\n  <echo>B</echo>\n</else>")]
    [KScriptElement(KScriptContentKind.Statements)]
    [KScriptAttribute("test", KScriptValueKind.Expression, "The condition, for example $n lt 10 and $name ne ''.", Required = true)]
    public class @if : KScriptConditional
    {
        [KScriptProperty("Used to signify whether to check for a yes or no value in order to parse inner script objects. By default, checks for true.", false)]
        [KScriptAcceptedOptions("yes", "no", "1", "0", "y", "n", "true", "false", "t", "f")]
        [KScriptExample(@"<if checkfor=""yes""> ... </if>")]
        [KScriptExample(@"<if checkfor=""no""> ... </if>")]
        [KScriptExample(@"<if checkfor=""1""> ... </if>")]
        [KScriptExample(@"<if checkfor=""0""> ... </if>")]
        [KScriptRetired("Write the condition with not: test=\"not $x\".")]
        public string checkfor { get; set; } = "yes";

        public override bool EvaluateCondition()
        {
            bool checkFor = checkfor != null ? ToBool(checkfor) : true;
            string boolValue = HandleCommands(condition ?? string.Empty);

            if (!IsBool(boolValue))
            {
                return false;
            }

            return checkFor ? ToBool(boolValue) : !ToBool(boolValue);
        }

        public override string UsageInformation() => "Used to only run the inner KScriptObjects if the condition is true.";

        public override void Validate() => throw new KScriptNoValidationNeeded(this);
    }
}
