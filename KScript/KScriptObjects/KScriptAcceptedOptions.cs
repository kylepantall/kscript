using System;

namespace KScript.KScriptObjects
{
    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public class KScriptAcceptedOptions : Attribute
    {
        private readonly string[] values;
        public KScriptAcceptedOptions(params string[] args) => values = args;
        public string[] GetValues() => values;
        public static readonly string[] BooleanValues = { "y", "n", "1", "0", "t", "f", "true", "false", "yes", "no" };
    }
}
