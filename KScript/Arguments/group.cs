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

        public string id { get; set; }

        public override void Run()
        {
            if (contents != null) contents.Run();
        }
        public override void Validate() => throw new KScriptNoValidationNeeded();
    }
}
