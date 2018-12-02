namespace KScript
{
    /// <summary>
    /// ExceptionWrapper used to catch exceptions in a container.
    /// </summary>
    public class KScriptExceptionWrapper : KScriptObject
    {
        /// <summary>
        /// Used to specify the type of exception to catch.
        /// </summary>
        [KScriptObjects.KScriptProperty("Used to specify the type of exception to catch.")]
        public string type { get; set; }


        /// <summary>
        /// The type of the KScriptObject or KScriptCommand throwing the exception.
        /// Not required.
        /// </summary>
        [KScriptObjects.KScriptProperty("Defines the type of KScriptObject/KScriptCommand to look out for.", false)]
        public string from { get; set; }

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
