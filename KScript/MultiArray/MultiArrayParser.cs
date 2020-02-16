using System.Xml.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Text.RegularExpressions;
using CsQuery.ExtensionMethods;

namespace KScript.MultiArray
{
    public class MultiArrayParser
    {
        public const string ARRAY_ITEM_KEY = "key";
        //~(\w+)\[.+\]
        public const string ArrayMatch = @"~(\w+)(\[(\w|\d+|\'\w+\')\])+";

        public const string RetrieveItems = @"\[(\d+|\'\w+\'|\~\'\w+\')\]";


        /* Supported formats:
         * Is Initialised by ~myArrayName[0] etc.
         * Accepted key selectors:
         *  - [number] - Index of item.
         *  - ['string'] - Key of item
         *  - [~'string'] (to-be-impl.) - Key of item (bubbles down the array from index).
         *  
         *      E.g.
         *      MyArray => [a => 'puppy', b => 'dog', c => [d => 'cat', e => 'kitten', a => [a => 'cub']]]
         *  
         *  Example: 
         *      - MyArray[2][~'d'] returns 'cat'.
         *      - MyArray[~'e'] returns 'kitten'.    
         *        Or MyArray['c'][~'a'] would return cub only bubbling down from index 'c' and not return 'puppy'                      
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

                    var result = needle.GetValue() is null ? item.Value.Groups[0].Value : needle.GetValue();
                    tmp = tmp.Replace($"[{{{item.Key}}}]", result);
                }
                return tmp;
            }
            catch (System.Exception ex)
            {
                container.HandleException(ex);
                return str;
            }
        }


        public static IArray GetArrayItem(string str, KScriptContainer container)
        {
            try
            {
                Match item = Regex.Match(str, ArrayMatch);

                MatchCollection m = Regex.Matches(item.Groups[0].Value, RetrieveItems);
                string id = Regex.Match(item.Groups[0].Value, ArrayMatch).Groups[1].Value;

                ArrayBase current = container.GetMultidimensionalArrays()[id];
                IArray needle = current.Find(m.Cast<Match>().ToArray());

                return needle;

            }
            catch (System.Exception ex)
            {
                container.HandleException(ex);
            }

            return null;
        }


        public static ArrayCollection ParseNode(XmlNode node)
        {
            XElement xElement = XDocument.Parse(node.OuterXml).Root;
            ArrayCollection collection = new ArrayCollection(true);
            Iterate(xElement, collection);
            return collection;
        }

        public static ArrayCollection ParseString(string xml)
        {
            XElement xElement = XDocument.Parse(xml).Root;
            ArrayCollection collection = new ArrayCollection(true);
            Iterate(xElement, collection);
            return collection;
        }

        private static void Iterate(XElement xElement, ArrayCollection parent)
        {
            XAttribute key = xElement.Attribute(ARRAY_ITEM_KEY);

            if (xElement.HasElements)
            {
                ArrayCollection collection = new ArrayCollection();

                if (key != null)
                {
                    collection.SetKey(key.Value);
                }

                xElement.Elements().ForEach(x => Iterate(x, collection));
                parent.AddItem(collection);
                return;
            }

            if (key != null)
            {
                parent.AddItem(new ArrayItem(xElement.Attribute(ARRAY_ITEM_KEY).Value, xElement.Value));
                return;
            }

            parent.AddItem(new ArrayItem(xElement.Value));
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
