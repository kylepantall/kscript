using KScript.Metadata;

namespace KScript.Language.Analysis
{
    /// <summary>
    /// Which arguments of KScript.Data's query, create and update methods name a model's columns, for the editor:
    /// <c>$User->where(name: 'Joan')</c> names one in an argument's name, <c>->order_by(name)</c> as a bare word, and
    /// <c>->with(posts)</c> names a relation.
    /// </summary>
    internal static class DataColumns
    {
        /// <summary>Methods whose named arguments are columns: <c>->where(name: 'Joan')</c>.</summary>
        public static readonly HashSet<string> Named = new HashSet<string>(StringComparer.Ordinal)
        {
            "where", "or_where", "where_not", "order_by", "create", "update", "fake", "fake_many",
        };

        /// <summary>Methods whose bare-word arguments are columns: <c>->order_by(name)</c>, <c>->pluck(email)</c>.</summary>
        public static readonly HashSet<string> Bare = new HashSet<string>(StringComparer.Ordinal)
        {
            "select", "order_by", "latest", "oldest", "sum", "avg", "min", "max", "pluck", "value", "increment", "decrement",
        };

        /// <summary>Methods whose bare-word arguments are relations: <c>->with(posts.tags)</c>.</summary>
        public static readonly HashSet<string> Relations = new HashSet<string>(StringComparer.Ordinal) { "with", "with_count" };

        /// <summary>The records a model, or a query on one, gives; null when that isn't known here.</summary>
        public static TypeShape RecordOf(TypeShape receiver)
        {
            TypeShape record = receiver.Kind == KType.Model || receiver.Type == KType.Query ? receiver.Instance : null;
            return record != null && record.Name != null && record.Fields.Count > 0 ? record : null;
        }

        /// <summary>True for a field that is a relation: its value is a record of a model, or a list of them.</summary>
        public static bool IsRelation(FieldInfo field)
        {
            TypeShape shape = field.Shape?.Type == KType.List ? field.Shape.Element : field.Shape;
            return shape != null && (shape.Kind == KType.Record || shape.Instance != null || shape.Name != null && shape.Fields.Count > 0);
        }
    }
}
