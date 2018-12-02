using System;

namespace KScript.KScriptObjects
{
    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public class KScriptAcceptedOptions : Attribute
    {
        private readonly string[] values;
        public KScriptAcceptedOptions(params string[] args) => values = args;
        public string[] GetValues() => values;
    }
}
