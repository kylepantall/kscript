using KScript.Runtime;
using KScript.Syntax;
using KScript.Syntax.Markup;

namespace KScript.Data
{
    /// <summary>Fields are composed by RecordType. This handles the model options shared by concrete models and mixins.</summary>
    internal static class ModelComposition
    {
        private static readonly string[] InheritedOptions =
            { "key", "timestamps", "soft_deletes", "unique", "index", "hidden", "guarded" };

        internal sealed record Base(string Name, IReadOnlyDictionary<string, string> Options);

        internal static bool TryReadBases(string text, out List<Base> bases, out string error)
        {
            bases = new List<Base>();
            error = null;
            foreach (string entry in Shapes.TypeNames(text))
            {
                ParseResult parsed = ExpressionParser.ParseExpression(entry);
                var options = new Dictionary<string, string>(StringComparer.Ordinal);
                string name;
                if (parsed.Success && parsed.Root is IdentifierNode id)
                    name = id.Name;
                else if (parsed.Success && parsed.Root is CallNode { Callee: IdentifierNode target } call)
                {
                    name = target.Name;
                    foreach (ArgumentNode arg in call.Arguments)
                    {
                        string key = arg.Name ?? (arg.Value as IdentifierNode)?.Name;
                        string value = arg.Name == null ? "true" : arg.Value switch
                        {
                            IdentifierNode word => word.Name,
                            LiteralNode { Template: null } literal => literal.Value,
                            ListNode list => entry.Substring(list.Span.Start, list.Span.Length),
                            _ => null,
                        };
                        if (!InheritedOptions.Contains(key, StringComparer.Ordinal) || value == null || !options.TryAdd(key, value))
                        {
                            error = $"Invalid model option on {name}. Use a key, timestamps, soft_deletes, unique, index, hidden or guarded with a literal value.";
                            return false;
                        }
                    }
                }
                else
                {
                    error = $"'{entry}' is not a model or record type name.";
                    return false;
                }
                if (bases.Any(b => b.Name == name))
                {
                    error = $"'{name}' is listed twice as a base.";
                    return false;
                }
                bases.Add(new Base(name, options));
            }
            if (text != null && bases.Count == 0)
            {
                error = "Name at least one model, mixin or record type to extend.";
                return false;
            }
            return true;
        }

        internal static Dictionary<string, string> Merge(
            IEnumerable<(IReadOnlyDictionary<string, string> Options, Base Entry)> bases, MarkupElement declaration)
        {
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var parent in bases)
            {
                if (parent.Options != null)
                    foreach (var option in parent.Options) options[option.Key] = option.Value;
                foreach (var option in parent.Entry.Options) options[option.Key] = option.Value;
            }
            foreach (string key in InheritedOptions)
                if (declaration.GetAttributeValue(key) is string value) options[key] = value;
            return options;
        }
    }
}
