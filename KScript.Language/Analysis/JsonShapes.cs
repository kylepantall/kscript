using System.Text.Json;
using KScript.Language.Catalog;

namespace KScript.Language.Analysis
{
    /// <summary>
    /// The shape of a JSON file's data, so <c>@read_json('data/people.json')</c> completes <c>$person.name</c> and
    /// list methods know their items are maps. Large or deep files are only read partly.
    /// </summary>
    internal static class JsonShapes
    {
        private const int MaxLength = 2 * 1024 * 1024;
        private const int MaxDepth = 8;
        private const int SampleItems = 50;

        public static TypeShape FromText(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > MaxLength)
            {
                return null;
            }
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                return From(doc.RootElement, 0);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static TypeShape From(JsonElement e, int depth)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.String: return new TypeShape(KType.String);
                case JsonValueKind.Number: return new TypeShape(KType.Number);
                case JsonValueKind.True:
                case JsonValueKind.False: return new TypeShape(KType.Bool);
                case JsonValueKind.Null: return new TypeShape(KType.Null);
                case JsonValueKind.Object:
                {
                    TypeShape map = new TypeShape(KType.Map);
                    if (depth < MaxDepth)
                    {
                        foreach (JsonProperty p in e.EnumerateObject())
                        {
                            map.Fields.TryAdd(p.Name, new FieldInfo(p.Name, From(p.Value, depth + 1), null, "From the JSON file."));
                        }
                    }
                    return map;
                }
                case JsonValueKind.Array:
                {
                    TypeShape list = new TypeShape(KType.List);
                    if (depth >= MaxDepth)
                    {
                        return list;
                    }
                    TypeShape element = null;
                    foreach (JsonElement item in e.EnumerateArray().Take(SampleItems))
                    {
                        element = element == null ? From(item, depth + 1) : Merge(element, From(item, depth + 1));
                    }
                    if (element != null)
                    {
                        list.Element = element;
                    }
                    return list;
                }
                default:
                    return TypeShape.Unknown;
            }
        }

        /// <summary>Items of one list usually share a shape; maps merge their fields (some items may lack some keys).</summary>
        private static TypeShape Merge(TypeShape a, TypeShape b)
        {
            if (a.Type == KType.Null) return b;
            if (b.Type == KType.Null) return a;
            if (a.Type != b.Type) return TypeShape.Unknown;
            if (a.Type == KType.Map)
            {
                foreach (KeyValuePair<string, FieldInfo> f in b.Fields)
                {
                    a.Fields.TryAdd(f.Key, f.Value);
                }
            }
            return a;
        }
    }
}
