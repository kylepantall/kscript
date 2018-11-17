using KScript.Handlers;
using KScript.KScriptObjects;
using KScript.KScriptTypes.KScriptExceptions;
using System;

namespace KScript.Arguments
{
    public class input : KScriptObject
    {
        /// <summary>
        /// Type of input to expect...
        /// 
        /// - Email (to be implemented)
        /// - Phone number (to be implemented)
        /// - string (by default)
        /// </summary>
        [KScriptAcceptedOptions("email", "phone", "string", "number")]
        [KScriptProperty("The type of input to expect and validate.", false)]
        [KScriptExample("<input type=\"email\"> Enter an email... </input>")]
        [KScriptExample("<input type=\"phone\"> Enter a phone number... </input>")]
        [KScriptExample("<input type=\"string\"> Enter your name... </input>")]
        [KScriptExample("<input type=\"number\"> Enter your age... </input>")]
        public string type { get; set; }

        /// <summary>
        /// Where to save input to - uses "def" to save input.
        /// </summary>
        [KScriptProperty("Which def to use to save the input.")]
        public string to { get; set; }


        [KScriptProperty("Used to declare what input is accepted. You can use KScript Commands.")]
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
            {
                type = "string";
            }

            Out(Contents);

            if (Def(to) == null)
            {
                throw new KScriptException("KScriptDefNotFound", string.Format("Definition '{0}' has not been declared", to));
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
            {
                Console.Clear();
            }

            return true;
        }

        public override void Validate()
        {
            if (type != "string" && type != "number" && string.IsNullOrWhiteSpace(type))
            {
                throw new KScriptInvalidScriptType();
            }
        }

        public override string UsageInformation() => @"Used to obtain input from the console.";
    }
}
