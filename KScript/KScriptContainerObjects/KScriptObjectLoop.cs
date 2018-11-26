using KScript.KScriptExceptions;

namespace KScript
{
    public class KScriptObjectLoop : KScriptObject
    {
        //Property to target
        public string to { get; set; }

        //Use the following command to update the value of the specified property
        public string math { get; set; }

        //While the property is equal to?
        public string @while { get; set; }


        public override bool Run() => true;
        public override void Validate() => throw new KScriptNoValidationNeeded(this);
        public override string UsageInformation() => @"Used to store an array of KScriptObjects.";
    }
}
