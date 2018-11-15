namespace KScript.Commands
{
    public class array : KScriptCommand
    {
        private readonly string id, index;
        public array(string id, string index)
        {
            this.id = id;
            this.index = index;
        }

        public override string Calculate() => KScript().ArrayGet(id)[int.Parse(index)];
    }
}
