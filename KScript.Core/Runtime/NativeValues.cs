using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// A value of a native type that has no class of its own (url, ip, country, ...): its tidied text and the parts
    /// its methods read. It prints as its text (masked, for a secret type such as card), keeps the string methods its
    /// type allows, and equals text that means the same once tidied.
    /// </summary>
    public sealed class NativeText : ISemanticText
    {
        public NativeText(string type, string text, IReadOnlyDictionary<string, object> parts = null, string display = null)
        {
            TypeName = type;
            Text = text;
            Parts = parts ?? new Dictionary<string, object>();
            Display = display;
        }

        public string TypeName { get; }

        /// <summary>The tidied text. For a secret type this is the real value: only its methods hand it out.</summary>
        public string Text { get; }

        /// <summary>What it was read into: a url's host and port, a semver's major, ...</summary>
        public IReadOnlyDictionary<string, object> Parts { get; }

        /// <summary>How it prints, when that isn't its text (a card shows only its last four digits).</summary>
        public string Display { get; }

        public object Part(string name) => Parts.TryGetValue(name, out object v) ? v : null;

        public bool TryGetMember(string name, out object value)
        {
            value = null;
            return false;
        }

        public bool TryCallMethod(CallContext call, string name, out object result)
        {
            result = null;
            return false;
        }

        public string ToText() => Display ?? Text;

        public override bool Equals(object obj) => obj is NativeText n && n.TypeName == TypeName && n.Text == Text;

        public override int GetHashCode() => HashCode.Combine(TypeName, Text);

        public override string ToString() => ToText();
    }

    /// <summary>How a native type's value is made from text, and the options it checks where it is used.</summary>
    public sealed class NativeParser
    {
        public NativeParser(Func<string, (object Value, string Reason)> parse, Func<object, IReadOnlyDictionary<string, object>, IEnumerable<string>> options = null)
        {
            Parse = parse;
            OptionProblems = options;
        }

        /// <summary>The value, or null and why the text doesn't fit (a reason such as "it has no host").</summary>
        public Func<string, (object Value, string Reason)> Parse { get; }

        /// <summary>Why a value breaks the options given where the type is used, such as ip(version: 4).</summary>
        public Func<object, IReadOnlyDictionary<string, object>, IEnumerable<string>> OptionProblems { get; }

        /// <summary>Also takes numbers (port takes 8080 as well as '8080').</summary>
        public bool TakesNumbers { get; init; }
    }

    /// <summary>The parsers of the native types, by name: the language's own here, and those packages add.</summary>
    public static class NativeParsers
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, NativeParser> Parsers = new Dictionary<string, NativeParser>(StringComparer.Ordinal);

        static NativeParsers() => BuiltIn.Register();

        /// <summary>Adds a native type: what the editor knows about it, how its values are made and what they do.</summary>
        public static void Register(NativeType type) => NativeTypeRegistry.Register(type);

        internal static void Add(string name, NativeParser parser)
        {
            lock (Gate) Parsers[name] = parser;
        }

        public static NativeParser Get(string name)
        {
            lock (Gate)
            {
                if (name == null) return null;
                if (Parsers.TryGetValue(name, out NativeParser p)) return p;
                if (NativeTypeRegistry.Get(name) is not NativeType type) return null;
                // A type on the NativeType contract: its parser, as the older callers ask for it.
                return Parsers[name] = new NativeParser(type.Parse, type.OptionProblems) { TakesNumbers = type.TakesNumbers };
            }
        }

        private static (object, string) Ok(object value) => (value, null);

        private static (object, string) No(string reason) => (null, reason);

        /// <summary>The language's own native types beyond email, password, handle, uuid and regex.</summary>
        private static class BuiltIn
        {
            public static void Register()
            {
                Add("phone", new NativeParser(Phone, PhoneOptions));
                Add("country", new NativeParser(Country));
                Add("currency", new NativeParser(Currency));
                Add("locale", new NativeParser(Locale));
                Add("iban", new NativeParser(Iban));
                Add("card", new NativeParser(Card));
            }

            private static List<string> Texts(object value) =>
                value is List<object> list ? list.Select(v => Values.ToStr(v).Trim()).ToList() : new List<string> { Values.ToStr(value).Trim() };

            // ---- people and places ------------------------------------------------------------------------------

            private static (object, string) Phone(string s)
            {
                string t = s.Trim();
                if (t.StartsWith("00", StringComparison.Ordinal)) t = "+" + t.Substring(2);
                if (!t.StartsWith('+')) return No("it needs + and the country code, as in +447700900123");
                // +44 (0)7700 ...: the (0) is the trunk prefix dialled at home, not part of the number.
                string digits = new string(t.Substring(1).Replace("(0)", string.Empty).Where(c => !(c is ' ' or '-' or '.' or '(' or ')')).ToArray());
                char bad = digits.FirstOrDefault(c => !char.IsAsciiDigit(c));
                if (bad != default) return No($"'{bad}' can't be used in a phone number");
                if (digits.Length < 8 || digits.Length > 15) return No("it must have 8 to 15 digits after the +");
                if (digits[0] == '0') return No("a country code can't start with 0");
                string code = CountryCodes.CallingCode(digits);
                if (code == null) return No($"+{digits.Substring(0, Math.Min(3, digits.Length))} isn't a country calling code");
                return Ok(new NativeText("phone", "+" + digits, new Dictionary<string, object>
                {
                    ["country_code"] = code,
                    ["national"] = digits.Substring(code.Length),
                }));
            }

            private static IEnumerable<string> PhoneOptions(object value, IReadOnlyDictionary<string, object> options)
            {
                if (value is NativeText phone && options.TryGetValue("countries", out object c))
                {
                    List<string> codes = Texts(c).Select(x => x.TrimStart('+')).ToList();
                    if (!codes.Contains((string)phone.Part("country_code"))) yield return $"must be a +{string.Join(" or +", codes)} number";
                }
            }

            private static (object, string) Country(string s)
            {
                string t = s.Trim();
                if (t.Length == 0) return No("it is empty");
                RegionInfo region = CountryCodes.Find(t);
                if (region == null)
                {
                    string close = CountryCodes.Closest(t);
                    return No("it isn't an ISO 3166 country code or an English country name" + (close != null ? $". Did you mean {close}?" : string.Empty));
                }
                return Ok(new NativeText("country", region.TwoLetterISORegionName, new Dictionary<string, object>
                {
                    ["name"] = region.EnglishName,
                    ["alpha3"] = region.ThreeLetterISORegionName,
                    ["currency"] = region.ISOCurrencySymbol,
                }));
            }

            private static (object, string) Currency(string s)
            {
                string t = s.Trim().ToUpperInvariant();
                if (t.Length != 3 || !t.All(char.IsAsciiLetter)) return No("it must be a three-letter ISO 4217 code, such as GBP");
                CountryCodes.CurrencyInfo info = CountryCodes.Currency(t);
                if (info == null) return No("it isn't an ISO 4217 currency code");
                return Ok(new NativeText("currency", t, new Dictionary<string, object>
                {
                    ["name"] = info.Name,
                    ["symbol"] = info.Symbol,
                    ["digits"] = (double)info.Digits,
                }));
            }

            private static (object, string) Locale(string s)
            {
                string t = s.Trim().Replace('_', '-');
                if (t.Length == 0) return No("it is empty");
                string[] parts = t.Split('-');
                if (parts[0].Length is < 2 or > 3 || !parts[0].All(char.IsAsciiLetter)) return No("it must start with a two- or three-letter language code, as in en or en-GB");
                CultureInfo culture;
                try
                {
                    culture = CultureInfo.GetCultureInfo(t, predefinedOnly: true);
                }
                catch (CultureNotFoundException)
                {
                    return No("it isn't a known language tag");
                }
                string tag = string.Join("-", parts.Select((p, i) => i == 0 ? p.ToLowerInvariant() : p.Length == 2 ? p.ToUpperInvariant() : p.Length == 4 ? char.ToUpperInvariant(p[0]) + p.Substring(1).ToLowerInvariant() : p.ToLowerInvariant()));
                string region = parts.Skip(1).FirstOrDefault(p => p.Length == 2 || (p.Length == 3 && p.All(char.IsAsciiDigit)))?.ToUpperInvariant() ?? string.Empty;
                return Ok(new NativeText("locale", tag, new Dictionary<string, object>
                {
                    ["language"] = parts[0].ToLowerInvariant(),
                    ["region"] = region,
                    ["name"] = culture.EnglishName,
                    ["native_name"] = culture.NativeName,
                    ["culture"] = culture,
                }));
            }

            // ---- money --------------------------------------------------------------------------------------------

            private static (object, string) Iban(string s)
            {
                string t = new string(s.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray()).ToUpperInvariant();
                if (t.Length < 15 || t.Length > 34) return No("it must be 15 to 34 letters and digits");
                if (!char.IsAsciiLetter(t[0]) || !char.IsAsciiLetter(t[1]) || !char.IsAsciiDigit(t[2]) || !char.IsAsciiDigit(t[3])) return No("it must start with a country code and two check digits, as in GB82");
                if (t.Any(c => !char.IsAsciiLetterOrDigit(c))) return No("it can only use letters and digits");
                if (CountryCodes.Find(t.Substring(0, 2)) == null) return No($"{t.Substring(0, 2)} isn't a country code");
                string moved = t.Substring(4) + t.Substring(0, 4);
                int remainder = 0;
                foreach (char c in moved)
                {
                    string digits = char.IsAsciiDigit(c) ? c.ToString() : (c - 'A' + 10).ToString(CultureInfo.InvariantCulture);
                    foreach (char d in digits) remainder = (remainder * 10 + (d - '0')) % 97;
                }
                if (remainder != 1) return No("its check digits don't match: check it for a typing mistake");
                return Ok(new NativeText("iban", t, new Dictionary<string, object>
                {
                    ["country"] = t.Substring(0, 2),
                    ["bban"] = t.Substring(4),
                    ["formatted"] = string.Join(" ", Enumerable.Range(0, (t.Length + 3) / 4).Select(i => t.Substring(i * 4, Math.Min(4, t.Length - i * 4)))),
                }));
            }

            private static (object, string) Card(string s)
            {
                string t = new string(s.Where(c => !(c is ' ' or '-')).ToArray());
                if (t.Length == 0) return No("it is empty");
                if (!t.All(char.IsAsciiDigit)) return No("it can only use digits, spaces and -");
                if (t.Length < 12 || t.Length > 19) return No("it must have 12 to 19 digits");
                int sum = 0;
                for (int i = 0; i < t.Length; i++)
                {
                    int d = t[t.Length - 1 - i] - '0';
                    if (i % 2 == 1) { d *= 2; if (d > 9) d -= 9; }
                    sum += d;
                }
                if (sum % 10 != 0) return No("its check digit doesn't match: check it for a typing mistake");
                string last4 = t.Substring(t.Length - 4);
                return Ok(new NativeText("card", t, new Dictionary<string, object> { ["last4"] = last4, ["brand"] = CardBrand(t) }, "•••• " + last4));
            }

            private static string CardBrand(string n)
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

        // Methods use these.
        internal static NativeText DurationOf(double seconds) => DurationType.Of(seconds);
    }
}
