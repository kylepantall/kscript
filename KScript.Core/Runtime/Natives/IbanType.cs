using System.Globalization;

using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// <c>iban</c>: an international bank account number, checked with its mod-97 digits. Account numbers have no
    /// meaningful order and no arithmetic, so it declares no operators: == compares the tidied number.
    /// </summary>
    public sealed class IbanType : NativeType
    {
        public override string Name => "iban";

        public override string Description => "An international bank account number, checked with its mod-97 digits. Kept in capitals without spaces.";

        public override string Example => "GB82WEST12345698765432";

        public override string Article => "an";

        public override RawText RawText => RawText.Word;

        public override IReadOnlyList<string> StringMethods => NativeTypes.Searchable;

        public override IReadOnlyList<string> Completions => Array.Empty<string>();

        public override Equality Equality => Equality.ByText;

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
            const string S = KType.String;
            const string Let = "<let name=\"x\" type=\"iban\" value=\"'GB82 WEST 1234 5698 7654 32'\" />\n";
            m.Field("country", "country", "The country the account is in: GB.", (t, _) => CountryType.Of((string)((NativeText)t).Part("country")), Let + "<echo>{ $x.country }</echo>");
            m.Method("formatted", "", "The IBAN in groups of four, the way it is printed: GB82 WEST 1234 5698 7654 32.", S, (t, c) => ((NativeText)t).Part("formatted"), Let + "<echo>{ $x->formatted() }</echo>");
            m.Field("bban", S, "The account number inside it, after the country code and check digits.", (t, _) => ((NativeText)t).Part("bban"), Let + "<echo>{ $x.bban }</echo>");
        }

        public override (object Value, string Reason) Parse(string s)
        {
            string t = new string(s.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray()).ToUpperInvariant();
            if (t.Length < 15 || t.Length > 34) return (null, "it must be 15 to 34 letters and digits");
            if (!char.IsAsciiLetter(t[0]) || !char.IsAsciiLetter(t[1]) || !char.IsAsciiDigit(t[2]) || !char.IsAsciiDigit(t[3])) return (null, "it must start with a country code and two check digits, as in GB82");
            if (t.Any(c => !char.IsAsciiLetterOrDigit(c))) return (null, "it can only use letters and digits");
            if (CountryCodes.Find(t.Substring(0, 2)) == null) return (null, $"{t.Substring(0, 2)} isn't a country code");
            string moved = t.Substring(4) + t.Substring(0, 4);
            int remainder = 0;
            foreach (char c in moved)
            {
                string digits = char.IsAsciiDigit(c) ? c.ToString() : (c - 'A' + 10).ToString(CultureInfo.InvariantCulture);
                foreach (char d in digits) remainder = (remainder * 10 + (d - '0')) % 97;
            }
            if (remainder != 1) return (null, "its check digits don't match: check it for a typing mistake");
            return (new NativeText("iban", t, new Dictionary<string, object>
            {
                ["country"] = t.Substring(0, 2),
                ["bban"] = t.Substring(4),
                ["formatted"] = string.Join(" ", Enumerable.Range(0, (t.Length + 3) / 4).Select(i => t.Substring(i * 4, Math.Min(4, t.Length - i * 4)))),
            }), null);
        }
    }
}
