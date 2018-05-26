using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KScript.Commands
{
    /// <summary>
    /// Compares values 'a' and 'b' to see if values are equal, returns "yes" if equal, else returns "no".
    /// </summary>
    public class compare_to : KScriptCommand
    {
        private string a = "";
        private string b = "";

        public compare_to(string a, string b)
        {
            this.a = a;
            this.b = b;
        }

        public override void Run()
        {
            if (a == b) Result("yes"); else Result("no");
        }
    }
}
