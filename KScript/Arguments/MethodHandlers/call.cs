using System.Collections.Generic;
using System.Linq;
using KScript.Document;
using KScript.KScriptObjects;

namespace KScript.Arguments
{
    public class call : KScriptObject
    {
        [KScriptProperty("Used to declare which method to call", true)]
        public string method { get; set; }


        [KScriptProperty("Used to pass properties used within this method", false)]
        public string args { get; set; }

        public override bool Run()
        {
            string _method = HandleCommands(method);

            if (!string.IsNullOrEmpty(args))
            {
                string[] _params = args.Split(',').Select(i => HandleCommands(i)).ToArray();

                List<string> keys = new List<string>();
                foreach (var item in ParentContainer.GetDefs())
                {
                    if (item.Key.StartsWith(_method) && item.Key.Contains("."))
                    {
                        keys.Add(item.Key);
                    }
                }

                for (int i = 0; i < _params.Length; i++)
                {
                    ParentContainer.GetDefs()[keys[i]].Contents = _params[i];
                }
            }

            List<IKScriptDocumentNode> nodes = ParentContainer.GetObjectStorageContainer().GetMethodCalls(_method);
            nodes.ForEach(node => node.Run(ParentContainer, args, this));

            return true;
        }

        public override string UsageInformation() => "KScriptObject used to call methods contained using the <method> object";

        public override void Validate()
        {
            KScriptValidator validator = new KScriptValidator(ParentContainer);
            validator.AddValidator(new KScriptValidationObject("args", true));
            validator.AddValidator(new KScriptValidationObject("method", false));
            validator.Validate(this);
        }
    }
}
