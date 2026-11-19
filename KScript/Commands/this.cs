using KScript.Metadata;
namespace KScript.Commands
{
    [KScriptRetired("Not part of KScript 2.")]
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
