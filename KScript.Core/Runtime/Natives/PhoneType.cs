using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// <c>phone</c>: a phone number in E.164, + then the country code and number. Numbers have no natural order and
    /// no arithmetic, so it declares no operators: == compares the tidied number.
    /// </summary>
    public sealed class PhoneType : NativeType
    {
        public override string Name => "phone";

        public override string Description => "A phone number in E.164: + then the country code and number, 8 to 15 digits. Spaces, dots, dashes and brackets are dropped.";

        public override string Example => "+447700900123";

        public override RawText RawText => RawText.Word;

        public override IReadOnlyList<string> Options => new[] { "countries" };

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
            const string S = KType.String;
            const string Let = "<let name=\"x\" type=\"phone\" value=\"'+44 7700 900123'\" />\n";
            m.Field("country_code", S, "The country calling code without +: 44 for a UK number.", (t, _) => ((NativeText)t).Part("country_code"), Let + "<echo>{ $x.country_code }</echo>");
            m.Field("national", S, "The number after the country code.", (t, _) => ((NativeText)t).Part("national"), Let + "<echo>{ $x.national }</echo>");
            m.Method("tel", "", "A tel: link for it: tel:+447700900123.", S, (t, c) => "tel:" + ((NativeText)t).Text, Let + "<echo>{ $x->tel() }</echo>");
        }

        public override (object Value, string Reason) Parse(string s)
        {
            string t = s.Trim();
            if (t.StartsWith("00", StringComparison.Ordinal)) t = "+" + t.Substring(2);
            if (!t.StartsWith('+')) return (null, "it needs + and the country code, as in +447700900123");
            // +44 (0)7700 ...: the (0) is the trunk prefix dialled at home, not part of the number.
            string digits = new string(t.Substring(1).Replace("(0)", string.Empty).Where(c => !(c is ' ' or '-' or '.' or '(' or ')')).ToArray());
            char bad = digits.FirstOrDefault(c => !char.IsAsciiDigit(c));
            if (bad != default) return (null, $"'{bad}' can't be used in a phone number");
            if (digits.Length < 8 || digits.Length > 15) return (null, "it must have 8 to 15 digits after the +");
            if (digits[0] == '0') return (null, "a country code can't start with 0");
            string code = CountryCodes.CallingCode(digits);
            if (code == null) return (null, $"+{digits.Substring(0, Math.Min(3, digits.Length))} isn't a country calling code");
            return (new NativeText("phone", "+" + digits, new Dictionary<string, object>
            {
                ["country_code"] = code,
                ["national"] = digits.Substring(code.Length),
            }), null);
        }

        public override IEnumerable<string> OptionProblems(object value, IReadOnlyDictionary<string, object> options)
        {
            if (value is NativeText phone && options.TryGetValue("countries", out object c))
            {
                List<string> codes = Texts(c).Select(x => x.TrimStart('+')).ToList();
                if (!codes.Contains((string)phone.Part("country_code"))) yield return $"must be a +{string.Join(" or +", codes)} number";
            }
        }

        private static List<string> Texts(object value) =>
            value is List<object> list ? list.Select(v => Values.ToStr(v).Trim()).ToList() : new List<string> { Values.ToStr(value).Trim() };
    }
}
