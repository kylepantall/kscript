using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// <c>json</c>: text that holds valid JSON. Two values are == when they hold the same data, so spacing and the
    /// order of an object's keys don't matter: '{"a": 1, "b": 2}' == '{"b":2,"a":1}'. JSON text has no order and takes
    /// no operators; ->json() gives the value, which does.
    /// </summary>
    public sealed class JsonType : NativeType
    {
        public override string Name => "json";

        public override string Description => "Text that holds valid JSON. ->json() gives the value.";

        public override string Example => "'{\"a\": 1}'";

        public override RawText RawText => RawText.Word;

        public override IReadOnlyList<string> StringMethods => NativeTypes.Searchable;

        public override IReadOnlyList<string> Completions => Array.Empty<string>();

        // Spacing and key order are how the JSON is written, not what it holds. Lists and maps compare deeply, as with ===.
        public override Equality Equality => Equality.By((a, b) => Values.StrictEquals(((NativeText)a).Part("value"), ((NativeText)b).Part("value")));

        public override Ordering Ordering => Ordering.None;

        public override Iteration Iteration => Iteration.None;

        protected override void DeclareOperators(NativeOperators o)
        {
        }

        protected override void DeclareMembers(NativeMembers m)
        {
        }

        public override (object Value, string Reason) Parse(string s)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(s);
                return (new NativeText("json", s.Trim(), new Dictionary<string, object> { ["value"] = Values.FromJson(doc.RootElement) }), null);
            }
            catch (System.Text.Json.JsonException e)
            {
                string message = e.Message.Split(" Path:")[0].Split(" LineNumber:")[0].TrimEnd('.');
                return (null, "it isn't valid JSON: " + char.ToLowerInvariant(message[0]) + message.Substring(1));
            }
        }
    }
}
