using System.Runtime.CompilerServices;
using KScript.KScriptObjects;

namespace KScript.Arguments
{
    class guard : KScriptObject
    {
        [KScriptProperty("The variable ID to check for a value.", true)]
        public string from { get; set; }

        public guard()
        {
            SetValidationType(ValidationTypes.DURING_PARSING);
        }

        public override bool Run()
        {
            string value = Def(from).Contents;
            bool @canContinue = value.Equals(NULL) || string.IsNullOrWhiteSpace(value);
            return canContinue;
        }

        public override string UsageInformation()
        {
            return "Used to check if a variable has returned a value and is ignored if the value is not NULL." +
                "If the value is NULL, the guard inner commands are parsed";
        }

        public override void Validate()
        {
            KScriptValidator validator = new KScriptValidator(KScript());
            validator.AddValidator(new KScriptValidationObject("from", false, KScriptValidator.ExpectedInput.DefID));
            validator.Validate(this);
        }
    }
}
