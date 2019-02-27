using System;
using KScript.KScriptExceptions;

namespace KScript.Commands
{
    class @char : KScriptCommand
    {
        readonly string number;
        public @char(string number) => this.number = number;

        public override string Calculate()
        {
            int calc_number;
            if (int.TryParse(number, out calc_number))
            {
                return Convert.ToChar(calc_number).ToString();
            }
            else
            {
                return NULL;
            }
        }

        public override void Validate() => throw new KScriptNoValidationNeeded(this);
    }
}
