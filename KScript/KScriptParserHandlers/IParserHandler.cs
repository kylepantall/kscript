using System.Xml;
using KScript.KScriptTypes;

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
    }
}
