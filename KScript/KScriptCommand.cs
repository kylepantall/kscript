using KScript.KScriptTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KScript
{
    public abstract class KScriptCommand : KScriptIO
    {
        public KScriptCommand(KScriptContainer container) : base(container) { }
        public KScriptCommand() { }
        public abstract void Run();


        public void Result(object val)
        {
            //Should handle calculated value.
        }
    }
}
