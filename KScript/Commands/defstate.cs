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
                return KScript().GetDef(this.id).StateLog.ToString();
            }

            return KScript().GetDef(this.id).StateLog.FindUsingValue((date) =>
            {
                // Compare to the second. The previous "hh" format was 12-hour, so 1 AM and 1 PM compared as equal.
                DateTime target = DateTime.Parse(timestamp, System.Globalization.CultureInfo.CurrentCulture);
                return date.ToString("yyyy-MM-dd HH:mm:ss") == target.ToString("yyyy-MM-dd HH:mm:ss");
            });
        }
        public override void Validate() { }
    }
}
