using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KScript.Commands
{
    public class random : KScriptCommand
    {
        public random() { }
        public random(string uid) { }
        public override string Calculate() => ParentContainer.GetRandom().Next().ToString();
    }
}
