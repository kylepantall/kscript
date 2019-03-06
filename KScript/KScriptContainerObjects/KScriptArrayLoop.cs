using KScript.KScriptObjects;

namespace KScript
{
    public class KScriptArrayLoop : KScriptObject
    {
        [KScriptProperty("Used to define which property when storing the looped array item.", true)]
        public string to { get; set; }

        [KScriptProperty("Used to define which array to loop through.", true)]
        public string from { get; set; }

        public override bool Run() => true;

        public override void Validate()
        {
            KScriptValidator validator = new KScriptValidator(ParentContainer);
            validator.AddValidator(new KScriptValidationObject("to", false));
            validator.AddValidator(new KScriptValidationObject("from", false, KScriptValidator.ExpectedInput.ArrayID));
            validator.Validate(this);
        }
        public override string UsageInformation() => @"Used to loop through an KScript Array Object.";
    }
}
