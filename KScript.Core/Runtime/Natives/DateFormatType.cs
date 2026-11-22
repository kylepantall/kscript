using System.Globalization;

using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// <c>date_format</c>: how a date is written, such as dd-MM-yyyy or d MMMM yyyy, HH:mm. It is checked letter by
    /// letter, so YYYY and DD are caught where they are written. It takes no operators.
    /// </summary>
    public sealed class DateFormatType : NativeType
    {
        public override string Name => "date_format";

        public override string Description => "How a date is written, such as dd-MM-yyyy or d MMMM yyyy, HH:mm: dd is the day, MM the month, yyyy the year, HH the hour and mm the minute. Text goes in quotes ('at' HH:mm).";

        public override string Example => "dd-MM-yyyy";

        public override RawText RawText => RawText.Word;

        public override IReadOnlyList<string> StringMethods => NativeTypes.Searchable;

        public override IReadOnlyList<string> Completions => new[] { "yyyy-MM-dd", "dd-MM-yyyy", "d MMMM yyyy", "HH:mm", "dddd d MMMM", "o" };

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
            const string S = KType.String, B = KType.Bool, L = KType.List;
            static object Part(object t, string name) => ((NativeText)t).Part(name);
            static string Pattern(object t) => DotNet((NativeText)t);
            m.Field("tokens", L, "Its parts in order: ['dd', '-', 'MM', '-', 'yyyy'].", (t, _) => new List<object>((List<object>)Part(t, "tokens")), "<let name=\\\"x\\\" type=\\\"date_format\\\" value=\\\"'dd MMM yyyy, HH:mm'\\\" />\\n<echo>{ $x.tokens }</echo>");
            m.Field("has_date", B, "True when it shows a day, month, year or era.", (t, _) => Part(t, "has_date"), "<let name=\\\"x\\\" type=\\\"date_format\\\" value=\\\"'dd MMM yyyy, HH:mm'\\\" />\\n<echo>{ $x.has_date }</echo>");
            m.Field("has_time", B, "True when it shows an hour, minute, second or AM/PM.", (t, _) => Part(t, "has_time"), "<let name=\\\"x\\\" type=\\\"date_format\\\" value=\\\"'dd MMM yyyy, HH:mm'\\\" />\\n<echo>{ $x.has_time }</echo>");
            m.Field("example", S, "A date written this way, to show what it looks like: 07-03-2026 for dd-MM-yyyy (Saturday 7 March 2026, 14:05:09).", (t, _) =>
                new DateTime(2026, 3, 7, 14, 5, 9, 123).ToString(Pattern(t), CultureInfo.InvariantCulture), "<let name=\\\"x\\\" type=\\\"date_format\\\" value=\\\"'dd MMM yyyy, HH:mm'\\\" />\\n<echo>{ $x.example }</echo>");
            m.Method("format", "date: date", "The date written this way: 07-03-2026.", S, (t, c) =>
                c.Arg(0) is DateTime when ? when.ToString(Pattern(t), CultureInfo.InvariantCulture) : throw new KScriptException($"->format() needs a date, but got {Values.Describe(c.Arg(0))}.", "Type"),
                "<let name=\\\"x\\\" type=\\\"date_format\\\" value=\\\"'dd MMM yyyy, HH:mm'\\\" />\\n<echo>{ $x->format('2026-11-22T16:30:00'->to_date()) }</echo>");
            m.Method("parse", "text: string", "Reads text written this way as a date: dd-MM-yyyy reads '07-03-2026'.", KType.Date, (t, c) =>
                DateTime.TryParseExact(c.Str(0).Trim(), Pattern(t), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime read)
                    ? read : throw new KScriptException($"'{c.Str(0)}' isn't a date written {((NativeText)t).Text}, such as {new DateTime(2026, 3, 7, 14, 5, 9).ToString(Pattern(t), CultureInfo.InvariantCulture)}.", "Format"),
                "<let name=\\\"x\\\" type=\\\"date_format\\\" value=\\\"'dd MMM yyyy, HH:mm'\\\" />\\n<echo>{ $x->parse('22 Nov 2026, 16:30')->format('yyyy-MM-dd') }</echo>");
        }

        /// <summary>What each letter of a date format means, and the longest run of it that means something.</summary>
        public static readonly IReadOnlyDictionary<char, (int Max, string Meaning)> DateLetters = new Dictionary<char, (int, string)>
        {
            ['d'] = (4, "the day: d is 7, dd is 07, ddd is Sat, dddd is Saturday"),
            ['M'] = (4, "the month: M is 3, MM is 03, MMM is Mar, MMMM is March"),
            ['y'] = (5, "the year: yy is 26, yyyy is 2026"),
            ['h'] = (2, "the hour on a 12-hour clock: h is 2, hh is 02"),
            ['H'] = (2, "the hour on a 24-hour clock: H is 14, HH is 14"),
            ['m'] = (2, "the minute: m is 5, mm is 05"),
            ['s'] = (2, "the second: s is 9, ss is 09"),
            ['f'] = (7, "fractions of a second: fff is milliseconds"),
            ['F'] = (7, "fractions of a second, without trailing zeros"),
            ['t'] = (2, "AM or PM: t is P, tt is PM"),
            ['z'] = (3, "the offset from UTC: z is +1, zz is +01, zzz is +01:00"),
            ['K'] = (1, "the time zone: Z for UTC or an offset such as +01:00"),
            ['g'] = (2, "the era: A.D."),
        };

        /// <summary>Letters people reach for from other languages, and what to write instead.</summary>
        private static readonly Dictionary<char, string> WrongDateLetters = new Dictionary<char, string>
        {
            ['Y'] = "years are y (yyyy)",
            ['D'] = "days are d (dd)",
            ['S'] = "seconds are s (ss) and fractions are f (fff)",
            ['a'] = "AM/PM is tt",
            ['A'] = "AM/PM is tt",
            ['E'] = "weekday names are ddd or dddd",
            ['n'] = "minutes are m (mm)",
            ['i'] = "minutes are m (mm)",
            ['G'] = "the hour is H (HH) or h (hh)",
            ['k'] = "the hour is H (HH) or h (hh)",
            ['u'] = "weekday names are ddd or dddd",
        };

        /// <summary>The standard one-letter .NET formats a date_format also takes: o is round-trip ISO 8601, and so on.</summary>
        internal static readonly HashSet<char> StandardDateFormats = new HashSet<char> { 'd', 'D', 'f', 'F', 'g', 'G', 'm', 'M', 'o', 'O', 'r', 'R', 's', 't', 'T', 'u', 'U', 'y', 'Y' };

        /// <summary>
        /// A .NET date format such as dd-MM-yyyy, d MMMM yyyy or HH:mm, or a standard one-letter format such as o.
        /// Every letter must mean something (YYYY and DD are the usual slips), runs can't be longer than they mean
        /// anything for, and text goes in quotes ('at' or "at") or after \. T and Z stand for themselves, for ISO
        /// 8601 (yyyy-MM-ddTHH:mm:ssZ).
        /// </summary>
        public override (object Value, string Reason) Parse(string s)
        {
            if (s.Trim().Length == 0) return (null, "it is empty: try dd-MM-yyyy");
            if (s.Length == 1)
            {
                if (!StandardDateFormats.Contains(s[0])) return (null, $"{s} isn't a date letter or a standard format such as o");
                return (new NativeText("date_format", s, new Dictionary<string, object>
                {
                    ["tokens"] = new List<object> { s }, ["has_date"] = s[0] is not ('t' or 'T'), ["has_time"] = s[0] is not ('d' or 'D' or 'm' or 'M' or 'y' or 'Y'), ["standard"] = true,
                }), null);
            }
            var tokens = new List<object>();
            bool date = false, time = false;
            for (int i = 0; i < s.Length;)
            {
                char c = s[i];
                if (c is '\'' or '"')
                {
                    int close = s.IndexOf(c, i + 1);
                    if (close < 0) return (null, $"its {c} isn't closed");
                    tokens.Add(s.Substring(i, close + 1 - i));
                    i = close + 1;
                    continue;
                }
                if (c == '\\')
                {
                    if (i + 1 >= s.Length) return (null, "it ends with \\");
                    tokens.Add(s.Substring(i, 2));
                    i += 2;
                    continue;
                }
                if (c == '%' && i + 1 < s.Length && DateLetters.ContainsKey(s[i + 1]))
                {
                    i++;
                    continue;
                }
                if (DateLetters.TryGetValue(c, out var letter))
                {
                    int run = 1;
                    while (i + run < s.Length && s[i + run] == c) run++;
                    if (run > letter.Max) return (null, $"{new string(c, run)} is longer than {c} goes; use at most {new string(c, letter.Max)}");
                    tokens.Add(new string(c, run));
                    if (c is 'd' or 'M' or 'y' or 'g') date = true;
                    else if (c is 'h' or 'H' or 'm' or 's' or 'f' or 'F' or 't') time = true;
                    i += run;
                    continue;
                }
                if (char.IsAsciiLetter(c) && c is not ('T' or 'Z'))
                {
                    string hint = WrongDateLetters.TryGetValue(c, out string why) ? why : $"put text in quotes: '{c}'";
                    return (null, $"{c} isn't a date letter: {hint}");
                }
                if (tokens.Count > 0 && tokens[^1] is string last && last.Length > 0 && !DateLetters.ContainsKey(last[0]) && last[0] is not ('\'' or '"' or '\\'))
                    tokens[^1] = last + c;
                else
                    tokens.Add(c.ToString());
                i++;
            }
            if (!date && !time && !tokens.Any(t => t is string text && text.Length > 0 && DateLetters.ContainsKey(text[0])))
                return (null, "it has no date or time letters in it: try dd-MM-yyyy");
            return (new NativeText("date_format", s, new Dictionary<string, object> { ["tokens"] = tokens, ["has_date"] = date, ["has_time"] = time, ["standard"] = false }), null);
        }

        /// <summary>The format .NET is given: a one-letter custom format needs % in front, so d is the day rather than the short date.</summary>
        internal static string DotNet(NativeText format) => (bool)format.Part("standard") ? format.Text : format.Text.Length == 1 ? "%" + format.Text : format.Text;
    }
}
