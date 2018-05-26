﻿using KScript.KScriptTypes.KScriptExceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KScript.Arguments
{
    public class @if : KScriptObjectEnumerable
    {
        public KScriptObject contents { get; set; }
        public string condition { get; set; }
        public override void Run()
        {
            if (contents != null) contents.Run();
        }
        public override void Validate() => throw new KScriptNoValidationNeeded();
    }
}
