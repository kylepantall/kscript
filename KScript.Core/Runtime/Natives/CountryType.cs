using System.Globalization;

using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// <c>country</c>: an ISO 3166 country, kept as its two capitals (GB). Codes sort alphabetically, which is how a
    /// list of countries is usually shown; there is no arithmetic on them.
    /// </summary>
    public sealed class CountryType : NativeType
    {
        public override string Name => "country";

        public override string Description => "An ISO 3166 country code. Two-letter, three-letter and English names are read; it is kept as two capitals, such as GB.";

        public override string Example => "GB";

        public override RawText RawText => RawText.Word;

        public override IReadOnlyList<string> StringMethods => NativeTypes.Searchable;

        public override IReadOnlyList<string> Completions => new[] { "GB", "US", "IE", "FR", "DE", "ES", "IT", "NL", "CA", "AU" };

        /// <summary>Every country's two-letter code.</summary>
        public override IEnumerable<NativeValueSuggestion> Suggest(NativeValueContext context) =>
            CountryCodes.TwoLetterCodes().Select(c => new NativeValueSuggestion(c, CountryCodes.Find(c)?.EnglishName));

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
            const string S = KType.String;
            const string Let = "<let name=\"x\" type=\"country\" value=\"'United Kingdom'\" />\n";
            m.Field("name", S, "The country's name in English: United Kingdom.", (t, _) => ((NativeText)t).Part("name"), Let + "<echo>{ $x.name }</echo>");
            m.Field("alpha3", S, "The three-letter code: GBR.", (t, _) => ((NativeText)t).Part("alpha3"), Let + "<echo>{ $x.alpha3 }</echo>");
            m.Field("currency", "currency", "The currency the country uses: GBP.", (t, _) => CurrencyType.Of((string)((NativeText)t).Part("currency")), Let + "<echo>{ $x.currency }</echo>");
            m.Field("flag", S, "The country's flag emoji: 🇬🇧.", (t, _) => string.Concat(((NativeText)t).Text.Select(ch => char.ConvertFromUtf32(0x1F1E6 + ch - 'A'))), Let + "<echo>{ $x.flag }</echo>");
        }

        public override (object Value, string Reason) Parse(string s) => Read(s);

        /// <summary>The country with this code (GB), or an error that says why it isn't one.</summary>
        internal static NativeText Of(string code)
        {
            (object value, string reason) = Read(code ?? string.Empty);
            return (NativeText)value ?? throw new KScriptException($"'{code}' isn't a country: {reason}.", "Value");
        }

        private static (object Value, string Reason) Read(string s)
        {
            string t = s.Trim();
            if (t.Length == 0) return (null, "it is empty");
            RegionInfo region = CountryCodes.Find(t);
            if (region == null)
            {
                string close = CountryCodes.Closest(t);
                return (null, "it isn't an ISO 3166 country code or an English country name" + (close != null ? $". Did you mean {close}?" : string.Empty));
            }
            return (new NativeText("country", region.TwoLetterISORegionName, new Dictionary<string, object>
            {
                ["name"] = region.EnglishName,
                ["alpha3"] = region.ThreeLetterISORegionName,
                ["currency"] = region.ISOCurrencySymbol,
            }), null);
        }
    }
}
