using KScript.KScriptExceptions;
using KScript.KScriptObjects;

namespace KScript.Arguments.Watcher
{
    public class watch : KScriptObject
    {
        public watch(KScriptContainer container) => SetContainer(container);

        public string @for { get; set; }

        public override bool Run() => true;

        public override void Validate() => throw new KScriptNoValidationNeeded(this);

        public override string UsageInformation() => @"Used to watch a specified def.";
    }
}