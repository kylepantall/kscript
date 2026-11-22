using System.Globalization;
using System.Text;

using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// <c>duration</c>: a length of time such as 1h30m, 90s or PT1H30M, kept in its short form with its seconds.
    /// Lengths add, subtract, scale and compare (90s &lt; 2m), move dates (<c>$start + 1h</c>) and step date ranges
    /// (<c>$monday..$friday step 1d</c>). This is the type to copy when writing a new one.
    /// </summary>
    public sealed class DurationType : NativeType
    {
        private static readonly (char Unit, double Seconds)[] Units = { ('w', 604800), ('d', 86400), ('h', 3600), ('m', 60), ('s', 1) };

        private static readonly Dictionary<string, double> UnitNames = new Dictionary<string, double>
        {
            ["w"] = 604800, ["d"] = 86400, ["h"] = 3600, ["m"] = 60, ["min"] = 60, ["s"] = 1, ["ms"] = 0.001,
        };

        public override string Name => "duration";

        public override string Description => "A length of time: 1h30m, 90s, 2d or ISO 8601 such as PT1H30M. Kept in its short form.";

        public override string Example => "1h30m";

        public override RawText RawText => RawText.Word;

        public override bool TakesNumbers => true;

        public override IReadOnlyList<string> StringMethods => NativeTypes.Searchable;

        public override IReadOnlyList<string> Completions => new[] { "30s", "5m", "1h", "1h30m", "1d", "1w" };

        public override Equality Equality => Equality.ByText;

        public override Ordering Ordering => Ordering.By((a, b) => Seconds(a).CompareTo(Seconds(b)));

        public override Iteration Iteration => Iteration.None;

        protected override void DeclareOperators(NativeOperators o)
        {
            const string D = "duration", N = "number", T = "date";
            o.Binary("+", D, D, D, "The two lengths added: 1h + 30m is 1h30m.", (a, b) => Of(Seconds(a) + Seconds(b)))
             .Binary("-", D, D, D, "The second length taken from the first: 1h - 15m is 45m.", (a, b) => Of(Seconds(a) - Seconds(b)))
             .Binary("*", D, N, D, "The length times a number: 30m * 3 is 1h30m.", (a, b) => Of(Seconds(a) * (double)b))
             .Binary("*", N, D, D, "The length times a number: 3 * 30m is 1h30m.", (a, b) => Of((double)a * Seconds(b)))
             .Binary("/", D, N, D, "The length split into equal parts: 1h / 4 is 15m.", (a, b) => Of(Seconds(a) / Divisor((double)b)))
             .Binary("/", D, D, N, "How many times the second length fits in the first: 1h / 15m is 4.", (a, b) => Seconds(a) / Divisor(Seconds(b)))
             .Binary("%", D, D, D, "What is left over after the second length fits in the first as many times as it can: 1h10m % 30m is 10m.", (a, b) => Of(Seconds(a) % Divisor(Seconds(b))))
             .Binary("+", T, D, T, "The date this long after: @now() + 1h.", (a, b) => ((DateTime)a).AddSeconds(Seconds(b)))
             .Binary("+", D, T, T, "The date this long after: 1h + @now().", (a, b) => ((DateTime)b).AddSeconds(Seconds(a)))
             .Binary("-", T, D, T, "The date this long before: @now() - 1d.", (a, b) => ((DateTime)a).AddSeconds(-Seconds(b)))
             .Negate(D, "The same length the other way: -1h.", v => Of(-Seconds(v)))
             .Range(T, D, "Dates from the start up to (not including) the end, a step apart: $monday..$saturday step 1d. Without a step, a day apart.", DateRange)
             .Falsy("0s is false; every other length is true.", v => Seconds(v) == 0);
        }

        protected override void DeclareMembers(NativeMembers m)
        {
            const string S = KType.String, N = KType.Number;
            const string Let = "<let name=\"x\" type=\"duration\" value=\"'1h30m'\" />\n";
            m.Field("seconds", N, "The length in seconds: 5400 for 1h30m.", (t, _) => Seconds(t), Let + "<echo>{ $x.seconds }</echo>");
            m.Field("minutes", N, "The length in minutes: 90 for 1h30m.", (t, _) => Seconds(t) / 60, Let + "<echo>{ $x.minutes }</echo>");
            m.Field("hours", N, "The length in hours: 1.5 for 1h30m.", (t, _) => Seconds(t) / 3600, Let + "<echo>{ $x.hours }</echo>");
            m.Field("ms", N, "The length in milliseconds.", (t, _) => Seconds(t) * 1000, Let + "<echo>{ $x.ms }</echo>");
            m.Method("iso", "", "The length in ISO 8601: PT1H30M.", S, (t, c) => System.Xml.XmlConvert.ToString(TimeSpan.FromSeconds(Seconds(t))),
                Let + "<echo>{ $x->iso() }</echo>");
            m.Method("after", "time?: date", "The time this long after a date, or after now.", KType.Date, (t, c) => (c.Arg(0) is DateTime d ? d : DateTime.Now).AddSeconds(Seconds(t)),
                Let + "<echo>{ $x->after('2026-11-21T09:00:00'->to_date()) }</echo>");
            m.Method("before", "time?: date", "The time this long before a date, or before now.", KType.Date, (t, c) => (c.Arg(0) is DateTime d ? d : DateTime.Now).AddSeconds(-Seconds(t)),
                Let + "<echo>{ $x->before('2026-11-21T09:00:00'->to_date()) }</echo>");
            m.Method("plus", "other: duration", "The two lengths added: 1h + 30m is 1h30m.", "duration", (t, c) =>
                Of(Seconds(t) + Seconds(Coerce(c.Arg(0)) ?? throw new KScriptException($"plus needs a duration, such as 30m, not {Values.Describe(c.Arg(0))}.", "Type"))),
                Let + "<echo>{ $x->plus(45m) }</echo>");
        }

        protected override void DeclareListMembers(NativeListMembers l)
        {
        }

        public override (object Value, string Reason) Parse(string s)
        {
            string t = s.Trim();
            if (t.Length == 0) return (null, "it is empty");
            if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double plain)) return (Of(plain), null);
            bool negative = t.StartsWith('-');
            if (negative) t = t.Substring(1);
            double total = 0;
            if (t.StartsWith('P') || t.StartsWith('p'))
            {
                try
                {
                    total = System.Xml.XmlConvert.ToTimeSpan(t.ToUpperInvariant()).TotalSeconds;
                }
                catch (FormatException)
                {
                    return (null, "it isn't an ISO 8601 duration such as PT1H30M");
                }
                return (Of(negative ? -total : total), null);
            }
            int i = 0;
            string compact = t.Replace(" ", string.Empty).ToLowerInvariant();
            if (compact.Length == 0) return (null, "it is empty");
            while (i < compact.Length)
            {
                int start = i;
                while (i < compact.Length && (char.IsAsciiDigit(compact[i]) || compact[i] == '.')) i++;
                if (i == start || !double.TryParse(compact.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
                    return (null, "write it as numbers with units, such as 1h30m, 90s or 2d (units w, d, h, m, s, ms)");
                int unitStart = i;
                while (i < compact.Length && char.IsAsciiLetter(compact[i])) i++;
                string unit = compact.Substring(unitStart, i - unitStart);
                if (!UnitNames.TryGetValue(unit, out double size)) return (null, unit.Length == 0 ? $"{n} needs a unit (w, d, h, m, s or ms)" : $"'{unit}' isn't a unit (use w, d, h, m, s or ms)");
                total += n * size;
            }
            return (Of(negative ? -total : total), null);
        }

        /// <summary>A duration of this many seconds.</summary>
        public static NativeText Of(double seconds) =>
            new NativeText("duration", Text(seconds), new Dictionary<string, object> { ["seconds"] = seconds });

        /// <summary>The seconds in a duration value.</summary>
        public static double Seconds(object duration) => (double)((NativeText)duration).Part("seconds");

        /// <summary>Seconds as a duration's short form: 5400 is 1h30m.</summary>
        public static string Text(double seconds)
        {
            if (seconds == 0) return "0s";
            var sb = new StringBuilder(seconds < 0 ? "-" : string.Empty);
            double left = Math.Abs(seconds);
            foreach ((char unit, double size) in Units)
            {
                if (unit == 's')
                {
                    if (left > 0) sb.Append(Math.Round(left, 3).ToString(CultureInfo.InvariantCulture)).Append('s');
                    break;
                }
                double n = Math.Floor(left / size);
                if (n > 0) { sb.Append(n.ToString(CultureInfo.InvariantCulture)).Append(unit); left -= n * size; }
            }
            return sb.ToString();
        }

        private static double Divisor(double d) => d == 0 ? throw new KScriptException("Division by zero.", "Math") : d;

        private static IEnumerable<object> DateRange(object start, object end, object step)
        {
            DateTime from = (DateTime)start, to = (DateTime)end;
            double seconds = step == null ? (to >= from ? 86400 : -86400) : Seconds(step);
            if (seconds == 0) throw new KScriptException("A range can't have a step of 0s.", "Argument");
            for (DateTime at = from; seconds > 0 ? at < to : at > to; at = at.AddSeconds(seconds)) yield return at;
        }
    }
}
