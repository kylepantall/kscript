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
            var array = MultiArray.MultiArrayParser.ParseString(GetNode().InnerXml);
            var arrayItem = MultiArray.MultiArrayParser.GetArrayItem(
                            ReturnFormattedVariables(at),
                            ParentContainer
                        );

            arrayItem.GetCollection().AddItem(array.Get());
            return true;
        }
        public override string UsageInformation() => @"Used to insert into an existing MArray.";
        public override void Validate()
        {
            KScriptValidator validator = new KScriptValidator(ParentContainer);
            validator.AddValidator(new KScriptValidationObject("at", false));
            validator.Validate(this);
        }
    }
}
