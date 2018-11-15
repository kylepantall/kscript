using KScript.KScriptTypes.KScriptExceptions;

namespace KScript.Arguments
{
    public class ainsert : KScriptObject
    {
        public ainsert(string contents) => Contents = contents;
        public string to { get; set; }

        public override bool Run()
        {
            KScript().ArrayGet(to).Add(HandleCommands(Contents));
            return true;
        }

        public override string UsageInformation() => "Used to insert into an Array with specified ID.";
        public override void Validate() => new KScriptNoValidationNeeded();
    }
}
