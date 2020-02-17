using KScript.Document;
using KScript.KScriptObjects;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace KScript.Arguments
{
    public class call : KScriptObject
    {
        [KScriptProperty("Used to declare which method to call", true)]
        public string method { get; set; }


        public string type { get; set; } = "method";

        [KScriptProperty("Used to pass properties used within this method", false)]
        public string args { get; set; }

        public override bool Run()
        {
            string _method = HandleCommands(method);

            if (!string.IsNullOrEmpty(args))
            {
                string[] _params = args.Split(',').Select(i => HandleCommands(i)).ToArray();

                List<string> keys = new List<string>();
                foreach (KeyValuePair<string, def> item in KScript().GetDefs())
                {
                    if (item.Key.StartsWith(_method) && item.Key.Contains("."))
                    {
                        keys.Add(item.Key);
                    }
                }

                for (int i = 0; i < _params.Length; i++)
                {
                    KScript().GetDefs()[keys[i]].Contents = _params[i];
                }
            }

            List<IKScriptDocumentNode> nodes = KScript().GetObjectStorageContainer().GetMethodCalls(_method);

            if (type == "thread")
            {
                new Thread(() =>
                {
                    Thread.CurrentThread.IsBackground = true;
                    /* run your code here */
                    nodes.ForEach(node => node.Run(KScript(), args, this));
                }).Start();
            }
            else
            {
                nodes.ForEach(node => node.Run(KScript(), args, this));
            }

            return true;
        }

        public override string UsageInformation() => "KScriptObject used to call methods contained using the <method> object";

        public override void Validate()
        {
            KScriptValidator validator = new KScriptValidator(KScript());
            validator.AddValidator(new KScriptValidationObject("args", true));
            validator.AddValidator(new KScriptValidationObject("method", false));
            validator.Validate(this);
        }
    }
}
