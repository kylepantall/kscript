using System.Runtime.CompilerServices;
using System.Linq;
using System.Xml;
using KScript.Arguments;
using KScript.Arguments.Watcher;

namespace KScript.KScriptParserHandlers
{
    public class WatcherHandler : IParserHandler
    {

        // <watcher handle_method="once">
        //     <watchers>
        //         <watch for="name"/>
        //         <watch for="age" />
        //     </watchers>

        //     <rules>
        //         <rule condtion="@empty($name)">
        //             <echo>You must enter a name</echo>
        //         </rule>

        //         <rule condition="@more_less($name,gt,10)">
        //             <echo>Name can't be more than 10</echo>
        //         </rule>
        //     </rules>
        // </watcher>

        public WatcherHandler(KScriptContainer container) : base(container) { }

        /// <summary>
        /// Accepts the given XMLNode and KScriptObject If the KScript Object is of the type loadHtml
        /// </summary>
        /// <param name="obj">KScript object created using innerXML</param>
        /// <returns>If the HTMLObjectParser accepts the given KScriptObject</returns>
        public override bool IsAcceptedObject(KScriptObject obj) => obj.GetType().IsAssignableFrom(typeof(watcher));


        /// <summary>
        /// If accepted, changes the inner contents of the KScriptObject value to the innerXML of the given node.
        /// </summary>
        /// <param name="parentObject">The KScriptObject to perform functions and property changes with.</param>
        /// <param name="node">Node passed to the Parser Handler</param>
        /// <returns></returns>
        public override KScriptObject GenerateKScriptObject(KScriptObject parentObject, XmlNode node)
        {
            var watcher = new watcher();
            watcher.SetContainer(ParentContainer);

            var watchers = SelectNodes("//watchers/watch", node);
            var rules = SelectNodes("//rules/rule", node);

            foreach (XmlNode watch in watchers)
            {
                watcher.AddWatcher(
                    new watch(ParentContainer)
                    {
                        @for = watch.Attributes["for"].Value
                    }
                );
            }

            foreach (XmlNode rule in rules)
            {
                watcher.AddRule(
                    new rule(ParentContainer)
                    {
                        condition = rule.Attributes["condition"].Value
                    }
                );
            }

            return watcher;
        }
    }
}
