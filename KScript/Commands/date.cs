using System;

namespace KScript.Commands
{
    public class date : KScriptCommand
    {
        /// <summary>
        /// Creates a new KScriptCommand Date object with the format empty.
        /// </summary>
        public date() => format = null;

        /// <summary>
        /// Creates a new KScriptCommand Date object with the format of 'format'.
        /// </summary>
        /// <param name="format">the format to use</param>
        public date(string format) => this.format = format;

        private readonly string format;

        /// <summary>
        /// Calculates the date using the specified format or using the default format.
        /// </summary>
        /// <returns>string value of the date</returns>
        public override string Calculate()
        {
            if (!string.IsNullOrWhiteSpace(format))
            {
                try { return DateTime.Now.Date.ToString(format); }
                catch (Exception ex) { HandleException(ex, this); }
            }

            return DateTime.Now.ToLongDateString();
        }
    }
}
