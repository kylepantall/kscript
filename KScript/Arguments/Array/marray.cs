using KScript.KScriptObjects;

namespace KScript.Arguments.Array
{
    class marray : KScriptObject
    {
        [KScriptProperty("Used as a unique identifier for this array", true)]
        public string name { get; set; }

        public override bool Run()
        {
            //ParentContainer.AddMultidimensionalArray(name, new MultiArray.ArrayCollection());
            return true;
        }

        public override string UsageInformation()
        {
            return @"Used to add a new MultiArray for this script with the given unique name";
        }

        public override void Validate()
        {
            KScriptValidator validator = new KScriptValidator(ParentContainer);
            validator.AddValidator(new KScriptValidationObject("name", false));
            validator.Validate(this);
        }
    }
}
