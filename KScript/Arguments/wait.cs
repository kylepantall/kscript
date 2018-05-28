using KScript.KScriptTypes.KScriptExceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KScript.Arguments
{
    public class wait : KScriptObject
    {
        public string interval { get; set; } = "5s";

        private int fromInterval()
        {
            if (interval.EndsWith("mins")) return int.Parse(interval.Substring(0, interval.Length - "mins".Length)) * 60000;
            if (interval.EndsWith("ms")) return int.Parse(interval.Substring(0, interval.Length - "ms".Length)) * 100;
            if (interval.EndsWith("s")) return int.Parse(interval.Substring(0, interval.Length - "s".Length)) * 1000;
            return 1000;
        }

        public override bool Run()
        {
            DateTime finish_time = DateTime.Now.AddMilliseconds(fromInterval());
            while (DateTime.Now < finish_time) { }
            return true;
        }

        public override string UsageInformation() => @"Uses the 'interval' property to determine how many seconds to wait until continuing.\n Accepted values e.g. 5ms or 5s or 5mins etc...";

        public override void Validate() => throw new KScriptNoValidationNeeded();
    }
}
