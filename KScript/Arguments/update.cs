namespace KScript.Arguments
{
    public class update : KScriptObject
    {
        [KScriptObjects.KScriptProperty("Used to identify the def to update.", true)]
        [KScriptObjects.KScriptAcceptedOptions("$[id]")]
        [KScriptObjects.KScriptExample("<update to=\"example\"> New value </update>")]
        public string to { get; set; }

        public update(string Contents) => this.Contents = Contents;

        public override bool Run()
        {
            Def(to).Contents = HandleCommands(Contents);
            return true;
        }

        public override string UsageInformation() => "Used to update the value stored within a def.";
        public override void Validate() => throw new KScriptExceptions.KScriptNoValidationNeeded(this);
    }
}
