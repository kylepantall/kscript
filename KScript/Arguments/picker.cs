using KScript.Handlers;
using KScript.KScriptExceptions;
using System;
using System.IO;

namespace KScript.Arguments
{
    public class picker : KScriptObject
    {
        public picker(string Contents) => this.Contents = Contents;

        [KScriptObjects.KScriptProperty("The def value to store the selected value to.", true)]
        public string to { get; set; }

        [KScriptObjects.KScriptProperty("The prompt upon receiving incorrect input.", false)]
        public string error_prompt { get; set; }

        private type_options _type = type_options.directory;

        /// <summary>
        /// Used to inform the picker whether we're checking for a file path or a directory
        /// </summary>
        /// 
        [KScriptObjects.KScriptProperty("The type of directory to expect.", true)]
        [KScriptObjects.KScriptAcceptedOptions("directory", "file")]
        public string type
        {
            get { return Enum.GetName(typeof(type_options), _type); }
            set { _type = (type_options)Enum.Parse(typeof(type_options), value); }
        }

        private type_options GetType_Options() => _type;

        /// <summary>
        /// Persist on a valid directory/file path
        /// </summary>
        /// 
        [KScriptObjects.KScriptProperty("Indicates whether to persist for a correct directory or file.", true)]
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
            if (!string.IsNullOrWhiteSpace(Contents))
            {
                Out(Contents);
            }

            string input = In();
            if (KScriptBoolHandler.Convert(persist))
            {
                switch (GetType_Options())
                {
                    case type_options.directory:
                        while (!Directory.Exists(input))
                        {
                            if (!string.IsNullOrEmpty(error_prompt))
                            {
                                Out(error_prompt);
                            }

                            input = In();
                        }
                        break;
                    default:
                        while (!File.Exists(input))
                        {
                            if (!string.IsNullOrEmpty(error_prompt))
                            {
                                Out(error_prompt);
                            }

                            input = In();
                        }
                        break;
                }
            }
            else
            {
                switch (GetType_Options())
                {
                    case type_options.directory:
                        if (!Directory.Exists(input))
                        {
                            throw new KScriptDirectoryNotFound(this, string.Format("Directory '{0}' doesn't exist", input));
                        }

                        break;
                    default:
                        if (!File.Exists(input))
                        {
                            throw new KScriptDirectoryNotFound(this, string.Format("File '{0}' doesn't exist", input));
                        }

                        break;
                }
            }
            if (!string.IsNullOrWhiteSpace(to))
            {
                Def(to).Contents = input;
            }

            return true;
        }

        public override void Validate() => throw new KScriptNoValidationNeeded(this);
        public override string UsageInformation() => @"Used to obtain a directory or file path from the user.";
    }
}
