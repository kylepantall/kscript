using KScript.KScriptObjects;
using KScript.KScriptExceptions;

namespace KScript.Arguments
{
    public class group : KScriptObject
    {
        public new KScriptObject Contents { get; set; }

        [KScriptProperty("Use this attribute to explain this group of script commands for more clarity", false)]
        public string description { get; set; }

        [KScriptProperty("Used to give the group a unique identity for other KScript objects and commands", false)]
        public string id { get; set; }

        public override bool Run() => true;

        public override string UsageInformation() => "Used to contain KScript Objects with familiar functionality.";

        public override void Validate() => throw new KScriptNoValidationNeeded(this);
    }
}
