﻿using KScript.Arguments;
using System.Xml;

namespace KScript.KScriptParserHandlers
{
    public class HtmlObjectParser : IParserHandler
    {
        public HtmlObjectParser(KScriptContainer container) : base(container) { }

        /// <summary>
        /// Accepts the given XMLNode and KScriptObject If the KScript Object is of the type loadHtml
        /// </summary>
        /// <param name="obj">KScript object created using innerXML</param>
        /// <returns>If the HTMLObjectParser accepts the given KScriptObject</returns>
        public override bool IsAcceptedObject(KScriptObject obj) => false; //obj.GetType().IsAssignableFrom(typeof(loadHtml)) || obj.GetType().IsAssignableFrom(typeof(updateContent));


        /// <summary>
        /// If accepted, changes the inner contents of the KScriptObject value to the innerXML of the given node.
        /// </summary>
        /// <param name="parentObject">The KScriptObject to perform functions and property changes with.</param>
        /// <param name="node">Node passed to the Parser Handler</param>
        /// <returns></returns>
        public override KScriptObject GenerateKScriptObject(KScriptObject parentObject, XmlNode node)
        {
            string html_contents = node.InnerXml;
            parentObject.Contents = html_contents;
            return parentObject;
        }
    }
}
