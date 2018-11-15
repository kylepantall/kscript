using System;

namespace KScript.KScriptObjects
{
    /// <summary>
    /// Used to declare that a KScriptObject doesn't required inner KScriptObjects or inner content.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
    public class KScriptNoInnerObjects : Attribute { }
}
