using System.Xml.Linq;
using System.Collections.Generic;
using KScript.Arguments.Array;
using KScript.MultiArray;
using System.Xml;
using System.Linq;
using CsQuery.ExtensionMethods;

namespace KScript.KScriptParserHandlers
{
    public class MArrayParser : IParserHandler
    {
        public const string ARRAY_ITEM_KEY = "key";

        public MArrayParser(KScriptContainer container) : base(container) { }
        public override KScriptObject GenerateKScriptObject(KScriptObject parentObject, XmlNode node)
        {
            XElement xElement = XDocument.Parse(node.OuterXml).Root;
            ArrayCollection collection = new ArrayCollection(true);
            Iterate(xElement, collection);
            ParentContainer.GetMultidimensionalArrays().AddArray(node.Attributes["id"].Value, new ArrayBase(collection));
            return parentObject;
        }


        private void Iterate(XElement xElement, ArrayCollection parent)
        {
            if (xElement.HasElements)
            {
                ArrayCollection collection = new ArrayCollection();
                XAttribute key = xElement.Attribute(ARRAY_ITEM_KEY);

                if (key != null)
                {
                    collection.SetKey(key.Value);
                }

                xElement.Elements().ForEach(x => Iterate(x, collection));
                parent.AddItem(collection);
                return;
            }

            parent.AddItem(new ArrayItem(xElement.Attribute(ARRAY_ITEM_KEY).Value, xElement.Value));
        }
        public override bool IsAcceptedObject(KScriptObject obj) => obj.GetType().IsAssignableFrom(typeof(marray));
    }
}
