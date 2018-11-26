using System;

namespace KScript.Commands
{
    public class time : KScriptCommand
    {
        public time() => format = null;
        public time(string format) => this.format = format;

        private readonly string format;

        public override string Calculate()
        {
            if (!string.IsNullOrWhiteSpace(format))
            {
                try { return DateTime.Now.TimeOfDay.ToString(format); }
                catch (Exception ex) { HandleException(ex, this); }
            }

            return DateTime.Now.TimeOfDay.ToString(@"hh\:mm\:ss");
        }
    }
}
