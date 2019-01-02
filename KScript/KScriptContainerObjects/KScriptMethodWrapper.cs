using KScript.KScriptObjects;

namespace KScript
{
    public class KScriptMethodWrapper : KScriptObject
    {
        /**
         * [@params] are the name of the parameters to pass to the method, declared with no spaces, seperated using commas.
         * [name] of the method which can be used to call the method later on.
         */
        public string name { get; set; }
        public string @params { get; set; }

        public override bool Run() => true;
        public override string UsageInformation() => "Allows blocks of script to be executed upon invocation using the @call command.";

        public override void Validate()
        {
            KScriptValidator validator = new KScriptValidator(ParentContainer);
            validator.AddValidator(new KScriptValidationObject("name", false));
            validator.AddValidator(new KScriptValidationObject("params", true));
            validator.Validate(this);
        }
    }
}
