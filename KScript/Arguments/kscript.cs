using KScript.KScriptTypes.KScriptExceptions;
using System;

namespace KScript.Arguments
{
    public class kscript : KScriptObject
    {
        public string quiet { get; set; }
        public string print_info { get; set; }

        public override bool Run()
        {
            bool _quiet = false, _print_info = false;

            if (quiet != null) _quiet = ToBool(quiet);
            else _quiet = false;

            if (print_info != null) _print_info = ToBool(print_info);
            else _print_info = false;

            ParentContainer.Properties.Quiet = _quiet;
            if (_print_info) ParentContainer.PrintInfo();

            return true;
        }

        public override void Validate() => throw new KScriptNoValidationNeeded();
        public override string UsageInformation() => @"Used to declare KScriptParser values and inform the parser where to find the script commands.";
    }
}
