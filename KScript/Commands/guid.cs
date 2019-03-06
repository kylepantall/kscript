using System;

namespace KScript.Commands
{
    class guid : KScriptCommand
    {
        public guid() { }

        public override string Calculate() => Guid.NewGuid().ToString();

        public override void Validate() => throw new KScriptExceptions.KScriptNoValidationNeeded(this);
    }
}
