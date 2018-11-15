using KScript.KScriptTypes.KScriptExceptions;

namespace KScript.Arguments
{
    [KScriptObjects.KScriptHideObject]
    public class kscript : KScriptObject
    {
        [KScriptObjects.KScriptProperty("Used to indicate whether the script is quiet - if false, no script timing is reported to the user.", false)]
        [KScriptObjects.KScriptAcceptedOptions("yes", "no", "y", "n", "1", "0", "t", "f", "true", "false")]
        public string quiet { get; set; } = "yes";

        [KScriptObjects.KScriptProperty("Used to indicate whether the console should print information regarding commands or KScript objects.", false)]
        [KScriptObjects.KScriptAcceptedOptions("yes", "no", "y", "n", "1", "0", "t", "f", "true", "false")]
        public string print_info { get; set; } = "no";

        [KScriptObjects.KScriptProperty("Used to indicate whether the console should wait after finishing.", false)]
        [KScriptObjects.KScriptAcceptedOptions("yes", "no", "y", "n", "1", "0", "t", "f", "true", "false")]
        public string on_finish_wait { get; set; } = "yes";

        public override bool Run()
        {
            bool _quiet = false, _print_info = false, _on_finish_wait = true;

            if (quiet != null)
            {
                _quiet = ToBool(quiet);
            }
            else
            {
                _quiet = false;
            }

            if (print_info != null)
            {
                _print_info = ToBool(print_info);
            }
            else
            {
                _print_info = false;
            }

            if (on_finish_wait != null)
            {
                _on_finish_wait = ToBool(on_finish_wait);
            }
            else
            {
                _on_finish_wait = false;
            }

            ParentContainer.Properties.Quiet = _quiet;
            ParentContainer.Properties.WaitOnFinish = _on_finish_wait;
            if (_print_info)
            {
                ParentContainer.PrintInfo();
            }

            return true;
        }

        public override void Validate() => throw new KScriptNoValidationNeeded();
        public override string UsageInformation() => @"Used to declare KScriptParser values and inform the parser where to find the script commands.";
    }
}
