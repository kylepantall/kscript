using KScript.KScriptTypes.KScriptExceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KScript.Arguments
{
    public class end : KScriptObject
    {
        public override bool Run() => ParentContainer.Stop();
        public override string UsageInformation() => @"Closes the application and halts all computation.";
        public override void Validate() => throw new KScriptNoValidationNeeded();
    }
}
