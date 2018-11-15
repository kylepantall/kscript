using KScript.KScriptObjects;
using KScript.KScriptTypes.KScriptExceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KScript.Arguments
{
    public class @if : KScriptObject
    {
        [KScriptProperty("Used to signify whether to check for a yes or no value in order to parse inner script objects. By default, checks for true.", false)]
        [KScriptAcceptedOptions("yes", "no", "1", "0", "y", "n", "true", "false", "t", "f")]
        [KScriptExample(@"<if checkfor=""yes""> ... </if>")]
        [KScriptExample(@"<if checkfor=""no""> ... </if>")]
        [KScriptExample(@"<if checkfor=""1""> ... </if>")]
        [KScriptExample(@"<if checkfor=""0""> ... </if>")]
        public string checkfor { get; set; } = "yes";

        [KScriptProperty(@"When parsed either is yes or no. Use KScript Commands to determine this value")]
        [KScriptExample(@"<if condition=""@compare_to(1,1)""> ... </if>")]
        [KScriptExample(@"<if condition=""@compare_to(@math(1+1),2)""> ... </if>")]
        public string condition { get; set; }

        public override bool Run()
        {
            bool _check_for = checkfor != null ? ToBool(checkfor) : true;
            string new_condition = HandleCommands(condition);

            if (!_check_for) return !ToBool(new_condition);
            else return ToBool(new_condition);
        }

        public override string UsageInformation() => "Used to only run the inner KScriptObjects if the condition is true.";

        public override void Validate() => throw new KScriptNoValidationNeeded();
    }
}
