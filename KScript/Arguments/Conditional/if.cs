using KScript.KScriptExceptions;
using KScript.KScriptObjects;

namespace KScript.Arguments
{
    public class @if : KScriptConditional
    {
        [KScriptProperty("Used to signify whether to check for a yes or no value in order to parse inner script objects. By default, checks for true.", false)]
        [KScriptAcceptedOptions("yes", "no", "1", "0", "y", "n", "true", "false", "t", "f")]
        [KScriptExample(@"<if checkfor=""yes""> ... </if>")]
        [KScriptExample(@"<if checkfor=""no""> ... </if>")]
        [KScriptExample(@"<if checkfor=""1""> ... </if>")]
        [KScriptExample(@"<if checkfor=""0""> ... </if>")]
        public string checkfor { get; set; } = "yes";

        public override bool Run()
        {
            bool checkFor = checkfor != null ? ToBool(checkfor) : true;
            string boolValue = HandleCommands(condition);
            var result = IsBool(boolValue) ? (!checkFor ? !ToBool(boolValue) : ToBool(boolValue)) : false;
            GetValueStore()["result"] = result;
            return result;
        }

        public override string UsageInformation() => "Used to only run the inner KScriptObjects if the condition is true.";

        public override void Validate() => throw new KScriptNoValidationNeeded(this);
    }
}
