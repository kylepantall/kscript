using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>Reads a field of a value: <c>$response.status</c>.</summary>
    public delegate object FieldHandler(object target, Interpreter interpreter);

    /// <summary>A field a package adds to a type of value, read as <c>value.name</c>.</summary>
    public sealed class ValueFieldDefinition
    {
        internal ValueFieldDefinition(ValueFieldInfo info, FieldHandler handler, KScriptPackage package)
        {
            Info = info;
            Handler = handler;
            Package = package;
        }

        public string Name => Info.Name;

        /// <summary>The type the field is on.</summary>
        public string Receiver => Info.Receiver;

        public ValueFieldInfo Info { get; }
        public FieldHandler Handler { get; }
        public KScriptPackage Package { get; }
    }
}
