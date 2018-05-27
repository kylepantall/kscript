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
        private string a = "", b = "";
        private bool ignore_case = false;

        public compare_to(string a, string b)
        {
            this.a = a;
            this.b = b;
        }

        public compare_to(string a, string b, string ignore_case)
        {
            this.a = a;
            this.b = b;
            this.ignore_case = ToBool(ignore_case);
        }

        public override string Calculate()
        {
            if (!ignore_case) return ToBoolString(a == b);
            else return ToBoolString(a.ToLower() == b.ToLower());
        }
    }
}
