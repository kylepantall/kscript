using System.Globalization;
using KScript.KScriptExceptions;

namespace KScript.Arguments
{
    [KScriptObjects.KScriptHideObject]
    public class kscript : KScriptObject
    {
        [KScriptObjects.KScriptProperty("Used to indicate whether the script is quiet - if false, no script timing is reported to the user.", false)]
        [KScriptObjects.KScriptAcceptedOptions("yes", "no", "y", "n", "1", "0", "t", "f", "true", "false")]
        public string quiet { get; set; } = "no";

        [KScriptObjects.KScriptProperty("Used to indicate whether the console should print information regarding commands or KScript objects.", false)]
        [KScriptObjects.KScriptAcceptedOptions("yes", "no", "y", "n", "1", "0", "t", "f", "true", "false")]
        public string print_info { get; set; } = "no";

        [KScriptObjects.KScriptProperty("Used to indicate whether the console should wait after finishing.", false)]
        [KScriptObjects.KScriptAcceptedOptions("yes", "no", "y", "n", "1", "0", "t", "f", "true", "false")]
        public string on_finish_wait { get; set; } = "yes";

        [KScriptObjects.KScriptProperty("Used to indicate the language of the machine", false)]
        public string language { get; set; }

        public string dynamic_defs { get; set; } = "no";

        public string throw_exceptions { get; set; } = "no";

        public override bool Run()
        {
            ParentContainer.Properties.Quiet = ToBool(quiet);
            ParentContainer.Properties.WaitOnFinish = ToBool(on_finish_wait);
            ParentContainer.Properties.DynamicDefs = ToBool(dynamic_defs);
            ParentContainer.Properties.ThrowAllExceptions = ToBool(throw_exceptions);

            if (string.IsNullOrWhiteSpace(ParentContainer.Properties.Language))
            {
                ParentContainer.Properties.Language = (language == "auto" ? CultureInfo.CurrentCulture.Name : language);
            }

            if (ToBool(print_info))
                ParentContainer.PrintInfo();

            return true;
        }

        public override void Validate() => throw new KScriptNoValidationNeeded(this);
        public override string UsageInformation() => @"Used to declare KScriptParser values and inform the parser where to find the script commands.";
    }
}
