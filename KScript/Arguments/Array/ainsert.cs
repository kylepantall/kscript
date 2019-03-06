using KScript.KScriptExceptions;

namespace KScript.Arguments
{
    public class ainsert : KScriptObject
    {
        public ainsert(string contents) => Contents = contents;
        public string to { get; set; }

        public override bool Run()
        {
            if (KScript().ArrayGet(this, to) != null)
                KScript().ArrayGet(this, to).Add(HandleCommands(Contents));
            else
                throw new KScriptArrayNotFound(this);
            return true;
        }

        public override string UsageInformation() => "Used to insert into an Array with specified ID.";
        public override void Validate() => new KScriptNoValidationNeeded(this);
    }
}
