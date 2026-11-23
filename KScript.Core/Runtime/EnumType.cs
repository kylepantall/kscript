namespace KScript.Runtime
{
    /// <summary>
    /// The value of <c>&lt;enum name="Size" values="small, medium, large" /&gt;</c>: a map from each name to itself, so
    /// <c>Size.small</c> is 'small' and <c>'small' in Size</c> works. As a parameter type (<c>params="size: Size"</c>)
    /// it limits the argument to those values, which calls may then write bare: <c>@pack(large)</c>.
    /// </summary>
    public sealed class EnumType : Dictionary<string, object>, IDeclaredType
    {
        public EnumType(string name, IEnumerable<string> names) : base(StringComparer.Ordinal)
        {
            Name = name;
            foreach (string n in names) this[n] = n;
        }

        public string Name { get; }

        public IReadOnlyList<string> Names => Keys.ToList();

        /// <summary>The enum value <paramref name="value"/> names, or an error naming the allowed values.</summary>
        public object Check(object value, string what)
        {
            value = KScript.Runtime.Values.Unwrap(value);
            if (value is string s)
            {
                string match = Keys.FirstOrDefault(k => string.Equals(k, s.Trim(), StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }
            string close = value is string text ? KScript.Runtime.Values.Closest(text, Keys) : null;
            throw new KScriptException($"{what} must be a {Name}: {string.Join(", ", Keys)}. It was {KScript.Runtime.Values.Describe(value)}." + (close != null ? $" Did you mean {close}?" : string.Empty), "Type");
        }
    }
}
