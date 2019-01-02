using KScript.KScriptObjects;

namespace KScript.Arguments
{
    [KScriptNoInnerObjects()]
    public class aremove : KScriptObject
    {
        [KScriptProperty("Defines the array to remove from.", true)]
        public string from { get; set; }

        [KScriptProperty("Remove the item from the array at the given index.", true)]
        public string index { get; set; }


        public override bool Run()
        {
            //ParentContainer.Parser.Iterate
            return true;
        }

        public override string UsageInformation()
        {
            throw new System.NotImplementedException();
        }

        public override void Validate()
        {
            KScriptValidator validator = new KScriptValidator(ParentContainer);
            validator.AddValidator(new KScriptValidationObject("index", false, KScriptValidator.ExpectedInput.Number));
            validator.AddValidator(new KScriptValidationObject("from", false, KScriptValidator.ExpectedInput.ArrayID));
            validator.Validate(this);
        }
    }
}
