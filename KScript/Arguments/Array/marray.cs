using KScript.KScriptObjects;

namespace KScript.Arguments.Array
{
    class marray : KScriptObject
    {
        [KScriptProperty(
            @"Used as a unique identifier for this array.",
            false
        )]
        public string id { get; set; }


        [KScriptProperty(
            @"Used to define a closure to call before each insertion.",
            false
        )]
        public string _insertion { get; set; }

        [KScriptProperty(
            @"Used to define a closure to call before each removal.",
            false
        )]
        public string _removal { get; set; }


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
