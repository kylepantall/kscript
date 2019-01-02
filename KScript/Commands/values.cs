namespace KScript.Commands
{
    public class values : KScriptCommand
    {
        private readonly string id;
        private readonly string valueid;

        public values(string id, string valueid)
        {
            this.id = id;
            this.valueid = valueid;
        }

        public override string Calculate() => ParentContainer.GetGlobalValue(id, valueid);

        public override void Validate() { }
    }
}
