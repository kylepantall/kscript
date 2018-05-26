using KScript.KScriptTypes.KScriptExceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KScript
{
    public class KScriptObjectEnumerable : KScriptObject
    {
        public KScriptObject Contents { get; set; }
        public KScriptObjectEnumerable(KScriptObject obj) => Contents = obj;

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
