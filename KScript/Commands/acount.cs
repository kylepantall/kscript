using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KScript.Commands
{
    public class acount : KScriptCommand
    {
        private string id;
        public acount(string id) => this.id = id;
        public override string Calculate() => KScript().ArrayGet(id).Count.ToString();
    }
}
