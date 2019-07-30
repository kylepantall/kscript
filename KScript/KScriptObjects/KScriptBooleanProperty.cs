using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KScript.KScriptObjects
{
    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    internal class KScriptBooleanProperty : Attribute
    {
        public KScriptBooleanProperty()
        {
        }
    }
}
