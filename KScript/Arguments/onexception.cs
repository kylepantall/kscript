using System;

namespace KScript.Arguments
{
    public class onexception : KScriptObject
    {
        public onexception() => RunImmediately = true;
        public string exception { get; set; }
        public string to { get; set; }

        public override bool Run()
        {
            throw new NotImplementedException();
            Def(to).Contents = "";
            return true;
        }

        public override string UsageInformation() => "Used to handle exceptions.";

        public override void Validate() => new KScriptTypes.KScriptExceptions.KScriptNoValidationNeeded();
    }
}
