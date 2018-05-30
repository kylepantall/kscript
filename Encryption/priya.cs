using KScript;
using KScript.KScriptTypes.KScriptExceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Encryption
{
    public class priya : KScriptObject
    {
        public string contents { get; set; }
        public string add { get; set; }

        public priya(string contents) => this.contents = contents;

        public override bool Run()
        {
            int number = int.Parse(contents);
            int number_to_add = int.Parse(add);

            Out((number + number_to_add).ToString());
            return true;
        }

        public override string UsageInformation() => "Yo fam, I'm Priya and I'll kill a bitch idec.";

        public override void Validate()
        {
            if (add != "7") throw new KScriptException("Number must be 7");
        }
    }
}
