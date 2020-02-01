using System;

namespace KScript.Commands
{
    public class datetime : KScriptCommand
    {
        public datetime() => format = null;
        public datetime(string format) => this.format = format;
        private readonly string format;
        public override string Calculate()
        {
            if (!string.IsNullOrWhiteSpace(format))
            {
                try { return DateTime.Now.ToString(format); }
                catch (Exception ex) { HandleException(ex, this); }
            }

            return DateTime.Now.ToString();
        }

        public override void Validate() { }
    }
}
