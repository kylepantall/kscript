using System;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using KScript.KScriptTypes.KScriptExceptions;
using KScript.Handlers;

namespace KScript.Arguments
{
    public class picker : KScriptObject
    {
        public picker(string contents) => this.contents = contents;

        public string contents { get; set; }
        public string to { get; set; }
        public string error_prompt { get; set; }

        private type_options _type = type_options.directory;
        /// <summary>
        /// Used to inform the picker whether we're checking for a file path or a directory
        /// </summary>
        public string type
        {
            get { return Enum.GetName(typeof(type_options), _type); }
            set { _type = (type_options)Enum.Parse(typeof(type_options), value); }
        }

        private type_options GetType_Options() => _type;

        /// <summary>
        /// Persist on a valid directory/file path
        /// </summary>
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
            if (!string.IsNullOrWhiteSpace(contents)) Out(contents);
            string input = In();
            if (KScriptBoolHandler.Convert(persist))
            {
                switch (GetType_Options())
                {
                    case type_options.directory:
                        while (!Directory.Exists(input))
                        {
                            if (!string.IsNullOrEmpty(error_prompt)) Out(error_prompt);
                            input = In();
                        }
                        break;
                    default:
                        while (!File.Exists(input))
                        {
                            if (!string.IsNullOrEmpty(error_prompt)) Out(error_prompt);
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
                            throw new KScriptException("Directory doesn't exist");
                        break;
                    default:
                        if (!File.Exists(input))
                            throw new KScriptException("File doesn't exist");
                        break;
                }
            }
            if (!string.IsNullOrWhiteSpace(to)) Def(to).contents = input;
            return true;
        }

        public override void Validate() => throw new KScriptNoValidationNeeded();
        public override string UsageInformation() => @"Used to obtain a directory or file path from the user.";
    }
}
