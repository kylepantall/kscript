using System.Globalization;
using System.Text.RegularExpressions;

using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// <c>money</c>: an amount in a currency, kept as the ISO code and the amount with the currency's decimal places:
    /// <c>GBP 9.50</c>, <c>JPY 1200</c>. The amount is a decimal inside, so 0.10 + 0.20 is exactly 0.30.
    /// <para>
    /// It reads <c>GBP 9.50</c>, <c>9.50 GBP</c> (the code in any case, the space optional) and a symbol in front for
    /// the four that are read as one currency: £ (GBP), $ (USD), € (EUR) and ¥ (JPY). Other currencies that share a
    /// symbol (CA$, CN¥) are written with their code. A minus goes in front (<c>-£5</c>, <c>GBP -5</c>). The decimal
    /// point is always ., and , only groups thousands (1,234.50), so <c>3,50</c> is refused rather than guessed at.
    /// An amount with more decimal places than its currency has (GBP 9.505) is refused too.
    /// </para>
    /// <para>
    /// Amounts of one currency add, subtract, divide into each other and compare; any amount scales by a number,
    /// rounding to the currency's smallest unit (half away from zero). Mixing currencies is an error that says so,
    /// since there is no rate to convert with. ->split(n) and ->allocate(ratios) share an amount without losing a
    /// penny.
    /// </para>
    /// </summary>
    public sealed class MoneyType : NativeType
    {
        private static readonly Dictionary<char, string> Symbols = new Dictionary<char, string>
        {
            ['£'] = "GBP", ['$'] = "USD", ['€'] = "EUR", ['¥'] = "JPY",
        };

        private static readonly Regex SymbolFirst = new Regex(@"^(?<neg>-)?(?<sym>[£$€¥])\s*(?<num>-?[0-9][0-9.,]*)$", RegexOptions.CultureInvariant);
        private static readonly Regex CodeFirst = new Regex(@"^(?<code>[A-Za-z]{3})\s*(?<num>-?[0-9][0-9.,]*)$", RegexOptions.CultureInvariant);
        private static readonly Regex CodeLast = new Regex(@"^(?<num>-?[0-9][0-9.,]*)\s*(?<code>[A-Za-z]{3})$", RegexOptions.CultureInvariant);
        private static readonly Regex Plain = new Regex(@"^[0-9]+(\.[0-9]+)?$", RegexOptions.CultureInvariant);
        private static readonly Regex Grouped = new Regex(@"^[0-9]{1,3}(,[0-9]{3})+(\.[0-9]+)?$", RegexOptions.CultureInvariant);

        public override string Name => "money";

        public override string Description => "An amount of money in a currency: GBP 9.50, 9.50 GBP, £9.50 or $12. Kept as the code and the amount, with the currency's decimal places.";

        public override string Example => "'GBP 9.50'";

        public override RawText RawText => RawText.Word;

        public override IReadOnlyList<string> StringMethods => Array.Empty<string>();

        public override IReadOnlyList<string> Completions => Array.Empty<string>();

        public override Equality Equality => Equality.By((a, b) => Code(a) == Code(b) && Amount(a) == Amount(b));

        public override Ordering Ordering => Ordering.By((a, b) => Amount(a).CompareTo(Amount(Same(a, b, "compare"))));

        public override Iteration Iteration => Iteration.None;

        protected override void DeclareOperators(NativeOperators o)
        {
            const string M = "money", N = "number";
            o.Binary("+", M, M, M, "The two amounts added, in one currency: GBP 9.50 + GBP 0.50 is GBP 10.00.", (a, b) => Of(Amount(a) + Amount(Same(a, b, "add")), a))
             .Binary("-", M, M, M, "The second amount taken from the first, in one currency: GBP 10 - GBP 0.50 is GBP 9.50.", (a, b) => Of(Amount(a) - Amount(Same(a, b, "subtract")), a))
             .Binary("*", M, N, M, "The amount times a number, rounded to the currency's smallest unit: GBP 9.50 * 3 is GBP 28.50.", (a, b) => Of(Amount(a) * Factor((double)b), a))
             .Binary("*", N, M, M, "The amount times a number, rounded to the currency's smallest unit: 3 * GBP 9.50 is GBP 28.50.", (a, b) => Of(Factor((double)a) * Amount(b), b))
             .Binary("/", M, N, M, "The amount divided by a number, rounded to the currency's smallest unit: GBP 10 / 4 is GBP 2.50. Use ->split(n) to share it without losing pennies.", (a, b) => Of(Amount(a) / Divisor(Factor((double)b)), a))
             .Binary("/", M, M, N, "How many times the second amount fits in the first, in one currency: GBP 10 / GBP 2.50 is 4.", (a, b) => (double)(Amount(a) / Divisor(Amount(Same(a, b, "divide")))))
             .Negate(M, "The amount with its sign turned over: -GBP 9.50 is GBP -9.50.", v => Of(-Amount(v), v))
             .Falsy("An amount of 0 (GBP 0.00) is false; every other amount is true.", v => Amount(v) == 0);
        }

        protected override void DeclareListMembers(NativeListMembers l)
        {
            l.Method("total", "", "The amounts added, in one currency: ['GBP 9.50', 'GBP 0.50'] gives GBP 10.00. An empty list gives null.", "money", (items, c) =>
                items.Skip(1).Aggregate(items.FirstOrDefault(), (sum, x) => Of(Amount(sum) + Amount(Same(sum, x, "add")), sum)),
                "<let name=\"x\" type=\"money[]\" value=\"['GBP 9.50', 'GBP 0.50']\" />\n<echo>{ $x->total() }</echo>");
        }

        protected override void DeclareMembers(NativeMembers m)
        {
            const string S = KType.String, N = KType.Number;
            const string Let = "<let name=\"x\" type=\"money\" value=\"'GBP 10.00'\" />\n";
            m.Field("amount", N, "The amount as a number, without the currency: 9.5 for GBP 9.50.", (t, _) => (double)Amount(t), Let + "<echo>{ $x.amount }</echo>");
            m.Field("currency", "currency", "The currency the amount is in: GBP.", (t, _) => ((NativeText)t).Part("currency"), Let + "<echo>{ $x.currency.name }</echo>");
            m.Method("format", "locale?: string", "The amount written for a locale, or the current one: £10.00 in en, 10,00 £ in fr.", S, (t, c) =>
            {
                string locale = c.Arg(0) is object l && Values.ToStr(l).Length > 0
                    ? Translator.Normalise(Values.ToStr(l))
                    : Translator.For(c.Interpreter.Host, c.Module?.Directory ?? c.Interpreter.Host.WorkingDirectory).Locale;
                return Formats.Currency((double)Amount(t), Code(t), locale, Digits(t));
            }, Let + "<echo>{ $x->format('en') } { $x->format('fr') }</echo>");
            m.Method("split", "parts: number", "The amount shared into this many parts that add up to it exactly: GBP 10.00 in 3 is GBP 3.34, GBP 3.33 and GBP 3.33.", "money[]", (t, c) =>
            {
                double n = c.Num(0);
                if (n < 1 || n != Math.Floor(n) || n > 10_000)
                    throw new KScriptException($"->split() needs a whole number of parts from 1 to 10000, not {Values.ToStr(c.Arg(0))}.", "Argument");
                return Allocate(t, Enumerable.Repeat(1m, (int)n).ToList());
            }, Let + "<echo>{ $x->split(3) }</echo>");
            m.Method("allocate", "ratios: list", "The amount shared by these ratios, in parts that add up to it exactly: GBP 10.00 by [1, 1, 2] is GBP 2.50, GBP 2.50 and GBP 5.00.", "money[]", (t, c) =>
            {
                if (c.Arg(0) is not List<object> list || list.Count == 0)
                    throw new KScriptException($"->allocate() needs a list of ratios, such as [1, 1, 2], not {Values.Describe(c.Arg(0))}.", "Argument");
                var ratios = list.Select(r => r is double d && d >= 0 && double.IsFinite(d) ? Factor(d)
                    : throw new KScriptException($"->allocate() needs ratios that are numbers of 0 or more, not {Values.Describe(r)}.", "Argument")).ToList();
                if (ratios.Sum() == 0) throw new KScriptException("->allocate() needs at least one ratio above 0.", "Argument");
                return Allocate(t, ratios);
            }, Let + "<echo>{ $x->allocate([1, 1, 2]) }</echo>");
        }

        public override (object Value, string Reason) Parse(string s)
        {
            string t = s.Trim();
            if (t.Length == 0) return (null, "it is empty");
            string code, number;
            bool negative = false;
            Match match;
            if ((match = SymbolFirst.Match(t)).Success)
            {
                code = Symbols[match.Groups["sym"].Value[0]];
                number = match.Groups["num"].Value;
                negative = match.Groups["neg"].Success;
                if (negative && number.StartsWith('-')) return (null, "it has two minus signs");
            }
            else if ((match = CodeFirst.Match(t)).Success || (match = CodeLast.Match(t)).Success)
            {
                code = match.Groups["code"].Value.ToUpperInvariant();
                number = match.Groups["num"].Value;
            }
            else if (Plain.IsMatch(t.TrimStart('-')) || Grouped.IsMatch(t.TrimStart('-')))
            {
                return (null, "it needs a currency, as in GBP 9.50, 9.50 GBP or £9.50");
            }
            else
            {
                return (null, "write it as a currency code and an amount, such as GBP 9.50 or 9.50 GBP, or with £, $, € or ¥ in front");
            }
            if (number.StartsWith('-'))
            {
                negative = true;
                number = number.Substring(1);
            }
            if (!Plain.IsMatch(number) && !Grouped.IsMatch(number))
                return (null, $"{number} isn't an amount: the decimal point is . and , only groups thousands, as in 1,234.50");
            NativeText currency = CurrencyType.Find(code);
            if (currency == null) return (null, $"{code} isn't an ISO 4217 currency code");
            int digits = (int)(double)currency.Part("digits");
            int point = number.IndexOf('.');
            int places = point < 0 ? 0 : number.Length - point - 1;
            if (places > digits)
                return (null, digits == 0 ? $"{code} amounts are whole numbers" : $"{code} amounts have at most {digits} decimal place{(digits == 1 ? string.Empty : "s")}");
            decimal amount = decimal.Parse(number.Replace(",", string.Empty), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
            return (Of(negative ? -amount : amount, currency), null);
        }

        /// <summary>
        /// The amount in the currency, rounded to its smallest unit (half away from zero). <paramref name="currency"/>
        /// is a currency value or a money value whose currency is used.
        /// </summary>
        public static NativeText Of(decimal amount, object currency)
        {
            var c = (NativeText)currency;
            if (c.TypeName == "money") c = (NativeText)c.Part("currency");
            int digits = (int)(double)c.Part("digits");
            decimal rounded = Math.Round(amount, digits, MidpointRounding.AwayFromZero);
            if (rounded == 0) rounded = 0m; // no -0.00
            string text = c.Text + " " + rounded.ToString("F" + digits, CultureInfo.InvariantCulture);
            return new NativeText("money", text, new Dictionary<string, object>
            {
                ["amount"] = rounded,
                ["currency"] = c,
            });
        }

        /// <summary>The amount in a money value, as a decimal.</summary>
        public static decimal Amount(object money) => (decimal)((NativeText)money).Part("amount");

        /// <summary>The currency code of a money value: GBP.</summary>
        public static string Code(object money) => ((NativeText)((NativeText)money).Part("currency")).Text;

        private static int Digits(object money) => (int)(double)((NativeText)((NativeText)money).Part("currency")).Part("digits");

        /// <summary><paramref name="b"/>, when it is in the same currency as <paramref name="a"/>; otherwise an error that says so.</summary>
        private static object Same(object a, object b, string verb)
        {
            if (Code(a) == Code(b)) return b;
            throw new KScriptException($"Can't {verb} {((NativeText)a).Text} and {((NativeText)b).Text}: they are in different currencies. Convert one to the other's currency first.", "Type");
        }

        private static decimal Factor(double n)
        {
            if (!double.IsFinite(n) || Math.Abs(n) > 1e15) throw new KScriptException($"{Values.ToStr(n)} is too large to use with money.", "Math");
            return (decimal)n;
        }

        private static decimal Divisor(decimal d) => d == 0 ? throw new KScriptException("Division by zero.", "Math") : d;

        /// <summary>
        /// The amount shared by <paramref name="ratios"/> in whole smallest units, so the parts add up to it exactly:
        /// each part gets its share rounded down, and the units left over go one each to the parts that lost most.
        /// </summary>
        private static List<object> Allocate(object money, List<decimal> ratios)
        {
            decimal unit = 1m;
            for (int i = 0; i < Digits(money); i++) unit /= 10;
            decimal total = Amount(money);
            decimal units = Math.Abs(total) / unit;
            decimal sum = ratios.Sum();
            var shares = new decimal[ratios.Count];
            var lost = new decimal[ratios.Count];
            for (int i = 0; i < ratios.Count; i++)
            {
                decimal exact = units * ratios[i] / sum;
                shares[i] = Math.Floor(exact);
                lost[i] = exact - shares[i];
            }
            decimal left = units - shares.Sum();
            foreach (int i in Enumerable.Range(0, ratios.Count).OrderByDescending(i => lost[i]).ThenBy(i => i).Take((int)left)) shares[i]++;
            int sign = total < 0 ? -1 : 1;
            return shares.Select(share => (object)Of(sign * share * unit, money)).ToList();
        }
    }
}
