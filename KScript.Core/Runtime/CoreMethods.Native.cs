using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

using KScript.Metadata;

namespace KScript.Runtime
{
    public sealed partial class CorePackage
    {
        private static NativeText T(object target) => (NativeText)target;

        private static object Part(object target, string name) => T(target).Part(name);

        /// <summary>Makes a native value from text, or fails with why it doesn't fit.</summary>
        private static object Native(string type, string text)
        {
            if (SemanticTypes.TryConvert(type, text, out object value, out string reason)) return value;
            throw new KScriptException($"'{text}' isn't {NativeTypes.Get(type).WithArticle}: {reason}.", "Value");
        }

        /// <summary>The fields and methods every native type declares, and the list filters on any list.</summary>
        private static void RegisterNativeMethods(PackageBuilder p)
        {
            // The types on the NativeType contract declare their own fields and methods.
            foreach (NativeType type in NativeTypeRegistry.BuiltIn) type.Members(p);
            ListFilters.Register(p);
        }
    }
}
