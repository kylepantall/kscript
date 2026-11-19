using KScript.Metadata;
using KScript.KScriptExceptions;
using System;

namespace KScript.Commands
{
    [KScriptDoc("The character with this character code.")]
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
