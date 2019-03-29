using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace KScript.MultiArray
{
    public class MultiArrayParser
    {
        const string ArrayMatch = @"~(\w+)\[.+\]";
        const string RetrieveItems = @"\[(\d+|\'\w+\'|\~\'\w+\')\]";


        /* Supported formats:
         * Is Initialised by ~myArrayName[0] etc.
         * Accepted key selectors:
         *  - [number] - Index of item.
         *  - ['string'] - Key of item
         *  - [~'string'] - Key of item (bubbles down the array from index).
         *  
         *      E.g.
         *      MyArray => [a => 'puppy', b => 'dog', c => [d => 'cat', e => 'kitten']]
         *  Example: 
         *      - MyArray[2][~'d'] returns 'cat'.
         *      - MyArray[~'e'] returns 'kitten'.                              
         */


        public static string HandleString(string str, KScriptContainer container)
        {
            if (Regex.IsMatch(str, ArrayMatch))
            {
                string id = Regex.Match(str, ArrayMatch).Groups[1].Value;
                MatchCollection matches = Regex.Matches(str, RetrieveItems);

                IArray current = container.GetMultidimensionalArrays()[id].GetRoot();

                foreach (Match match in matches)
                {
                    current = current.Find(StripKey(match.Groups[1].Value));
                }

                if (current != null)
                    container.Out(current.GetValue());
            }
            return str;
        }


        public static string StripKey(string val)
        {
            string value = val;
            if (val.StartsWith("~"))
                value = value.TrimStart('~');
            value = val.Trim('\'');
            return value;
        }

        // a => (b => 'kitten', c => (d => 'cat', e => 'dog', f => (g => 'animals')))
        public static void CreateExampleArray(KScriptContainer container)
        {
            ArrayBase values = new ArrayBase(
                new ArrayCollection("a", new List<IArray>(){
                    new ArrayItem("b","kitten"),
                    new ArrayCollection("c", new List<IArray>() {
                        new ArrayItem("d", "cat"),
                        new ArrayItem("e", "dog"),
                        new ArrayCollection("f", new List<IArray>() {
                            new ArrayItem("g","animals")
                        })
                    })
                })
            );

            container.AddMultidimensionalArray("myArray", values);
            //container.GetMultidimensionalArrays()["myArray"]
        }
    }
}
