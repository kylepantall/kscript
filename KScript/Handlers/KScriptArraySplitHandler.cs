using System.Collections.Generic;
using System.Linq;

namespace KScript.Handlers
{
    public class KScriptArraySplitHandler
    {
        /// <summary>
        /// Used to split a string into an array using a split_char string.
        /// </summary>
        /// <param name="value">String to split</param>
        /// <param name="split_char">tring to use when splitting</param>
        /// <returns>Returns an Array of string</returns>
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
