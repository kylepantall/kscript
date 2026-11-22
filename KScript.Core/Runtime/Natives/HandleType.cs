using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// <c>handle</c>: a username such as @joan, held as a <see cref="HandleName"/> and written raw as an @name. Handles
    /// sort alphabetically; they take no arithmetic.
    /// </summary>
    public sealed class HandleType : NativeType
    {
        public override string Name => KType.Handle;

        public override string Description => "@ followed by letters, digits, _, . or -: a username such as @joan. Lowercased.";

        public override string Example => "@joan";

        public override RawText RawText => RawText.Handle;

        public override IReadOnlyList<string> StringMethods => new[]
        {
            "contains", "starts_with", "ends_with", "count", "upper", "slice", "matches", "truncate", "pad_left", "pad_right", "escape", "url_encode", "markdown_escape", "json",
        };

        public override IReadOnlyList<string> Completions => Array.Empty<string>();

        public override Equality Equality => Equality.ByText;

        public override Ordering Ordering => Ordering.ByText;

        public override Iteration Iteration => Iteration.None;

        protected override void DeclareOperators(NativeOperators o)
        {
        }

        protected override void DeclareMembers(NativeMembers m)
        {
            m.Field("name", KType.String, "The handle without its @.", (t, _) => ((HandleName)t).Name,
                "<let name=\"who\" type=\"handle\" value=\"@Joan_S\" />\n<echo>{ $who.name }</echo>");
        }

        public override (object Value, string Reason) Parse(string text) =>
            HandleName.TryParse(text, out HandleName handle, out string reason) ? (handle, null) : (null, reason);
    }
}
