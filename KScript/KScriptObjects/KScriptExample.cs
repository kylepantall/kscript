using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KScript.KScriptObjects
{
    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = true)]
    public class KScriptExample : Attribute
    {
        private string Example = "";
        public KScriptExample(string example) => Example = example;
        public override string ToString() => Example;
    }
}
