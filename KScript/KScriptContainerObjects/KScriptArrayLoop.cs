using KScript.KScriptExceptions;

namespace KScript
{
    public class KScriptArrayLoop : KScriptObject
    {
        [KScriptObjects.KScriptProperty("Used to define which property when storing the looped array item.", true)]
        public string to { get; set; }

        public override bool Run() => true;
        public override void Validate() => throw new KScriptNoValidationNeeded(this);
        public override string UsageInformation() => @"Used to loop through an KScript Array Object.";
    }
}
