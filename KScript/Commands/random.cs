﻿using System;

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
                HandleException(this, ex);
            }
        }


        public override string Calculate()
        {
            if (min > -1 && max > -1)
            {
                return ParentContainer.GetRandom().Next(min, max).ToString();
            }
            else
            {
                return ParentContainer.GetRandom().Next().ToString();
            }
        }
    }
}
