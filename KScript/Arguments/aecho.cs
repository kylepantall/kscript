using System.Collections.Generic;
using KScript.Handlers;
using KScript.KScriptExceptions;
using KScript.KScriptObjects;

namespace KScript.Arguments
{
    class aecho : KScriptObject
    {
        [KScriptProperty("Specify the array ID to retrieve the values from.", true)]
        [KScriptAcceptedOptions("Array ID without prefix $")]
        [KScriptExample("myarrayid")]
        public string from { get; set; } = "array";

        [KScriptProperty("Define the output format using $value and $index. Commands are accepted." +
            "By Default, format is '[$index] - $value'.", true)]
        public string format { get; set; } = @"[$index] - $value\n";

        public override bool Run()
        {
            string _format = format;
            for (int i = 0; i < KScript().ArrayGet(from).Count; i++)
            {
                var array = KScript().ArrayGet(from);
                Out(KScriptReplacer.Replace(_format, new KeyValuePair<string, string>("index", i.ToString()), new KeyValuePair<string, string>("value", array[i])));
            }
            return true;
        }

        public override string UsageInformation() => "Using the KScript format string, outputs an array in the given format.";
        public override void Validate() => throw new KScriptNoValidationNeeded(this);
    }
}
