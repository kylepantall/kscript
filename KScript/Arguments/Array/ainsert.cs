using KScript.KScriptExceptions;

namespace KScript.Arguments
{
    public class ainsert : KScriptObject
    {
        public ainsert(string contents) => Contents = contents;

        [KScriptObjects.KScriptProperty("The array ID to insert a value into.", true)]
        public string to { get; set; }

        public override bool Run()
        {
            if (KScript().ArrayGet(this, to) == null)
                throw new KScriptArrayNotFound(this);

            KScript().ArrayGet(this, to).Add(HandleCommands(Contents));
            return true;
        }

        public override string UsageInformation() => "Used to insert into an Array with specified ID.";
        public override void Validate() => new KScriptNoValidationNeeded(this);
    }
}
