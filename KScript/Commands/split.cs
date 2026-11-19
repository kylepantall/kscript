using KScript.Metadata;
using System;

namespace KScript.Commands
{
    [KScriptDoc("One part of text split by a separator.")]
    public class split : KScriptCommand
    {
        private readonly string @string;
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

        public override void Validate() { }
    }
}
