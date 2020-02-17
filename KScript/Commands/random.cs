using System;

namespace KScript.Commands
{
    public class random : KScriptCommand
    {
        public int min { get; set; } = -1;
        public int max { get; set; } = -1;

        public random() { }
        public random(string min, string max)
        {
            try
            {
                this.min = int.Parse(min);
                this.max = int.Parse(max);
            }
            catch (Exception ex)
            {
                HandleException(ex, this);
            }
        }


        public override string Calculate()
        {
            if (min > -1 && max > -1)
            {
                return KScript().GetRandom().Next(min, max + 1).ToString();
            }
            else
            {
                return KScript().GetRandom().Next().ToString();
            }
        }


        public override void Validate() { }
    }
}
