namespace KScript
{
    public class KScriptExceptionWrapper : KScriptObject
    {
        /// <summary>
        /// Used to specify the type of exception to catch.
        /// </summary>
        //[KScriptObjects.KScriptProperty("Used to specify the type of exception to catch. Wildcard can be used for example: KScriptFile* where the exception could be: KScriptFileNotFound, KScriptFileInUse etc.")]
        public string type { get; set; }
                                          
        /// <summary>
        /// Used to return the methods unique object to a variable name. 
        /// Can later be used using commands
        /// </summary>
        public string @return { get; set; }

        public override bool Run() => true;
        public override string UsageInformation() => "Allows KScripts to handle types of exceptions.";
        public override void Validate() => throw new KScriptExceptions.KScriptNoValidationNeeded(this);
    }
}
