﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KScript.Commands
{
    public class not : KScriptCommand
    {
        public not(string value) => Value = value;
        private string Value { get; set; }
        public override string Calculate() => ToBoolString(!ToBool(Value));
    }
}
