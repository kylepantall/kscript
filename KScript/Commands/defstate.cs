using System;
using System.Linq;

namespace KScript.Commands
{
    public class defstate : KScriptCommand
    {
        private readonly string id;
        private readonly string timestamp;

        public defstate(string id) => this.id = id;

        public defstate(string id, string timestamp) : this(id)
        {
            this.timestamp = timestamp;
        }

        public override string Calculate()
        {
            if (IsEmpty(this.timestamp))
            {
                return ParentContainer.GetDef(this.id).StateLog.ToString();
            }

            return ParentContainer.GetDef(this.id).StateLog.FindUsingValue((date) =>
            {
                return date.ToString("dd/MM/yyyy hh:mm:ss") == DateTime.Parse(timestamp).ToString("dd/MM/yyyy hh:mm:ss");
            });
        }
        public override void Validate() { }
    }
}
