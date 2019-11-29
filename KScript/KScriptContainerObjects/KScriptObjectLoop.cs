using KScript.KScriptExceptions;

namespace KScript
{
    public class KScriptObjectLoop : KScriptObject
    {
        public string to { get; set; }

        public string math { get; set; }

        public string @while { get; set; }


        public override bool Run() => true;
        public override void Validate() => throw new KScriptNoValidationNeeded(this);
        public override string UsageInformation() => @"Used to store an array of KScriptObjects.";
    }
}
