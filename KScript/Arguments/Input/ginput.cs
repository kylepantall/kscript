using KScript.KScriptObjects;

namespace KScript.Arguments
{
    class ginput : KScriptObject
    {
        public ginput() => SetValidationType(ValidationTypes.DURING_PARSING);

        [KScriptProperty("The property to store the input to.", true)]
        public string to { get; set; }

        [KScriptProperty("The output to print to the console.", false)]
        public string output { get; set; }

        public override bool Run()
        {
            var input = !string.IsNullOrEmpty(output) ? In(output) : In();

            if (string.IsNullOrEmpty(input))
            {
                return true;
            }

            Def(to).Contents = input;
            return false;
        }

        public override string UsageInformation() => "A guarded input - if no value is provided, the inner commands " +
            "are executed.";


        public override void Validate()
        {
            KScriptValidator validator = new KScriptValidator(ParentContainer);
            validator.AddValidator(new KScriptValidationObject("to", false, KScriptValidator.ExpectedInput.DefID));
            validator.AddValidator(new KScriptValidationObject("output", true));
            validator.Validate(this);
        }
    }
}
