using System;

namespace KScript.Commands
{
    public class args : KScriptCommand
    {
        private string index = "0";
        public args(string index) { Index = int.Parse(index); }

        public int Index
        {
            get { return int.Parse(index); }
            set { index = value.ToString(); }
        }

        public override string Calculate()
        {
            try
            {
                return KScript().Parser.CustomArguments[Index];
            }
            catch (Exception)
            {
                return NULL;
            }
        }
    }
}
