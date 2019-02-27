using System;

namespace KScript.Arguments
{
    class @try : KScriptObject
    {
        [KScriptObjects.KScriptProperty("The command to execute upon failure.", true)]
        public string @else { get; set; }

        public override bool Run()
        {
            try
            {
                HandleCommands(Contents);
                return true;
            }
            catch (Exception ex)
            {
                HandleCommands(@else);
                HandleException(ex, this);
                return false;
            }
        }

        public override string UsageInformation() => "Used to attempt a KScriptCommand, upon failure run an alternative KScriptCommand.";
        public override void Validate() => throw new KScriptExceptions.KScriptNoValidationNeeded(this);
    }
}
