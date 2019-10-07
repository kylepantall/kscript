using KScript.MultiArray;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KScript.Handlers
{
    internal class MArrayHandler
    {

        public static JTokenType[] ValueTypes = new JTokenType[] {
            JTokenType.String,
        };

        public static JTokenType[] IterativeTypes = new JTokenType[] {
            JTokenType.Object,
            JTokenType.Array,
            JTokenType.Property,
        };

        public static bool ConvertJSONToMArray(string array_id, string json, KScriptContainer container)
        {
            var jarray = JArray.Parse(json).Root;
            var collection = new ArrayCollection();

            Iterate(jarray, collection);


            container.AddMultidimensionalArray(array_id, new ArrayBase(collection));
            return true;
        }

        public static void Iterate(JToken node, ArrayCollection parent)
        {
            if (node.HasValues && IterativeTypes.Contains(node.Type))
            {
                var collection = new ArrayCollection();
                foreach (var item in node.Children())
                {
                    parent.AddItem(collection);
                    Iterate(item, collection);
                }

                return;
            }

            if (ValueTypes.Contains(node.Type))
            {
                var aItem = new ArrayItem("Key", node.Value<string>());
                parent.AddItem(aItem);
            }
        }

        /**
         * Look at root. If contains children, new container, loop them with new container.
         * If single item, add to current collection.
         */
    }
}
