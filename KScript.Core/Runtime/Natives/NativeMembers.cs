namespace KScript.Runtime
{
    /// <summary>
    /// The fields and methods a native type declares, in its <see cref="NativeType.DeclareMembers"/>. They register
    /// on the package that adds the type, with the type as the receiver, so they follow the same naming rules as
    /// every other method (<see cref="Conventions"/>).
    /// </summary>
    public sealed class NativeMembers
    {
        private readonly KScriptPackage.PackageBuilder p;
        private readonly string type;

        internal NativeMembers(KScriptPackage.PackageBuilder p, string type)
        {
            this.p = p;
            this.type = type;
        }

        /// <summary>The package builder, for a command that makes values of the type (@slugify).</summary>
        public KScriptPackage.PackageBuilder Package => p;

        /// <summary>A field, read as <c>$value.name</c>. <paramref name="example"/> is a script for the docs that prints it.</summary>
        public ValueFieldDefinition Field(string name, string returns, string description, FieldHandler handler, string example = null)
        {
            ValueFieldDefinition def = p.Field(type, name, returns, description, handler);
            if (example != null) p.Examples((type + "." + name, example));
            return def;
        }

        /// <summary>A method, called as <c>$value->name(args)</c>. <paramref name="example"/> is a script for the docs that prints it.</summary>
        public MethodDefinition Method(string name, string parameters, string description, string returns, MethodHandler handler, string example = null)
        {
            MethodDefinition def = p.Method(type, name, parameters, description, returns, handler);
            if (example != null) p.Examples((type + "->" + name, example));
            return def;
        }
    }
}
