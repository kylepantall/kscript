using KScript.KScriptTypes.KScriptExceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KScript.Arguments
{
    public class @if : KScriptObjectEnumerable
    {
        public string checkfor { get; set; } = "yes";
        public string condition { get; set; }
        public override bool Run()
        {
            bool _check_for = checkfor != null ? ToBool(checkfor) : true;
            string new_condition = HandleCommands(condition);

            if (!_check_for) return !ToBool(new_condition);
            else return ToBool(new_condition);
        }

        public override void Validate() => throw new KScriptNoValidationNeeded();
    }
}
