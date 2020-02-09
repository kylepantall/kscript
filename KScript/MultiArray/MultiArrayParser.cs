using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace KScript.MultiArray
{
    public class MultiArrayParser
    {
        //~(\w+)\[.+\]
        const string ArrayMatch = @"~(\w+)(\[(\w|\d+|\'\w+\')\])+";

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
            try
            {
                MatchCollection matches = Regex.Matches(str, ArrayMatch);

                Dictionary<string, Match> Matched_Items = new Dictionary<string, Match>();

                string tmp = str;

                if (matches.Count < 1)
                {
                    return str;
                }

                foreach (Match match in matches)
                {
                    string uID = Guid.NewGuid().ToString();
                    tmp = tmp.Replace(match.Groups[0].Value, $"[{{{uID}}}]");
                    Matched_Items.Add(uID, match);
                }

                foreach (KeyValuePair<string, Match> item in Matched_Items)
                {
                    MatchCollection m = Regex.Matches(item.Value.Groups[0].Value, RetrieveItems);

                    if (m.Count < 1)
                    {
                        return str;
                    }

                    string id = Regex.Match(item.Value.Groups[0].Value, ArrayMatch).Groups[1].Value;
                    ArrayBase current = container.GetMultidimensionalArrays()[id];
                    IArray needle = current.Find(m.Cast<Match>().ToArray());
                    tmp = tmp.Replace($"[{{{item.Key}}}]", needle.GetValue());
                }
                return tmp;
            }
            catch (System.Exception ex)
            {
                container.HandleException(ex);
                return str;
            }
        }


        public static string StripKey(string val)
        {
            string tmp = val.TrimStart('~');
            tmp = tmp.Trim('\'');
            return tmp;
        }

        /* 'Values' => (
            'Size' => '1045MB', 
            'Files' => (
                0 => 'Users.xml', 
                1 => 'Admins.xml', 
                2 => (
                    'Archive' => 'App.exe'
                )
            )
        ) */
        public static void CreateExampleArray(KScriptContainer container)
        {
            ArrayBase values = new ArrayBase(
                new ArrayCollection("Values", new List<IArray>(){
                    new ArrayItem("Size","1045MB"),
                    new ArrayCollection("Files", new List<IArray>() {
                        new ArrayItem("Users.xml"),
                        new ArrayItem("Admins.xml"),
                        new ArrayCollection("Archive", new List<IArray>() {
                            new ArrayItem("App.exe")
                        })
                    })
                })
            );

            container.AddMultidimensionalArray("myArray", values);
        }
    }
}
