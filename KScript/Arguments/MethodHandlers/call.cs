using KScript.Metadata;
using KScript.Handlers;
using KScript.Document;
using KScript.KScriptObjects;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace KScript.Arguments
{
    [KScriptDoc("Calls a function as a statement.", Example = "<call fn=\"greet\" args=\"'World'\" />")]
    [KScriptElement(KScriptContentKind.Empty)]
    [KScriptAttribute("fn", KScriptValueKind.FunctionReference, "The function to call.", Required = true)]
    [KScriptAttribute("args", KScriptValueKind.ExpressionList, "Arguments, comma separated.")]
    public class call : KScriptObject
    {
        [KScriptProperty("Used to declare which method to call", true)]
        [KScriptRetired("Use fn=\"...\" with a <fn> function.", Rename = "fn")]
        public string method { get; set; }


        public string type { get; set; } = "method";

        [KScriptProperty("Used to pass properties used within this method", false)]
        public string args { get; set; }

        public override bool Run()
        {
            string _method = HandleCommands(method);

            if (!string.IsNullOrEmpty(args))
            {
                // Split on top-level commas only, so quoted strings and nested commands may contain commas.
                string[] _params = KScriptCommandHandler.EvaluateArgumentList(args, KScript(), this);

                // Bind arguments to the method's declared parameter names, in declaration order.
                string[] names = GetDeclaredParameters(_method);

                if (_params.Length > names.Length)
                {
                    throw new KScriptExceptions.KScriptException(this,
                        $"Method '{_method}' accepts {names.Length} argument(s) but {_params.Length} were given.");
                }

                for (int i = 0; i < _params.Length; i++)
                {
                    KScript()[$"{_method}.{names[i]}"] = new def(_params[i]);
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

        private string[] GetDeclaredParameters(string methodName)
        {
            KScriptMethodWrapper wrapper = KScript().GetObjectStorageContainer().GetObject<KScriptMethodWrapper>($"{Global.GlobalIdentifiers.CALLS}:{methodName}");

            if (wrapper == null || string.IsNullOrWhiteSpace(wrapper.@params))
            {
                return new string[0];
            }

            return wrapper.@params.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).ToArray();
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
