using System.Text;

using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// <c>jwt</c>: a JSON Web Token, three base64url parts. Its header and claims can be read; its signature isn't
    /// checked. A token is an opaque credential: it takes no operators and has no order, and == compares the token.
    /// </summary>
    public sealed class JwtType : NativeType
    {
        public override string Name => "jwt";

        public override string Description => "A JSON Web Token: three base64url parts. Its claims can be read; its signature isn't checked.";

        public override string Example => "'eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.sig'";

        public override RawText RawText => RawText.Word;

        public override IReadOnlyList<string> StringMethods => NativeTypes.Searchable;

        public override IReadOnlyList<string> Completions => Array.Empty<string>();

        public override Equality Equality => Equality.ByText;

        public override Ordering Ordering => Ordering.None;

        public override Iteration Iteration => Iteration.None;

        protected override void DeclareOperators(NativeOperators o)
        {
        }

        protected override void DeclareMembers(NativeMembers m)
        {
            const string Let = "<let name=\"x\" type=\"jwt\" value=\"'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJqb2FuIiwiZXhwIjoxNzAwMDAwMDAwfQ.sig'\" />\n";
            m.Field("header", KType.Map, "The token's header: { alg, typ, ... }.", (t, _) => Part(t, "header"), Let + "<echo>{ $x.header }</echo>");
            m.Field("claims", KType.Map, "The token's claims: { sub, exp, ... }. The signature isn't checked, so don't trust them for sign-in.", (t, _) => Part(t, "claims"),
                Let + "<echo>{ $x.claims }</echo>");
            m.Field("expires", KType.Date, "When the token expires (its exp claim), or null if it doesn't say.", (t, _) =>
                ((Dictionary<string, object>)Part(t, "claims")).TryGetValue("exp", out object exp) && exp is double seconds ? DateTimeOffset.FromUnixTimeSeconds((long)seconds).LocalDateTime : null,
                Let + "<echo>{ $x.expires }</echo>");
            m.Field("is_expired", KType.Bool, "True when its exp claim is in the past.", (t, _) =>
                ((Dictionary<string, object>)Part(t, "claims")).TryGetValue("exp", out object exp) && exp is double seconds && DateTimeOffset.FromUnixTimeSeconds((long)seconds) < DateTimeOffset.UtcNow,
                Let + "<echo>{ $x.is_expired }</echo>");
        }

        private static object Part(object target, string name) => ((NativeText)target).Part(name);

        public override (object Value, string Reason) Parse(string s)
        {
            s = s.Trim();
            if (s.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) s = s.Substring(7).Trim();
            string[] parts = s.Split('.');
            if (parts.Length != 3) return (null, "it needs three parts separated by dots (header.payload.signature)");
            var decoded = new List<object>();
            foreach (string part in parts.Take(2))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(Encoding.UTF8.GetString(Base64Type.Bytes(part)));
                    if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return (null, "its header and payload must be JSON objects");
                    decoded.Add(Values.FromJson(doc.RootElement));
                }
                catch (Exception e) when (e is FormatException or System.Text.Json.JsonException)
                {
                    return (null, "its header and payload must be base64url JSON");
                }
            }
            return (new NativeText("jwt", s, new Dictionary<string, object> { ["header"] = decoded[0], ["claims"] = decoded[1] }), null);
        }
    }
}
