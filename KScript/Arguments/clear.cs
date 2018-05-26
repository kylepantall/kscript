using System;
using KScript.KScriptTypes.KScriptExceptions;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KScript.Arguments
{
    public class clear : KScriptObject
    {
        public override void Run() => Console.Clear();
        public override void Validate()
        {
            throw new KScriptNoValidationNeeded();
        }
    }
}
