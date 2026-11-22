using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// <c>email</c>: an email address such as joan@example.com, held as an <see cref="EmailAddress"/>. Addresses
    /// sort alphabetically, so a list of them can be sorted; they take no arithmetic.
    /// </summary>
    public sealed class EmailType : NativeType
    {
        public override string Name => KType.Email;

        public override string Description => "An email address such as joan@example.com. Spaces are trimmed and the domain is lowercased.";

        public override string Example => "joan@example.com";

        public override RawText RawText => RawText.Word;

        public override string Article => "an";

        public override string JsonFormat => "email";

        public override IReadOnlyList<string> Options => new[] { "domains" };

        public override IReadOnlyList<string> StringMethods => NativeTypes.Searchable.Concat(new[] { "lower", "upper", "split", "replace" }).ToArray();

        public override IReadOnlyList<string> Completions => Array.Empty<string>();

        public override Equality Equality => Equality.ByText;

        public override Ordering Ordering => Ordering.ByText;

        public override Iteration Iteration => Iteration.None;

        protected override void DeclareOperators(NativeOperators o)
        {
        }

        protected override void DeclareMembers(NativeMembers m)
        {
            const string Let = "<let name=\"who\" type=\"email\" value=\"Joan.Smith@Example.com\" />\n";
            m.Field("local", KType.String, "The part before the @, as it was written.", (t, _) => ((EmailAddress)t).Local, Let + "<echo>{ $who.local }</echo>");
            m.Field("domain", KType.String, "The part after the @, in lower case.", (t, _) => ((EmailAddress)t).Domain, Let + "<echo>{ $who.domain }</echo>");
        }

        public override (object Value, string Reason) Parse(string text) =>
            EmailAddress.TryParse(text, out EmailAddress email, out string reason) ? (email, null) : (null, reason);
    }
}
