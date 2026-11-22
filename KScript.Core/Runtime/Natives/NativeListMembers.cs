namespace KScript.Runtime
{
    /// <summary>
    /// What a list of one native type can do on top of every list's methods, declared in
    /// <see cref="NativeType.DeclareListMembers"/>: named filters (<c>$versions->without(prereleases)</c>,
    /// <c>->only(releases)</c>) and methods of its own (<c>$versions->latest()</c>). They work on a list whose items
    /// are all of the type, and the editor offers them only on such a list (<c>semver[]</c>).
    /// </summary>
    public sealed class NativeListMembers
    {
        private readonly KScriptPackage.PackageBuilder p;
        private readonly NativeType type;
        private readonly List<(string Name, string Summary, Func<object, bool> Keep)> filters = new List<(string, string, Func<object, bool>)>();

        internal NativeListMembers(KScriptPackage.PackageBuilder p, NativeType type)
        {
            this.p = p;
            this.type = type;
        }

        /// <summary>The receiver its methods register under: semver[].</summary>
        public string Receiver => type.Name + "[]";

        /// <summary>The filters, by name, with what each keeps.</summary>
        internal IReadOnlyList<(string Name, string Summary, Func<object, bool> Keep)> Filters => filters;

        /// <summary>
        /// A named filter: <c>->only(name)</c> keeps the items <paramref name="keep"/> says yes to and
        /// <c>->without(name)</c> drops them. The name is written bare, as an enum value is.
        /// </summary>
        public NativeListMembers Filter(string name, string summary, Func<object, bool> keep)
        {
            Conventions.Enforce(Conventions.Name(p.Name, "filter", name));
            if (filters.Any(f => f.Name == name)) throw new ConventionException($"Native type {type.Name} declares list filter '{name}' twice.");
            if (string.IsNullOrWhiteSpace(summary)) throw new ConventionException($"Native type {type.Name} declares list filter '{name}' without a summary.");
            filters.Add((name, summary, keep));
            return this;
        }

        /// <summary>A method of a list of the type: <c>$versions->latest()</c>. The handler gets the items.</summary>
        public MethodDefinition Method(string name, string parameters, string description, string returns, Func<List<object>, CallContext, object> handler, string example = null)
        {
            MethodDefinition def = p.Method(Receiver, name, parameters, description, returns, (t, c) => handler((List<object>)t, c));
            if (example != null) p.Examples((Receiver + "->" + name, example));
            return def;
        }

        /// <summary>The filter with this name (case ignored), or null.</summary>
        internal Func<object, bool> Find(string name) =>
            filters.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)).Keep;
    }
}
