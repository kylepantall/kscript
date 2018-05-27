using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KScript.Commands
{
    public class date : KScriptCommand
    {
        public date() => format = null;
        public date(string format) => this.format = format;

        private string format;

        public override string Calculate()
        {
            if (!string.IsNullOrWhiteSpace(format))
                try { return DateTime.Now.Date.ToString(format); }
                catch (Exception) { }
            return DateTime.Now.ToLongDateString();
        }
    }
}
