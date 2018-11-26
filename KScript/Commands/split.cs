using System;

namespace KScript.Commands
{
    public class split : KScriptCommand
    {
        private string @string;
        private readonly string split_string;
        private readonly int Index;

        public split(string str, string split_str, string index)
        {
            @string = str;
            split_string = split_str;
            Index = int.Parse(index);
        }

        public override string Calculate()
        {
            try
            {
                return @string.Split(new string[] { split_string }, StringSplitOptions.None)[Index];
            }
            catch (Exception ex)
            {
                HandleException(ex, this);
                return null;
            }
        }
    }
}
