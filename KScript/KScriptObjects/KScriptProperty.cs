using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KScript.KScriptObjects
{
    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public class KScriptProperty : Attribute
    {
        private string Usage { get; set; }
        private bool Required { get; set; }
        public KScriptProperty(string Usage, bool Required = true)
        {
            this.Usage = Usage;
            this.Required = Required;
        }
        public override string ToString() => Usage;
        public bool IsRequired() => Required;
    }
}
