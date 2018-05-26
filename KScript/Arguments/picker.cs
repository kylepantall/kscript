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

        /// <summary>
        /// Persist on a valid directory
        /// </summary>
        public string persist { get; set; } = "no";

        public override void Run()
        {
            if (!string.IsNullOrWhiteSpace(contents)) Out(contents);
            string input = In();
            if (KScriptBoolHandler.Convert(persist))
            {
                while (!Directory.Exists(input))
                {
                    if (!string.IsNullOrEmpty(error_prompt)) Out(error_prompt);
                    input = In();
                }
            }
            else
            {
                if (!Directory.Exists(input))
                    throw new KScriptException("Directory doesn't exist");
            }
            if (!string.IsNullOrWhiteSpace(to)) Def(to).contents = input;
        }

        public override void Validate() => throw new KScriptNoValidationNeeded();
    }
}
