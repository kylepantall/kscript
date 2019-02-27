﻿using System;
using KScript.KScriptExceptions;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KScript.Arguments
{
    /// <summary>
    /// KScript object to clear the console.
    /// </summary>
    [KScriptObjects.KScriptNoInnerObjects]
    public class clear : KScriptObject
    {
        public override bool Run()
        {
            Console.Clear();
            return true;
        }
        public override string UsageInformation() => "Used to clear the console.";
        public override void Validate() => throw new KScriptNoValidationNeeded(this);
    }
}