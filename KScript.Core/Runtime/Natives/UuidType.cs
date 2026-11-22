using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// <c>uuid</c>: a UUID such as 3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f, held as a <see cref="UuidValue"/>. UUIDs sort
    /// by their text, which for time-based ones (version 8 from <c>@uuid(v: 8)</c>) puts newer ones later. They take
    /// no arithmetic.
    /// </summary>
    public sealed class UuidType : NativeType
    {
        public override string Name => KType.Uuid;

        public override string Description => "A UUID, versions 1 to 8 and the nil and max UUIDs, lowercased to 8-4-4-4-12.";

        public override string Example => "3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f";

        public override RawText RawText => RawText.Word;

        public override string JsonFormat => "uuid";

        public override IReadOnlyList<string> Options => new[] { "v" };

        public override IReadOnlyList<string> StringMethods => new[]
        {
            "contains", "starts_with", "ends_with", "upper", "slice", "split", "pad_left", "pad_right", "escape", "url_encode", "markdown_escape", "json",
        };

        public override IReadOnlyList<string> Completions => new[] { "00000000-0000-0000-0000-000000000000" };

        public override Equality Equality => Equality.ByText;

        public override Ordering Ordering => Ordering.ByText;

        public override Iteration Iteration => Iteration.None;

        protected override void DeclareOperators(NativeOperators o)
        {
        }

        protected override void DeclareMembers(NativeMembers m)
        {
            m.Field("version", KType.Number, "The UUID's version: 4 for random ones, 8 for custom ones (0 for the nil UUID).", (t, _) => (double)((UuidValue)t).Version,
                "<let name=\"id\" type=\"uuid\" value=\"3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f\" />\n<echo>{ $id.version }</echo>");
        }

        public override (object Value, string Reason) Parse(string text) =>
            UuidValue.TryParse(text, out UuidValue uuid, out string reason) ? (uuid, null) : (null, reason);
    }
}
