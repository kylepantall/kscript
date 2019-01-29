using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;

namespace KScript.Commands
{
    class xpath : KScriptCommand
    {
        private readonly XmlDocument xmlDocument;
        private readonly string xPath;

        public xpath(string xml_string, string xPath)
        {
            xmlDocument = new XmlDocument();
            xmlDocument.LoadXml(xml_string);
            this.xPath = xPath;
        }

        public override string Calculate()
        {
            List<XmlNode> nodeLists = xmlDocument.SelectNodes(xPath).Cast<XmlNode>().ToList();

            StringBuilder builder = new StringBuilder();
            nodeLists.ForEach(i => builder.AppendLine(i.InnerXml));
            return builder.ToString();
        }

        public override void Validate()
        {
            throw new NotImplementedException();
        }
    }
}
