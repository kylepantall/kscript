using System.Linq;

namespace KScript.Commands
{
    public class defstate : KScriptCommand
    {
        private readonly string id;

        public defstate(string id) => this.id = id;

        public override string Calculate()
        {
            return ParentContainer.GetDef(this.id).StateLog.ToString();
        }

        public override void Validate() { }
    }
}
