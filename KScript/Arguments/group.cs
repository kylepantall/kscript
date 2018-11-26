using KScript.KScriptObjects;
using KScript.KScriptExceptions;

namespace KScript.Arguments
{
    public class group : KScriptConditional
    {
        public new KScriptObject Contents { get; set; }

        [KScriptProperty("Use this attribute to explain this group of script commands for more clarity", false)]
        public string description { get; set; }

        [KScriptProperty("Used to give the group a unique identity for other KScript objects and commands", false)]
        public string id { get; set; }

        public override bool Run()
        {
            if (Contents != null)
            {
                Contents.Run();
            }

            return true;
        }

        public override void Validate() => throw new KScriptNoValidationNeeded(this);
    }
}
