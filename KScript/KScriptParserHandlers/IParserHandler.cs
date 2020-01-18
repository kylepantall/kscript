using System.Xml;
using KScript.KScriptTypes;
using System.Linq;
using System.Collections.Generic;

namespace KScript.KScriptParserHandlers
{
    public abstract class IParserHandler : KScriptIO
    {
        public IParserHandler(KScriptContainer container) : base(container) { }

        /// <summary>
        /// Determines if the given KScriptObject is accepted by this parse handler.
        /// </summary>
        /// <param name="obj">Given KScriptObject</param>
        /// <returns>True or false based on the given KScriptObject</returns>
        public abstract bool IsAcceptedObject(KScriptObject obj);

        /// <summary>
        /// Used to generate the specified KScriptObject using given XML Node
        /// </summary>
        /// <returns></returns>
        public abstract KScriptObject GenerateKScriptObject(KScriptObject parentObject, XmlNode node);


        /// <summary>
        /// Used to return Xpath results from a node as a list of XmlNodes
        /// </summary>
        /// <param name="xpath">The xpath expression</param>
        /// <param name="node">The node to run the xpath expression on</param>
        /// <returns>List <XmlNode></returns>
        public List<XmlNode> SelectNodes(string xpath, XmlNode node) => node.SelectNodes(xpath).Cast<XmlNode>().ToList();
    }
}
