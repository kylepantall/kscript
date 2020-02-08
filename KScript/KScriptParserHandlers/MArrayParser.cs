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
        public MArrayParser(KScriptContainer container) : base(container)
        {
        }
        public override KScriptObject GenerateKScriptObject(KScriptObject parentObject, XmlNode node)
        {
            ArrayCollection collection = new ArrayCollection();
            Iterate(node, collection);
            ParentContainer.GetMultidimensionalArrays().AddArray(node.Attributes["id"].Value, new ArrayBase(collection));
            return parentObject;
        }

        private void Iterate(XmlNode node, ArrayCollection parent)
        {
            if (node.HasChildNodes)
            {
                ArrayCollection col = new ArrayCollection();
                node.ChildNodes.Cast<XmlNode>().ForEach(cnode => Iterate(cnode, col));
                parent.AddItem(col);
                return;
            }
        }
        public override bool IsAcceptedObject(KScriptObject obj) => obj.GetType().IsAssignableFrom(typeof(marray));
    }
}
