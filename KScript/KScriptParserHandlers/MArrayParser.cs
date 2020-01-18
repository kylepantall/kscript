using KScript.Arguments.Array;
using KScript.MultiArray;
using System.Xml;

namespace KScript.KScriptParserHandlers
{
    public class MArrayParser : IParserHandler
    {
        public MArrayParser(KScriptContainer container) : base(container) => ArrayCollection = new ArrayCollection();

        private readonly ArrayCollection ArrayCollection;
        public override KScriptObject GenerateKScriptObject(KScriptObject parentObject, XmlNode node)
        {
            Iterate(node, ArrayCollection);
            ArrayCollection x = ArrayCollection;
            return parentObject;
        }

        public void Iterate(XmlNode node, ArrayCollection parent)
        {
            if (node.HasChildNodes)
            {
                ArrayCollection col = new ArrayCollection();
                parent.Key = node.Name;
                foreach (XmlNode cnode in node.ChildNodes)
                {
                    Iterate(cnode, col);
                }

                parent.AddItem(col);
                return;
            }

            ArrayItem aItem = new ArrayItem(node.Name, node.InnerText);
            parent.AddItem(aItem);
        }
        public override bool IsAcceptedObject(KScriptObject obj) => obj.GetType().IsAssignableFrom(typeof(marray));
    }
}
