using System;

namespace KScript.KScriptObjects
{
    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = true)]
    public class KScriptExample : Attribute
    {
        private readonly string Example = "";
        public KScriptExample(string example) => Example = example;
        public override string ToString() => Example;
    }
}
