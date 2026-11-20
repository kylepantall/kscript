namespace KScript.Runtime
{
    /// <summary>
    /// The value of <c>&lt;enum name="Size" values="small, medium, large" /&gt;</c>: a map from each name to itself, so
    /// <c>$Size.small</c> is 'small' and <c>'small' in $Size</c> works. As a parameter type (<c>params="size: Size"</c>)
    /// it limits the argument to those values, which calls may then write bare: <c>@pack(large)</c>.
    /// </summary>
    public sealed class EnumType : Dictionary<string, object>
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
            if (value is string s)
            {
                string match = Keys.FirstOrDefault(k => string.Equals(k, s.Trim(), StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }
            string close = value is string text ? KScript.Runtime.Values.Closest(text, Keys) : null;
            throw new KScriptException($"{what} must be a {Name}: {string.Join(", ", Keys)}. It was {KScript.Runtime.Values.Describe(value)}." + (close != null ? $" Did you mean {close}?" : string.Empty), "Type");
        }

        /// <summary>Finds the &lt;enum&gt; a parameter type names.</summary>
        internal static EnumType Resolve(string type, Scope scope, Module module, string function)
        {
            if (scope.TryGet(type, out object found) && found is EnumType e) return e;
            if (module != null && module.Globals.TryGet(type, out found) && found is EnumType g) return g;
            throw new KScriptException($"Unknown type '{type}' for a parameter of @{function}(). Declare it with <enum name=\"{type}\" values=\"...\" />, or use one of: {string.Join(", ", KScript.Runtime.Values.DeclarableTypes)}.", "Type");
        }
    }
}
