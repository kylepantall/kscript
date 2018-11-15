using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

        private string format;

        /// <summary>
        /// Calculates the date using the specified format or using the default format.
        /// </summary>
        /// <returns>string value of the date</returns>
        public override string Calculate()
        {
            if (!string.IsNullOrWhiteSpace(format))
                try { return DateTime.Now.Date.ToString(format); }
                catch (Exception) { }
            return DateTime.Now.ToLongDateString();
        }
    }
}
