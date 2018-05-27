using KScript.KScriptTypes.KScriptExceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KScript.Arguments
{
    public class def : KScriptObject
    {
        public string id { get; set; }
        public string contents { get; set; }
        public def(string contents) => this.contents = contents;
        public override bool Run()
        {
            if (string.IsNullOrWhiteSpace(contents)) throw new KScriptNoRunMethodImplemented();
            else { contents = HandleCommands(contents); }
            return true;
        }
        public override void Validate() => throw new KScriptNoValidationNeeded();

        public override string UsageInformation() => @"Used to store values to the definition log within KScript." +
            "\nThe id attribute is to within other KScriptObjects to retrieve the values." +
            "\nE.g. '$tmp_name' can be used to retrieve the value of a def KScriptObject with the id 'tmp_name'.";
    }
}
