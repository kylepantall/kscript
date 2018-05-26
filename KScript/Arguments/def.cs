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

        public override void Run()
        {
            ParentContainer[id] = this;
            //Tells the parser to ignore any code here.
            throw new KScriptNoRunMethodImplemented();
        }

        public override void Validate()
        {
            throw new KScriptNoValidationNeeded();
        }
    }
}
