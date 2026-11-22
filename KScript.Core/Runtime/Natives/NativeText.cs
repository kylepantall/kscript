using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// A value of a native type that has no class of its own (url, ip, country, ...): its tidied text and the parts
    /// its methods read. It prints as its text (masked, for a secret type such as card), keeps the string methods its
    /// type allows, and equals text that means the same once tidied.
    /// </summary>
    public sealed class NativeText : ISemanticText
    {
        public NativeText(string type, string text, IReadOnlyDictionary<string, object> parts = null, string display = null)
        {
            TypeName = type;
            Text = text;
            Parts = parts ?? new Dictionary<string, object>();
            Display = display;
        }

        public string TypeName { get; }

        /// <summary>The tidied text. For a secret type this is the real value: only its methods hand it out.</summary>
        public string Text { get; }

        /// <summary>What it was read into: a url's host and port, a semver's major, ...</summary>
        public IReadOnlyDictionary<string, object> Parts { get; }

        /// <summary>How it prints, when that isn't its text (a card shows only its last four digits).</summary>
        public string Display { get; }

        public object Part(string name) => Parts.TryGetValue(name, out object v) ? v : null;

        public bool TryGetMember(string name, out object value)
        {
            value = null;
            return false;
        }

        public bool TryCallMethod(CallContext call, string name, out object result)
        {
            result = null;
            return false;
        }

        public string ToText() => Display ?? Text;

        public override bool Equals(object obj) => obj is NativeText n && n.TypeName == TypeName && n.Text == Text;

        public override int GetHashCode() => HashCode.Combine(TypeName, Text);

        public override string ToString() => ToText();
    }
}
