using KScript.Arguments.Array;
using KScript.MultiArray;
using System.Xml;

namespace KScript.KScriptParserHandlers
{
    public class MArrayParser : IParserHandler
    {
        public MArrayParser(KScriptContainer container) : base(container) => ArrayCollection = new ArrayCollection();

        private ArrayCollection ArrayCollection;
        public override KScriptObject GenerateKScriptObject(KScriptObject parentObject, XmlNode node)
        {
            Iterate(node, ArrayCollection);
            var x = ArrayCollection;
            return parentObject;
        }

        public void Iterate(XmlNode node, ArrayCollection parent)
        {
            if (node.HasChildNodes)
            {
                var col = new ArrayCollection();
                parent.Key = (node.Attributes?["key"]?.InnerText) ?? "";
                foreach (XmlNode cnode in node.ChildNodes)
                    Iterate(cnode, col);
                parent.AddItem(col);
            }
            else
            {
                var aItem = new ArrayItem((node.Attributes?["key"]?.InnerText) ?? "", node.InnerText);
                parent.AddItem(aItem);
            }
        }

        //public override bool IsAcceptedObject(KScriptObject obj) => obj.GetType().IsAssignableFrom(typeof(marray));
        public override bool IsAcceptedObject(KScriptObject obj)
        {
            return false;
        }
    }
}
