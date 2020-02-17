using KScript.KScriptObjects;

namespace KScript.Arguments.Array
{
    class marray : KScriptObject
    {
        [KScriptProperty(
            @"Used as a unique identifier for this array.
            Required if not used in conjuction with 'at'",
            false
        )]
        public string id { get; set; }
        public marray(string Contents) => this.Contents = Contents;
        public override bool Run() => true;
        public override string UsageInformation() => @"Used to add a new MultiArray for this script with the given unique name";
        public override void Validate()
        {
            KScriptValidator validator = new KScriptValidator(KScript());
            validator.AddValidator(new KScriptValidationObject("id", false));
            validator.Validate(this);
        }
    }
}
