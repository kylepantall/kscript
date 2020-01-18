using KScript.Arguments.Output;
using System.Xml;

namespace KScript.KScriptParserHandlers
{
    public class MenuHandler : IParserHandler
    {
        public MenuHandler(KScriptContainer container) : base(container) { }

        /** 
         * 
         * <menu to="variable_id">
         *  <option>Example</option>
         *  <option>Example 2</option>
         * </menu>
         */
        public override KScriptObject GenerateKScriptObject(KScriptObject parentObject, XmlNode node)
        {
            //var menuOptions = from m in node.ChildNodes.Cast<XmlNode>()
            //                  where m.Name == "option"
            //                  select m;

            //var availableOptions = from menuOption in menuOptions
            //                       where menuOption.Attributes["value"] != null
            //                       select menuOption.Attributes["value"].Value;


            //while (!availableOptions.Contains(input))
            //{
            //    Ask for input again.
            //}

            return parentObject;
        }

        public override bool IsAcceptedObject(KScriptObject obj) => obj.GetType().IsAssignableFrom(typeof(menu));
    }
}
