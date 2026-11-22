using System.Globalization;

using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// <c>card</c>: a payment card number, checked with the Luhn digit, that prints masked apart from its last four
    /// digits. == compares the whole numbers, which answers only true or false and so shows nothing of either (it is
    /// how a script asks "is this the card on file?"); Equality.Never would block that without hiding anything more.
    /// Cards have no order: sorting or &lt; by the hidden number would hand out what the mask keeps back, one
    /// comparison at a time, and no order of card numbers means anything. No operators and no iteration.
    /// </summary>
    public sealed class CardType : NativeType
    {
        public override string Name => "card";

        public override string Description => "A payment card number, checked with the Luhn digit. It prints masked apart from the last four digits.";

        public override string Example => "'4242 4242 4242 4242'";

        public override bool Secret => true;

        public override RawText RawText => RawText.Word;

        public override IReadOnlyList<string> StringMethods => Array.Empty<string>();

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
            const string Let = "<let name=\"x\" type=\"card\" value=\"'4242 4242 4242 4242'\" />\n";
            m.Field("last4", S, "The last four digits, which are safe to show.", (t, _) => ((NativeText)t).Part("last4"), Let + "<echo>{ $x.last4 }</echo>");
            m.Field("brand", S, "visa, mastercard, amex, discover, jcb, diners, unionpay, maestro or unknown, from its first digits.", (t, _) => ((NativeText)t).Part("brand"), Let + "<echo>{ $x.brand }</echo>");
            m.Method("reveal", "", "The whole number. Only use it to pass the card on to a payment provider; never store or log it.", S, (t, c) => ((NativeText)t).Text,
                Let + "<echo>{ $x->reveal()->count() }</echo>");
        }

        public override (object Value, string Reason) Parse(string s)
        {
            string t = new string(s.Where(c => !(c is ' ' or '-')).ToArray());
            if (t.Length == 0) return (null, "it is empty");
            if (!t.All(char.IsAsciiDigit)) return (null, "it can only use digits, spaces and -");
            if (t.Length < 12 || t.Length > 19) return (null, "it must have 12 to 19 digits");
            int sum = 0;
            for (int i = 0; i < t.Length; i++)
            {
                int d = t[t.Length - 1 - i] - '0';
                if (i % 2 == 1) { d *= 2; if (d > 9) d -= 9; }
                sum += d;
            }
            if (sum % 10 != 0) return (null, "its check digit doesn't match: check it for a typing mistake");
            string last4 = t.Substring(t.Length - 4);
            return (new NativeText("card", t, new Dictionary<string, object> { ["last4"] = last4, ["brand"] = Brand(t) }, "•••• " + last4), null);
        }

        private static string Brand(string n)
        {
            int Prefix(int digits) => int.Parse(n.AsSpan(0, digits), CultureInfo.InvariantCulture);
            if (n[0] == '4') return "visa";
            if (Prefix(2) is >= 51 and <= 55 || Prefix(4) is >= 2221 and <= 2720) return "mastercard";
            if (Prefix(2) is 34 or 37) return "amex";
            if (Prefix(4) == 6011 || Prefix(2) == 65 || Prefix(3) is >= 644 and <= 649) return "discover";
            if (Prefix(2) == 35) return "jcb";
            if (Prefix(2) is 36 or 38 or 39 || Prefix(3) is >= 300 and <= 305) return "diners";
            if (Prefix(2) == 62) return "unionpay";
            if (Prefix(2) is 50 or >= 56 and <= 69) return "maestro";
            return "unknown";
        }
    }
}
