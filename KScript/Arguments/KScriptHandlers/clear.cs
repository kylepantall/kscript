using KScript.Metadata;
using KScript.KScriptExceptions;
using System;

namespace KScript.Arguments
{
    /// <summary>
    /// KScript object to clear the console.
    /// </summary>
    [KScriptObjects.KScriptNoInnerObjects]
    [KScriptDoc("Clears the terminal.")]
    [KScriptElement(KScriptContentKind.Empty, Context = KScriptElementContext.Script)]
    public class clear : KScriptObject
    {
        public override bool Run()
        {
            Helpers.Platform.ClearConsole();
            return true;
        }
        public override string UsageInformation() => "Used to clear the console.";
        public override void Validate() => throw new KScriptNoValidationNeeded(this);
    }
}
