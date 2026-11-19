using KScript.Metadata;
using System;

namespace KScript.Commands
{
    [KScriptDoc("yes if the first text contains the second.", Returns = "bool")]
    public class contains : KScriptCommand
    {
        private readonly string Arg0;
        private readonly string Arg1;

        public contains(string arg0, string arg1)
        {
            Arg0 = arg0;
            Arg1 = arg1;
        }
        public override string Calculate()
        {
            try
            {
                return ToBoolString(Arg0.Contains(Arg1));
            }
            catch (Exception ex)
            {
                HandleException(ex, this);
                return null;
            }
        }

        public override void Validate() { }
    }
}
