using System;

namespace KScript.Commands
{
    public class args : KScriptCommand
    {
        private string index = "0";
        public args(string index) { Index = int.Parse(index); }

        public args() { index = ""; }

        public int Index
        {
            get { return int.Parse(index); }
            set { index = value.ToString(); }
        }

        public override string Calculate()
        {
            try
            {
                if (string.IsNullOrEmpty(index)) { return KScript().Parser.CustomArguments.Length.ToString(); }
                else { return KScript().Parser.CustomArguments[Index]; }
            }
            catch (Exception ex)
            {
                HandleException(ex, this);
                return null;
            }
        }
    }
}
