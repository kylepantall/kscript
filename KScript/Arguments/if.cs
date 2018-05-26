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
        public string condition { get; set; }

        public @if(KScriptObject obj) : base(obj) { }

        public override void Run()
        {
            throw new KScriptNoRunMethodImplemented();
        }

        public override void Validate()
        {
            throw new KScriptNoValidationNeeded();
        }
    }
}
