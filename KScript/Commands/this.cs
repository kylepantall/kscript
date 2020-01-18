namespace KScript.Commands
{
    class @this : KScriptCommand
    {
        public override string Calculate()
        {
            return GetBaseObject().ToString();
        }

        public override void Validate()
        {
            throw new KScriptExceptions.KScriptNoValidationNeeded(this);
        }
    }
}
