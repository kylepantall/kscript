using System;
using System.Linq;
using KScript.Handlers;
using KScript.KScriptObjects;

namespace KScript.Arguments
{
    public class echo : KScriptObject
    {
        public echo(string Contents) { this.Contents = Contents; }

        /// <summary>
        /// By default - no special treatment for echo contents
        /// </summary>
        private Types _type = Types.Normal;

        [KScriptProperty("Used to indicate whether a new line should trail after the echoed string. By default is yes.", false)]
        [KScriptAcceptedOptions("yes", "no", "y", "n", "1", "0", "t", "f", "true", "false")]
        public string trail_newline { get; set; } = "yes";

        [KScriptProperty("Used to indicate whether the content's trailing whitespace should be removed.", false)]
        [KScriptAcceptedOptions("yes", "no", "y", "n", "1", "0", "t", "f", "true", "false")]
        public string trim { get; set; } = "no";


        /// <summary>
        /// Defines the types of echo's 
        /// </summary>
        protected enum Types
        {
            /// <summary>
            /// Prints out individual lines surrounded by (').
            /// E.g. '1''2' would print:
            ///     1
            ///     2
            /// </summary>
            Paragraph,

            /// <summary>
            /// No specific treatment of contents.
            /// </summary>
            Normal
        }

        /// <summary>
        /// Defines the type of echo to init - use Paragraph type to print out individual lines surrounded by (').
        /// </summary>
        public string type
        {
            get
            {
                return _type.ToString().ToLower();
            }
            set
            {
                _type = (Types)Enum.Parse(typeof(Types), value, true);
            }
        }

        public override bool Run()
        {
            string _text = Contents;

            if (KScriptBoolHandler.Convert(trim))
            {
                _text = _text.Trim();
            }

            if (type == "paragraph")
            {
                _text = KScriptParagraphHandler.Parse(_text);
            }

            Out(_text);

            if (!string.IsNullOrEmpty(trail_newline) && KScriptBoolHandler.Convert(trail_newline))
            {
                Out();
            }
            return true;
        }

        public override void Validate()
        {
            KScriptValidator validator = new KScriptValidator(ParentContainer);
            validator.AddValidator(new KScriptValidationObject("Contents", true));
            validator.AddValidator(new KScriptValidationObject("trim", true, KScriptValidator.ExpectedInput.Bool));
            validator.AddValidator(new KScriptValidationObject("type", true, Enum.GetNames(typeof(Types)).Select(i => i.ToLower()).ToArray()));
            validator.Validate(this);
        }

        public override string UsageInformation() => @"Used to output values to the console.";
    }
}
