using KScript.KScriptTypes.KScriptExceptions;
using System;

namespace KScript.Arguments
{
    [KScriptObjects.KScriptNoInnerObjects]
    public class wait : KScriptObject
    {
        [KScriptObjects.KScriptProperty("The time to wait.", true)]
        [KScriptObjects.KScriptAcceptedOptions("s", "ms", "mins")]
        [KScriptObjects.KScriptExample("<wait interval=\"5ms\"/>")]
        [KScriptObjects.KScriptExample("<wait interval=\"10mins\"/>")]
        [KScriptObjects.KScriptExample("<wait interval=\"3s\"/>")]
        public string interval { get; set; } = "5s";

        private int fromInterval()
        {
            if (interval.EndsWith("mins") || interval.EndsWith("min"))
            {
                return int.Parse(interval.Substring(0, interval.Length - "mins".Length)) * Global.Time.Second;
            }

            if (interval.EndsWith("ms"))
            {
                return int.Parse(interval.Substring(0, interval.Length - "ms".Length)) * Global.Time.Millisecond;
            }

            if (interval.EndsWith("s"))
            {
                return int.Parse(interval.Substring(0, interval.Length - "s".Length)) * Global.Time.Second;
            }

            return Global.Time.Second;
        }

        public override bool Run()
        {
            DateTime finish_time = DateTime.Now.AddMilliseconds(fromInterval());
            while (DateTime.Now < finish_time) { }
            return true;
        }

        public override string UsageInformation() => @"Uses the 'interval' property to determine how many seconds to wait until continuing.";

        public override void Validate() => throw new KScriptNoValidationNeeded();
    }
}
