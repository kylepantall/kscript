using System;

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
