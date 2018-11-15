using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KScript.KScriptObjects
{
    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public class KScriptAcceptedOptions : Attribute
    {
        private string[] values;
        public KScriptAcceptedOptions(params string[] args) => values = args;
        public string[] GetValues() => values;
    }
}
