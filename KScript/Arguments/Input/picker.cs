using System;
using System.IO;
using KScript.KScriptExceptions;
using KScript.KScriptObjects;

namespace KScript.Arguments
{
    public class picker : KScriptObject
    {
        public picker(string Contents) => this.Contents = Contents;

        [KScriptProperty("The def value to store the selected value to.", true)]
        public string to { get; set; }

        [KScriptProperty("The prompt upon receiving incorrect input.", false)]
        public string error_prompt { get; set; }

        private type_options _type = type_options.directory;

        /// <summary>
        /// Used to inform the picker whether we're checking for a file path or a directory
        /// </summary>
        [KScriptProperty("The type of directory to expect.", true)]
        [KScriptAcceptedOptions("directory", "file")]
        public string type
        {
            get => _type.ToString();
            set
            {
                type_options val = type_options.directory;
                if (Enum.TryParse(value, out val))
                    _type = val;
                else
                    _type = type_options.directory;
            }
        }

        private type_options GetType_Options() => _type;

        /// <summary>
        /// Persist on a valid directory/file path
        /// </summary>
        /// 
        [KScriptProperty("Indicates whether to persist for a correct directory or file.", true)]
        public string persist { get; set; } = "no";


        enum type_options
        {
            /// <summary>
            /// Informs the picker to only allow directory path input
            /// </summary>
            directory,
            /// <summary>
            /// Informs the picker to only allow file path input
            /// </summary>
            file
        }

        public override bool Run()
        {
            if (!string.IsNullOrWhiteSpace(to))
                CreateDef(to, "");

            if (!string.IsNullOrWhiteSpace(Contents))
                Out(Contents);

            string input = In();

            if (ToBool(persist))
                switch (GetType_Options())
                {
                    case type_options.directory:
                        while (!Directory.Exists(input))
                        {
                            if (!string.IsNullOrEmpty(error_prompt))
                                Out(error_prompt);
                            input = In();
                        }
                        break;
                    default:
                        while (!File.Exists(input))
                        {
                            if (!string.IsNullOrEmpty(error_prompt))
                                Out(error_prompt);
                            input = In();
                        }
                        break;
                }
            else
                switch (GetType_Options())
                {
                    case type_options.directory:
                        if (!Directory.Exists(input))
                            throw new KScriptDirectoryNotFound(this, string.Format("Directory '{0}' doesn't exist", input));
                        break;
                    default:
                        if (!File.Exists(input))
                            throw new KScriptDirectoryNotFound(this, string.Format("File '{0}' doesn't exist", input));
                        break;
                }

            Def(to).Contents = input;

            return true;
        }

        public override void Validate()
        {
            KScriptValidator validator = new KScriptValidator(ParentContainer);
            validator.AddValidator(new KScriptValidationObject("to", false));
            validator.AddValidator(new KScriptValidationObject("type", false, Enum.GetNames(typeof(type_options))));
            validator.AddValidator(new KScriptValidationObject("persist", false, KScriptValidator.ExpectedInput.Bool));
            validator.AddValidator(new KScriptValidationObject("error_prompt", true));
            validator.Validate(this);

        }
        public override string UsageInformation() => @"Used to obtain a directory or file path from the user.";
    }
}
