using System.Linq;

namespace KScript.Commands
{
    public class exists : KScriptCommand
    {
        private readonly string id;
        public exists(string id) => this.id = id;
        public override string Calculate() => ToBoolString(ParentContainer.GetDefs().Any(i => i.Key == id));

        public override void Validate() { }
    }
}
