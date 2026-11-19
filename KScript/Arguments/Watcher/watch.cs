using KScript.Metadata;
using KScript.KScriptExceptions;
using KScript.KScriptObjects;

namespace KScript.Arguments.Watcher
{
    [KScriptRetired("Not part of KScript 2; check the value with <if> where it changes.")]
    public class watch : KScriptObject
    {
        public watch(KScriptContainer container) => SetContainer(container);

        public string @for { get; set; }

        public override bool Run() => true;

        public override void Validate() => throw new KScriptNoValidationNeeded(this);

        public override string UsageInformation() => @"Used to watch a specified def.";
    }
}