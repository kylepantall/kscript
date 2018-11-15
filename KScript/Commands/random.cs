using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KScript.Commands
{
    public class random : KScriptCommand
    {
        public int min { get; set; } = -1;
        public int max { get; set; } = -1;

        public random() { }
        public random(string min, string max) { this.min = int.Parse(min); this.max = int.Parse(max); }


        public override string Calculate()
        {
            if (min > -1 && max > -1)
                return ParentContainer.GetRandom().Next(min, max).ToString();
            else
                return ParentContainer.GetRandom().Next().ToString();
        }
    }
}
