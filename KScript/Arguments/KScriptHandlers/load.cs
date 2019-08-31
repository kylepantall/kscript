using KScript.KScriptExceptions;

namespace KScript.Arguments
{
    public class load : KScriptObject
    {
        [KScriptObjects.KScriptProperty("Used to specify the file location of where to load the script from.", true)]
        public string location { get; set; }


        [KScriptObjects.KScriptProperty("Used to signify whether or not to recreate KScript properties and script containers", false)]
        public string refresh { get; set; } = "y";

        public override bool Run()
        {
            KScript().Stop();
            KScript().Parser.Load(HandleCommands(location), ToBool(refresh));
            KScript().Parser.Parse();
            return true;
        }

        public override string UsageInformation() => "Used to load KScript's dynamically from an existing KScript";

        public override void Validate()
        {
            if (string.IsNullOrEmpty(location))
            {
                throw new KScriptException("The property 'location' is missing and is required for this KScript Object");
            }
            else
            {
                throw new KScriptNoValidationNeeded(this);
            }
        }
    }
}
