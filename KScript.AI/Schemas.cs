using System.Text.Json;
using System.Text.RegularExpressions;

using KScript.Runtime;

namespace KScript.AI
{
    /// <summary>
    /// Turns KScript's short schemas into JSON schemas: <c>{ category: 'bug|feature', urgency: 'int 1-5', tags: ['string'] }</c>.
    /// Each field is required. A text value names a type (string, number, int, bool, date) or lists choices with |;
    /// anything else becomes a string field described by that text, such as <c>'a one-line summary'</c>.
    /// </summary>
    internal static class Schemas
    {
        public static Dictionary<string, JsonElement> FromValue(object value, string where)
        {
            if (value is not Dictionary<string, object> map)
                throw new KScriptException($"{where} needs a map of fields, such as {{ category: 'bug|feature', urgency: 'int 1-5' }}, but got {Values.Describe(value)}.", "Argument");
            return ToJson(Field(map, where)).Deserialize(AiJsonContext.Default.DictionaryStringJsonElement);
        }

        private static JsonElement ToJson(Dictionary<string, object> o) => JsonSerializer.SerializeToElement(o, AiJsonContext.Default.DictionaryStringObject);

        private static Dictionary<string, object> Field(object spec, string where) => spec switch
        {
            Dictionary<string, object> map => new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = map.ToDictionary(kv => kv.Key, kv => (object)Field(kv.Value, where)),
                ["required"] = map.Keys.ToList(),
                ["additionalProperties"] = false,
            },
            List<object> list when list.Count == 1 => new Dictionary<string, object> { ["type"] = "array", ["items"] = Field(list[0], where) },
            List<object> => throw new KScriptException($"A list in {where} describes its items with one value, such as ['string'] or [{{ name: 'string' }}].", "Argument"),
            string text => Text(text),
            _ => throw new KScriptException($"{where} can't use {Values.Describe(spec)} as a field. Use text such as 'string', 'int 1-5' or 'yes|no', a map, or a list.", "Argument"),
        };

        private static readonly Regex Range = new Regex(@"^(int|integer|number|float)\s+(-?\d+(?:\.\d+)?)\s*(?:-|to|\.\.)\s*(-?\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase);

        private static Dictionary<string, object> Text(string text)
        {
            string t = text.Trim();
            Match range = Range.Match(t);
            if (range.Success)
            {
                string type = range.Groups[1].Value.StartsWith("int", StringComparison.OrdinalIgnoreCase) ? "integer" : "number";
                return new Dictionary<string, object> { ["type"] = type, ["description"] = $"From {range.Groups[2].Value} to {range.Groups[3].Value}." };
            }
            switch (t.ToLowerInvariant())
            {
                case "string": case "text": return Of("string");
                case "number": case "float": return Of("number");
                case "int": case "integer": return Of("integer");
                case "bool": case "boolean": return Of("boolean");
                case "date": return new Dictionary<string, object> { ["type"] = "string", ["description"] = "A date, as YYYY-MM-DD." };
            }
            if (t.Contains('|'))
            {
                List<string> choices = t.Split('|').Select(c => c.Trim()).Where(c => c.Length > 0).Distinct().ToList();
                return new Dictionary<string, object> { ["type"] = "string", ["enum"] = choices };
            }
            return new Dictionary<string, object> { ["type"] = "string", ["description"] = t };
        }

        private static Dictionary<string, object> Of(string type) => new Dictionary<string, object> { ["type"] = type };

        /// <summary>A tool for a &lt;fn&gt; function: its parameters, their types, and its description.</summary>
        public static AiTool Tool(object value, string where)
        {
            if (value is not UserFunction fn)
                throw new KScriptException($"{where}: tools are <fn> functions, such as tools=\"lookup_order\" or tools: [@lookup_order], but got {Values.Describe(value)}.", "Argument");
            var properties = new Dictionary<string, JsonElement>();
            foreach (Parameter p in fn.Parameters)
            {
                properties[p.Name] = ToJson(ParameterType(p.Type));
            }
            return new AiTool
            {
                Name = fn.Name,
                Description = string.IsNullOrWhiteSpace(fn.Description) ? $"The KScript function {fn.Name}({string.Join(", ", fn.Parameters.Select(p => p.Name))})." : fn.Description,
                Properties = properties,
                Required = fn.Parameters.Where(p => p.Default == null).Select(p => p.Name).ToList(),
                Function = fn,
            };
        }

        private static Dictionary<string, object> ParameterType(string type) => (type ?? "any").ToLowerInvariant() switch
        {
            "string" => Of("string"),
            "int" => Of("integer"),
            "float" or "number" => Of("number"),
            "bool" => Of("boolean"),
            "list" => Of("array"),
            "map" => Of("object"),
            "date" => new Dictionary<string, object> { ["type"] = "string", ["description"] = "A date, as YYYY-MM-DD." },
            _ => new Dictionary<string, object>(),
        };
    }
}
