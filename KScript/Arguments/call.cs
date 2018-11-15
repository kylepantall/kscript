using KScript.Document;
using System.Collections.Generic;

namespace KScript.Arguments
{
    public class call : KScriptObject
    {
        [KScriptObjects.KScriptProperty("Used to declare which method to call", true)]
        public string method { get; set; }

        [KScriptObjects.KScriptProperty("Used to pass properties used within this method", false)]
        public string args { get; set; }

        public override bool Run()
        {
            string[] _params = args.Split(',');
            string _method = HandleCommands(method);

            List<string> keys = new List<string>();
            foreach (var item in ParentContainer.defs)
            {
                if (item.Key.StartsWith(_method) && item.Key.Contains("."))
                {
                    keys.Add(item.Key);
                }
            }

            for (int i = 0; i < _params.Length; i++)
            {
                ParentContainer.defs[keys[i]].Contents = _params[i];
            }

            List<IKScriptDocumentNode> nodes = ParentContainer.ObjectStorageContainer.GetMethodCalls(_method);
            foreach (IKScriptDocumentNode node in nodes)
            {
                node.Run(ParentContainer, args);
            }


            return true;
        }

        public override string UsageInformation() => "KScriptObject used to call methods contained using the <method> object";

        public override void Validate() => throw new KScriptTypes.KScriptExceptions.KScriptNoValidationNeeded();
    }
}
