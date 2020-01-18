using KScript.KScriptExceptions;
using KScript.KScriptObjects;
using System;

namespace KScript.Arguments
{
    [KScriptNoInnerObjects]
    public class anykey : KScriptObject
    {
        [KScriptProperty("The prompt used to inform the user to press any key.", false)]
        [KScriptExample("<anykey prompt=\"Press any key to continue downloading...\" />")]
        public string prompt { get; set; }

        public override bool Run()
        {
            Out(prompt, () => !string.IsNullOrEmpty(prompt));
            Console.ReadKey(true);
            return true;
        }

        public override string UsageInformation() => "Awaits for any key input using a prompt to signal the user.";
        public override void Validate() => new KScriptNoValidationNeeded(this);
    }
}
