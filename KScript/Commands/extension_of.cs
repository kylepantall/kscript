using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KScript.Commands
{
    public class extension_of : KScriptCommand
    {
        private string value;
        public extension_of(string value) => this.value = value;
        public override string Calculate() => value.Substring(value.LastIndexOf('.') + 1);
    }
}
