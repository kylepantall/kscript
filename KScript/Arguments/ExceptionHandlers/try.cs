using KScript.Metadata;
using System;

namespace KScript.Arguments
{
    [KScriptDoc("Runs its body, handing errors to its <catch> elements. <finally> always runs.")]
    [KScriptElement(KScriptContentKind.Statements)]
    class @try : KScriptObject
    {
        [KScriptObjects.KScriptProperty("The command to execute upon failure.", true)]
        [KScript.Metadata.KScriptRetired("Use <catch> inside <try>.")]
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
