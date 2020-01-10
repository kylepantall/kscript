using System.Collections.Generic;
using System.Xml;
using KScript.Arguments;

namespace KScript.KScriptParserHandlers
{
    class LanguageContainerParser : IParserHandler
    {
        public LanguageContainerParser(KScriptContainer container) : base(container) => Values = new Dictionary<string, List<KeyValuePair<string, string>>>();

        private readonly Dictionary<string, List<KeyValuePair<string, string>>> Values;

        public override KScriptObject GenerateKScriptObject(KScriptObject parentObject, XmlNode node)
        {
            XmlNodeList nodes = node.ChildNodes;

            foreach (XmlNode cnode in nodes)
            {
                foreach (XmlNode ccnode in cnode.ChildNodes)
                {
                    if (ccnode.Name == "value")
                    {
                        var val = new KeyValuePair<string, string>(ccnode.Attributes["id"].InnerText, ccnode.InnerText);

                        if (!Values.ContainsKey(cnode.Name))
                        {
                            Values.Add(cnode.Name, new List<KeyValuePair<string, string>>());
                        }

                        Values[cnode.Name].Add(val);
                    }
                }
            }
            parentObject.GetValueStore()["data"] =  Values;
            return parentObject;
        }

        public override bool IsAcceptedObject(KScriptObject obj) => (obj.GetType().IsAssignableFrom(typeof(languages)));

    }
}
