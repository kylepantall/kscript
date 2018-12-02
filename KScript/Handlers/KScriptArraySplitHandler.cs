using System.Collections.Generic;
using System.Linq;

namespace KScript.Handlers
{
    public class KScriptArraySplitHandler
    {
        public static List<string> Split(string value, string split_char)
        {
            var parser = new Microsoft.VisualBasic.FileIO.TextFieldParser(new System.IO.StringReader(value));
            parser.SetDelimiters(split_char);
            parser.HasFieldsEnclosedInQuotes = true;
            parser.TrimWhiteSpace = false;

            List<string> data = new List<string>();

            while (!parser.EndOfData)
            {
                data.AddRange(parser.ReadFields().ToList());
            }

            return data;
        }
    }
}
