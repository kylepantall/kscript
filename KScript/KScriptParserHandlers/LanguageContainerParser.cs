using System;
using System.Xml;
using KScript.Arguments;

namespace KScript.KScriptParserHandlers
{
    class LanguageContainerParser : IParserHandler
    {
        public LanguageContainerParser(KScriptContainer container) : base(container) { }


        public override KScriptObject GenerateKScriptObject(KScriptObject parentObject, XmlNode node)
        {
            Out(node.FirstChild.Name);

            return parentObject;
        }

        public override bool IsAcceptedObject(KScriptObject obj) => (obj.GetType().IsAssignableFrom(typeof(languages)));

    }
}
