using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Encryption
{
    public class md5encrypt : KScript.KScriptObject
    {
        public string contents { get; set; }

        public md5encrypt(string contents) => this.contents = contents;

        public override bool Run()
        {
            Out(Encryption.GetMd5Hash(MD5.Create(), contents) + "\n");
            return true;
        }

        public override string UsageInformation() => "This is an encryption extension...";

        public override void Validate() => throw new KScript.KScriptTypes.KScriptExceptions.KScriptNoValidationNeeded();
    }
}
