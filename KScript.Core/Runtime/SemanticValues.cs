namespace KScript.Runtime
{
    /// <summary>
    /// A text value with a meaning of its own, such as an email address or a handle. It prints as its text, string
    /// methods work on it, and it equals text that means the same thing once normalised.
    /// </summary>
    public interface ISemanticText : IKObject
    {
        /// <summary>The normalised text.</summary>
        string Text { get; }
    }

    /// <summary>
    /// Converting values to native types (email, password, url, ...) and explaining why one doesn't fit. Each type
    /// parses itself (<see cref="NativeType.Parse"/>); this is the one place the runtime asks.
    /// </summary>
    public static class SemanticTypes
    {
        public const string Email = "email";
        public const string Password = "password";
        public const string Handle = "handle";
        public const string Uuid = "uuid";
        public const string Regex = "regex";

        /// <summary>Every native type: these five, the rest of the language's own (url, ip, country, ...) and packages' types.</summary>
        public static IReadOnlyList<string> Names => KScript.Metadata.NativeTypes.Names.ToList();

        public static bool IsSemantic(string type) => NativeTypeRegistry.Get(type) != null;

        /// <summary>
        /// Converts <paramref name="value"/> to the native type: a value of the type stays as it is, and text (or a
        /// number, for a type that takes them) is parsed by the type. Returns false with <paramref name="reason"/> set
        /// to why it doesn't fit (never quoting a password).
        /// </summary>
        public static bool TryConvert(string type, object value, out object result, out string reason)
        {
            result = null;
            reason = null;
            if (NativeTypeRegistry.Get(type) is not NativeType native) return false;
            if (value is IKObject own && own.TypeName == type)
            {
                result = value;
                return true;
            }
            if (value is double && native.TakesNumbers) value = Values.ToStr(value);
            string text = value switch { string str => str, ISemanticText other => other.Text, _ => null };
            if (text == null) return false;
            (result, reason) = native.Parse(text);
            return result != null;
        }

        /// <summary>Why a native value breaks the options given where its type is used: ip(version: 4), url(schemes: [https]).</summary>
        public static IEnumerable<string> OptionProblems(string type, object value, IReadOnlyDictionary<string, object> options) =>
            options.Count == 0 || NativeTypeRegistry.Get(type) is not NativeType native ? Enumerable.Empty<string>() : native.OptionProblems(value, options);

        /// <summary>
        /// Why a value doesn't fit, starting "needs ...". The value itself is never shown for a password.
        /// </summary>
        public static string Mismatch(string type, object value, string reason)
        {
            if (type == Password)
                return value is string ? $"needs a password, but got text that {reason}" : $"needs a password (text), but got {"a " + Values.TypeOf(value)}";
            return reason != null ? $"needs {Article(type)} {type}, but got {Values.Describe(value)}: {reason}" : $"needs {Article(type)} {type}, but got {Values.Describe(value)}";
        }

        private static string Article(string type) => KScript.Metadata.NativeTypes.Get(type)?.WithArticle.Split(' ')[0] ?? "a";

        /// <summary>The options a semantic type takes besides min, max, pattern, one_of and not.</summary>
        public static IReadOnlyList<string> OptionsOf(string type) => KScript.Metadata.NativeTypes.Get(type)?.Options ?? Array.Empty<string>();
    }
}
