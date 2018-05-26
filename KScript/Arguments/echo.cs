using KScript.Handlers;
using KScript.KScriptTypes.KScriptExceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace KScript.Arguments
{
    public class echo : KScriptObject
    {
        public echo(string contents) { this.contents = contents; }

        public string trail_newline { get; set; } = "yes";
        public string contents { get; set; }

        public override void Run()
        {
            Out(contents);
            if (!string.IsNullOrEmpty(trail_newline) && KScriptBoolHandler.Convert(trail_newline)) Out();
        }

        public override void Validate()
        {
            throw new KScriptNoValidationNeeded();
        }
    }
}
