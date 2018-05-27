using KScript.KScriptTypes.KScriptExceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KScript.Arguments
{
    public class group : KScriptObjectEnumerable
    {
        public KScriptObject contents { get; set; }

        public string description { get; set; }

        public string id { get; set; }

        public override bool Run()
        {
            if (contents != null) contents.Run();
            return true;
        }
        public override void Validate() => throw new KScriptNoValidationNeeded();
    }
}
