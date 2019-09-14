﻿using KScript.KScriptExceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KScript.Arguments.Output
{
    class menu : KScriptObject
    {
        public override bool Run()
        {
            return true;
        }
        public override string UsageInformation() => "Used to create a menu.";

        public override void Validate() => throw new KScriptNoValidationNeeded(this);
    }
}
