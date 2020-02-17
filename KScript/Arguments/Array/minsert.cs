using KScript.KScriptObjects;

namespace KScript.Arguments.Array
{
    class minsert : KScriptObject
    {
        [KScriptProperty(
            @"Used to define where to insert the defined array.",
            true
        )]
        public string at { get; set; }
        public minsert(string Contents) => this.Contents = Contents;
        public override bool Run()
        {
            var array = MultiArray.MultiArrayParser.ParseString(HandleCommands(GetNode().InnerXml));
            var arrayItem = MultiArray.MultiArrayParser.GetArrayItem(
                            ReturnFormattedVariables(at),
                            KScript()
                        );

            arrayItem.GetCollection().AddItem(array.Get());
            return true;
        }
        public override string UsageInformation() => @"Used to insert into an existing MArray.";
        public override void Validate()
        {
            KScriptValidator validator = new KScriptValidator(KScript());
            validator.AddValidator(new KScriptValidationObject("at", false));
            validator.Validate(this);
        }
    }
}
