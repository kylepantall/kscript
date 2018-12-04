using System.Collections.Generic;
using KScript.Handlers;
using KScript.KScriptObjects;

namespace KScript.Arguments
{
    /// <summary>
    /// Class used to handle the KScript Object echo of arrays.
    /// </summary>
    internal class aecho : KScriptObject
    {
        /// <summary>
        /// Defines the array to echo values from.
        /// </summary>
        [KScriptProperty("Specify the array ID to retrieve the values from.", true)]
        [KScriptAcceptedOptions("Array ID without prefix $")]
        [KScriptExample("myarrayid")]
        public string from { get; set; }

        /// <summary>
        /// Defines the format to use when outputting the array values.
        /// </summary>
        [KScriptProperty("Define the output format using $value and $index. Commands are accepted." +
            "By Default, format is '[$index] - $value'.", true)]
        public string format { get; set; } = @"[$index] - $value\n";

        public aecho() => ValidationType = ValidationTypes.DURING_PARSING;

        public override bool Run()
        {
            string _format = format;

            for (int i = 0; i < KScript().ArrayGet(this, from).Count; i++)
            {
                var array = KScript().ArrayGet(this, from);
                Out(KScriptReplacer.Replace(_format, new KeyValuePair<string, string>("index", i.ToString()), new KeyValuePair<string, string>("value", array[i])));
            }

            return true;
        }


        public override string UsageInformation() =>
            "Using the KScript format string, outputs an array in the given format.";


        public override void Validate()
        {
            KScriptValidator validator = new KScriptValidator(ParentContainer);
            validator.AddValidator(new KScriptValidationObject("format", false, "$index", "$value"));
            validator.AddValidator(new KScriptValidationObject("from", false, KScriptValidator.ExpectedInput.ArrayID));
            validator.Validate(this);
        }
    }
}
