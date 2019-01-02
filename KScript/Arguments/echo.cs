using KScript.Handlers;
using KScript.KScriptExceptions;

namespace KScript.Arguments
{
    public class echo : KScriptObject
    {
        public echo(string Contents) { this.Contents = Contents; }

        [KScriptObjects.KScriptProperty("Used to indicate whether a new line should trail after the echoed string. By default is yes.", false)]
        [KScriptObjects.KScriptAcceptedOptions("yes", "no", "y", "n", "1", "0", "t", "f", "true", "false")]
        public string trail_newline { get; set; } = "yes";

        public override bool Run()
        {
            Out(Contents);
            if (!string.IsNullOrEmpty(trail_newline) && KScriptBoolHandler.Convert(trail_newline))
            {
                Out();
            }
            return true;
        }

        public override void Validate() => throw new KScriptNoValidationNeeded(this);
        public override string UsageInformation() => @"Used to output values to the KScript log.";
    }
}
