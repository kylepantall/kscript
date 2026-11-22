using System.Collections.Concurrent;
using System.Globalization;

using KScript.Runtime;

namespace KScript.Data
{
    /// <summary>
    /// <c>$User->fake(role: 'admin')</c>: a saved record with made-up values for the fields that need one, for tests
    /// and seeders. Optional fields and fields with defaults are left to the model, and a required belongs-to relation
    /// gets a fake record of its own.
    /// </summary>
    internal static class Fakes
    {
        /// <summary>A number per model, so each fake's text, emails and numbers differ from the last.</summary>
        private static readonly ConcurrentDictionary<ModelInfo, int> Counters = new ConcurrentDictionary<ModelInfo, int>();

        /// <summary>How deep fakes of linked records go, so models that need each other don't loop.</summary>
        private const int MaxDepth = 8;

        public static Record Make(CallContext call, ModelInfo model, Dictionary<string, object> given, int depth = 0)
        {
            if (depth > MaxDepth)
                throw new KScriptException($"Can't make a fake {model.Name}: its required relations go round in a circle. Give one of them yourself.", "Argument");
            int n = Counters.AddOrUpdate(model, 1, (_, last) => last + 1);
            var values = new Dictionary<string, object>(given, StringComparer.Ordinal);
            foreach (Relation r in model.Relations.Values.Where(r => r.Kind == RelationKind.BelongsTo && !r.Optional))
            {
                if (values.ContainsKey(r.Name) || values.ContainsKey(r.ForeignKey)) continue;
                values[r.Name] = Make(call, r.Target, new Dictionary<string, object>(StringComparer.Ordinal), depth + 1);
            }
            foreach (Column c in model.Columns)
            {
                if (values.ContainsKey(c.Name) || c.AutoIncrement || c.Nullable || c.References != null || c.Field.HasDefault) continue;
                if (c.Name == ModelInfo.CreatedAt || c.Name == ModelInfo.UpdatedAt || c.Name == ModelInfo.DeletedAt) continue;
                values[c.Name] = Value(model, c, n);
            }
            return Records.Create(call, model, values, fillable: false);
        }

        /// <summary>A made-up value for a column, inside the limits its type gives (min, max).</summary>
        private static object Value(ModelInfo model, Column c, int n)
        {
            IReadOnlyDictionary<string, object> args = c.Field.Type.Args;
            double? min = args?.GetValueOrDefault("min") as double?;
            double? max = args?.GetValueOrDefault("max") as double?;
            string snake = ModelInfo.Snake(model.Name);
            string number = n.ToString(CultureInfo.InvariantCulture);
            switch (c.Kind)
            {
                case ColumnKind.Integer:
                case ColumnKind.Real:
                    double value = n;
                    if (min != null && value < min) value = min.Value + n - 1;
                    if (max != null && value > max) value = max.Value;
                    return value;
                case ColumnKind.Bool:
                    return false;
                case ColumnKind.Date:
                    return Rows.Clock();
                case ColumnKind.Email:
                    return $"{snake}{number}@example.com";
                case ColumnKind.Handle:
                    return "@" + snake.Replace("_", string.Empty, StringComparison.Ordinal) + number;
                case ColumnKind.Uuid:
                    return UuidValue.New(4);
                case ColumnKind.Password:
                    return "fake password " + number + " " + Guid.NewGuid().ToString("N");
                case ColumnKind.Enum:
                    return c.Words[0];
                case ColumnKind.Json:
                    return c.Field.Type.ListDepth > 0 ? new List<object>() : new Dictionary<string, object>(StringComparer.Ordinal);
                default:
                    string text = $"{model.Name} {c.Name.Replace('_', ' ')} {number}";
                    int least = min != null ? (int)min.Value : 0;
                    if (text.Length < least) text = text.PadRight(least, 'x');
                    int most = (int?)max ?? c.Length ?? int.MaxValue;
                    return text.Length > most ? text.Substring(text.Length - most) : text;
            }
        }
    }
}
