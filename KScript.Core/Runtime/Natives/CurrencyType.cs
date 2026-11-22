using System.Globalization;

using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// <c>currency</c>: an ISO 4217 currency code, kept in capitals. Codes sort alphabetically; amounts of money are
    /// the <c>money</c> type, which does the arithmetic.
    /// </summary>
    public sealed class CurrencyType : NativeType
    {
        public override string Name => "currency";

        public override string Description => "An ISO 4217 currency code, kept in capitals: GBP, USD, EUR.";

        public override string Example => "GBP";

        public override RawText RawText => RawText.Word;

        public override IReadOnlyList<string> StringMethods => NativeTypes.Searchable;

        public override IReadOnlyList<string> Completions => new[] { "GBP", "USD", "EUR", "JPY", "CHF", "CAD", "AUD" };

        /// <summary>Every currency code.</summary>
        public override IEnumerable<NativeValueSuggestion> Suggest(NativeValueContext context) =>
            CountryCodes.CurrencyCodes().Select(c => new NativeValueSuggestion(c, CountryCodes.Currency(c)?.Name));

        public override Equality Equality => Equality.ByText;

        public override Ordering Ordering => Ordering.ByText;

        public override Iteration Iteration => Iteration.None;

        protected override void DeclareOperators(NativeOperators o)
        {
        }

        protected override void DeclareListMembers(NativeListMembers l)
        {
        }

        protected override void DeclareMembers(NativeMembers m)
        {
            const string S = KType.String, N = KType.Number;
            const string Let = "<let name=\"x\" type=\"currency\" value=\"'jpy'\" />\n";
            m.Field("name", S, "The currency's name in English: British Pound.", (t, _) => ((NativeText)t).Part("name"), Let + "<echo>{ $x.name }</echo>");
            m.Field("symbol", S, "Its symbol: £.", (t, _) => ((NativeText)t).Part("symbol"), Let + "<echo>{ $x.symbol }</echo>");
            m.Field("digits", N, "How many decimal places amounts have: 2 for GBP, 0 for JPY.", (t, _) => ((NativeText)t).Part("digits"), Let + "<echo>{ $x.digits }</echo>");
            m.Method("format", "amount: number", "An amount written in this currency: £1,234.50.", S, (t, c) =>
            {
                var currency = (NativeText)t;
                int digits = (int)(double)currency.Part("digits");
                string number = Math.Abs(c.Num(0)).ToString("N" + digits, CultureInfo.InvariantCulture);
                return (c.Num(0) < 0 ? "-" : string.Empty) + (string)currency.Part("symbol") + number;
            }, Let + "<echo>{ $x->format(1234.5) }</echo>");
        }

        public override (object Value, string Reason) Parse(string s) => Read(s);

        private static (object Value, string Reason) Read(string s)
        {
            string t = s.Trim().ToUpperInvariant();
            if (t.Length != 3 || !t.All(char.IsAsciiLetter)) return (null, "it must be a three-letter ISO 4217 code, such as GBP");
            CountryCodes.CurrencyInfo info = CountryCodes.Currency(t);
            if (info == null) return (null, "it isn't an ISO 4217 currency code");
            return (new NativeText("currency", t, new Dictionary<string, object>
            {
                ["name"] = info.Name,
                ["symbol"] = info.Symbol,
                ["digits"] = (double)info.Digits,
            }), null);
        }

        /// <summary>The currency with this code (GBP), or null when it isn't one.</summary>
        internal static NativeText Find(string code) => code == null ? null : Read(code).Value as NativeText;

        /// <summary>The currency with this code, or an error that says why it isn't one.</summary>
        internal static NativeText Of(string code)
        {
            (object value, string reason) = Read(code ?? string.Empty);
            return (NativeText)value ?? throw new KScriptException($"'{code}' isn't a currency: {reason}.", "Value");
        }
    }
}
