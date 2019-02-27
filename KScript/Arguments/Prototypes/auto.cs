using KScript.KScriptExceptions;

namespace KScript.Arguments.Prototypes
{
    /**
     * The auto function is still to be implemented.
     */
    [KScriptObjects.KScriptHideObject()]
    class auto : KScriptObject
    {
        public override bool Run() => throw new KScriptPrototypeException(this);

        public override string UsageInformation() => @"Used for contextual calculation of sentences and descriptions. 
E.g. Example 1: 12% of £1.23,
Example 2: 12% of 9%.
Support for currencies (potential feature): £1.23 or $20";

        public override void Validate() => throw new KScriptNoValidationNeeded(this);
    }
}
