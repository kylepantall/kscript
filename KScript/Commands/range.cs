using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KScript.Commands
{
    public class range : KScriptCommand
    {
        private string check, max, min = "";

        public range(string check, string min, string max)
        {
            this.check = check;
            this.max = max;
            this.min = min;
        }
        public override string Calculate()
        {
            int max = -1;
            int min = -1;
            int check = -1;


            if (int.TryParse(Format(this.check), out check) &&
                int.TryParse(Format(this.max), out max) &&
                int.TryParse(Format(this.min), out min))
            {
                return ToBoolString(check >= min && check <= max);
            }
            else
            {
                return ToBoolString(false);
            }

        }

        public override void Validate()
        {
            //To implement
        }
    }
}
