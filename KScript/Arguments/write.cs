using KScript.KScriptTypes.KScriptExceptions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace KScript.Arguments
{
    public class write : KScriptObject
    {
        /// <summary>
        /// Location to read from
        /// </summary>
        public string contents { get; set; }

        /// <summary>
        /// Location to write to
        /// </summary>
        public string to { get; set; }

        public write(string contents) => this.contents = contents;

        public override bool Run()
        {
            contents = HandleCommands(contents);
            to = HandleCommands(to);

            File.WriteAllText(to, contents);
            return true;
        }

        public override string UsageInformation() => "Used to write contents to a file using the properties ('contents' - text) and ('to' - file path).";

        public override void Validate() => throw new KScriptNoValidationNeeded();
    }
}
