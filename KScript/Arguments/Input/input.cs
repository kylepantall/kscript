using System;
using KScript.Handlers;
using KScript.KScriptExceptions;
using KScript.KScriptObjects;

namespace KScript.Arguments
{
    public class input : KScriptObject
    {
        private input_types _type = input_types.text;
        /// <summary>
        /// Type of input to expect...
        /// 
        /// - Email (to be implemented)
        /// - Phone number (to be implemented)
        /// - string (by default)
        /// </summary>
        [KScriptAcceptedOptions("text", "number")]
        [KScriptProperty("The type of input to expect and validate.", false)]
        [KScriptExample("<input type=\"text\"> Enter an email... </input>")]
        [KScriptExample("<input type=\"text\"> Enter your name... </input>")]
        [KScriptExample("<input type=\"number\"> Enter your age... </input>")]
        public string type
        {
            get => _type.ToString();
            set
            {
                input_types val;
                if (Enum.TryParse(value, out val))
                {
                    _type = val;
                    return;
                }
                _type = input_types.text;
            }
        }

        public enum input_types
        {
            text, number
        }

        /// <summary>
        /// Where to save input to - uses "def" to save input.
        /// </summary>
        [KScriptProperty("Which def to use to save the input.")]
        [KScriptExample("my_param_id")]
        public string to { get; set; }


        [KScriptProperty("Used to declare what input is accepted. You can use KScript Commands.", false)]
        public string accepted_values { get; set; }

        /// <summary>
        /// Clear-After-Input command;
        /// </summary>
        /// 
        [KScriptProperty("Specifies whether the console clear after input", false)]
        public string cai { get; set; } = "no";

        public input(string content) => Contents = content;

        public override bool Run()
        {
            if (string.IsNullOrWhiteSpace(type))
                type = "string";


            Out(Contents);

            if (!string.IsNullOrWhiteSpace(to))
            {
                CreateDef(to);
                if (Def(to) is null)
                    throw new KScriptException("KScriptDefNotFound", $"Definition '{to}' has not been declared");
            }

            switch (type.ToLower())
            {
                case "number":
                    ParentContainer[to].Contents = InNumber().ToString();
                    break;
                case "multiline":
                    ParentContainer[to].Contents = In();
                    break;
                default:
                    ParentContainer[to].Contents = In();
                    break;
            }

            if (ParentContainer.Properties.ClearAfterInput || KScriptBoolHandler.Convert(cai))
                Console.Clear();

            return true;
        }

        public override void Validate()
        {
            KScriptValidator validator = new KScriptValidator(ParentContainer);
            validator.AddValidator(new KScriptValidationObject("type", true, Enum.GetNames(typeof(input_types))));
            validator.Validate(this);
        }

        public override string UsageInformation() => @"Used to obtain input from the console.";
    }
}
