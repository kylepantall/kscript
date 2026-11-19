using KScript.Metadata;
using System;

namespace KScript.Commands
{
    [KScriptDoc("A new GUID.")]
    class guid : KScriptCommand
    {
        public guid() { }

        public override string Calculate() => Guid.NewGuid().ToString();

        public override void Validate() => throw new KScriptExceptions.KScriptNoValidationNeeded(this);
    }
}
