namespace KScript.Runtime
{
    /// <summary>
    /// A handle such as <c>@kyle_p</c>: an @ and then 1 to 30 letters, digits, _, . or -. It is lower-cased, so
    /// <c>'@Kyle'</c> and <c>'@kyle'</c> are the same handle. <c>.name</c> is the name without the @.
    /// </summary>
    public sealed class HandleName : ISemanticText
    {
        public const int MaxLength = 30;

        private HandleName(string name) => Name = name;

        /// <summary>The name without the @.</summary>
        public string Name { get; }
        public string Text => "@" + Name;
        public string TypeName => SemanticTypes.Handle;

        public static bool TryParse(string text, out HandleName handle, out string reason)
        {
            handle = null;
            string s = (text ?? string.Empty).Trim();
            reason = Problem(s);
            if (reason != null) return false;
            handle = new HandleName(s.Substring(1).ToLowerInvariant());
            return true;
        }

        private static string Problem(string s)
        {
            if (s.Length == 0) return "it is empty";
            if (s[0] != '@') return $"it must start with @, as in @{(s.Length > 0 && s.Length <= MaxLength ? s.ToLowerInvariant() : "name")}";
            string name = s.Substring(1);
            if (name.Length == 0) return "it needs a name after the @";
            if (name.Length > MaxLength) return $"the name is longer than {MaxLength} characters";
            char bad = name.FirstOrDefault(c => !(char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-'));
            if (bad != default) return $"'{bad}' can't be used in a handle (use letters, digits, _, . and -)";
            if (name[0] is '.' or '-') return "the name must start with a letter, a digit or _";
            if (name.EndsWith('.') || name.EndsWith('-')) return "the name can't end with . or -";
            if (name.Contains("..")) return "the name can't have two dots in a row";
            return null;
        }

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

        public string ToText() => Text;

        public override bool Equals(object obj) => obj is HandleName h && h.Name == Name;

        public override int GetHashCode() => Name.GetHashCode(StringComparison.Ordinal);

        public override string ToString() => Text;
    }
}
