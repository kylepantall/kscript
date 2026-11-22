using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// <c>password</c>: a secret, held as a <see cref="PasswordValue"/>. It prints masked, can't be compared with ==
    /// or ordered (<see cref="PasswordValue.CompareError"/> says to use ->verify()), and takes no operators, so its
    /// text never leaks through one.
    /// </summary>
    public sealed class PasswordType : NativeType
    {
        private const string StrengthRules = "min?: int, max?: int, upper?: bool, lower?: bool, digit?: bool, symbol?: bool, common?: bool, not?: list, score?: int";

        public override string Name => KType.Password;

        public override string Description => "A secret. It prints as ********, can't be compared with ==, and is hashed to be stored.";

        public override string Example => "'correct horse battery staple'";

        public override RawText RawText => RawText.Word;

        public override bool Secret => true;

        public override string JsonFormat => "password";

        public override IReadOnlyList<string> Options => new[] { "upper", "lower", "digit", "symbol", "common", "score", "algorithm" };

        public override IReadOnlyList<string> StringMethods => Array.Empty<string>();

        public override IReadOnlyList<string> Completions => Array.Empty<string>();

        public override Equality Equality => Equality.Never;

        public override Ordering Ordering => Ordering.None;

        public override Iteration Iteration => Iteration.None;

        protected override void DeclareOperators(NativeOperators o)
        {
        }

        protected override void DeclareListMembers(NativeListMembers l)
        {
        }

        protected override void DeclareMembers(NativeMembers m)
        {
            const string Let = "<let name=\"pw\" type=\"password\" value=\"'correct horse battery staple'\" />\n";
            m.Method("hash", "algorithm?: argon2id | bcrypt", "A hash to store instead of the password: argon2id (the default) or bcrypt, with a new random salt each time.", KType.String,
                (t, c) => ((PasswordValue)t).Hash(c.Arg(0) is null ? ((PasswordValue)t).Algorithm : c.Str(0)),
                Let + "<echo>{ $pw->hash()->starts_with('$argon2id$') }</echo>");
            m.Method("verify", "hash: string", "True when this is the password the stored hash was made from. Takes the same time whether it matches or not.", KType.Bool,
                (t, c) => ((PasswordValue)t).Verify(c.Str(0)),
                Let + "<let name=\"stored\" value=\"$pw->hash()\" />\n<echo>{ $pw->verify($stored) }</echo>");
            m.Method("needs_rehash", "hash: string", "True when the stored hash was made with weaker settings or another algorithm than ->hash() uses now, so it should be replaced after a successful login.", KType.Bool,
                (t, c) => PasswordValue.NeedsRehash(c.Str(0), ((PasswordValue)t).Algorithm),
                Let + "<echo>{ $pw->needs_rehash($pw->hash(algorithm: 'bcrypt')) }</echo>");
            m.Method("strength", StrengthRules, "How hard the password is to guess: { score (0 to 4), label, ok, problems }. Rules: min length (8), max, upper, lower, digit, symbol, common (true rejects well-known passwords), not (text it mustn't contain) and score (the lowest score that passes, 3).", KType.Map,
                (t, c) => ((PasswordValue)t).Strength(c),
                "<let name=\"pw\" type=\"password\" value=\"'password1'\" />\n<echo>{ $pw->strength(min: 12, symbol: true).problems }</echo>");
            m.Method("is_strong", StrengthRules, "True when the password passes ->strength() with these rules.", KType.Bool,
                (t, c) => (bool)((PasswordValue)t).Strength(c)["ok"],
                Let + "<echo>{ $pw->is_strong(min: 12) }</echo>");
            m.Method("reveal", "", "The password's text. Only use it to pass the password on, such as to another service's login.", KType.String, (t, c) => ((PasswordValue)t).Secret,
                "<let name=\"pw\" type=\"password\" value=\"'s3cret!'\" />\n<echo>$pw is { $pw->reveal()->count() } characters</echo>");
        }

        /// <summary>The password, or why the text can't be one. The reason never quotes the text.</summary>
        public override (object Value, string Reason) Parse(string text) =>
            PasswordValue.TryCreate(text, out PasswordValue password, out string reason) ? (password, null) : (null, reason);
    }
}
