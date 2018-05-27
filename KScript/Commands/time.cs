using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KScript.Commands
{
    public class time : KScriptCommand
    {
        public time() => format = null;
        public time(string format) => this.format = format;

        private string format;

        public override string Calculate()
        {
            if (!string.IsNullOrWhiteSpace(format))
                try { return DateTime.Now.TimeOfDay.ToString(format); }
                catch (Exception) { }
            return DateTime.Now.TimeOfDay.ToString(@"hh\:mm\:ss");
        }
    }
}
