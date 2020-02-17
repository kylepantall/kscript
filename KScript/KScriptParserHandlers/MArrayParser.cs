using System.Xml.Linq;
using KScript.Arguments.Array;
using KScript.MultiArray;
using System.Xml;
using CsQuery.ExtensionMethods;

namespace KScript.KScriptParserHandlers
{
    public class MArrayParser : IParserHandler
    {
        public const string ARRAY_ITEM_KEY = "key";

        public MArrayParser(KScriptContainer container) : base(container) { }
        public override KScriptObject GenerateKScriptObject(KScriptObject parentObject, XmlNode node)
        {
            var collection = MultiArrayParser.ParseNode(node);

            KScript().GetMultidimensionalArrays().AddArray(
                node.Attributes["id"].Value,
                new ArrayBase(collection)
            );

            return parentObject;
        }

        public override bool IsAcceptedObject(KScriptObject obj) => obj.GetType().IsAssignableFrom(typeof(marray));
    }
}
