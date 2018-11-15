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
        public override string UsageInformation() => "Allows blocks of script to be executed dynamically.";
        public override void Validate() => throw new KScriptTypes.KScriptExceptions.KScriptNoValidationNeeded();
    }
}
