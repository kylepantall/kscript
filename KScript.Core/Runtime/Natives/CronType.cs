using System.Globalization;

using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// <c>cron</c>: a schedule of five fields (minute hour day month weekday) or a macro such as @daily, kept in
    /// lower case with single spaces. Two schedules are equal when they run at the same times (@daily ==
    /// '0 0 * * *'). They have no order and take no operators, and a for loop can't walk one because it never
    /// ends: ->next() and ->next_times(n) give the times.
    /// </summary>
    public sealed class CronType : NativeType
    {
        private static readonly Dictionary<string, string> Macros = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["@yearly"] = "0 0 1 1 *", ["@annually"] = "0 0 1 1 *", ["@monthly"] = "0 0 1 * *", ["@weekly"] = "0 0 * * 0",
            ["@daily"] = "0 0 * * *", ["@midnight"] = "0 0 * * *", ["@hourly"] = "0 * * * *",
        };

        private static readonly string[] Months = { "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" };
        private static readonly string[] Days = { "sun", "mon", "tue", "wed", "thu", "fri", "sat" };

        public override string Name => "cron";

        public override string Description => "A cron schedule of five fields (minute hour day month weekday), or @daily, @hourly and the like.";

        public override string Example => "'*/15 9-17 * * mon-fri'";

        public override RawText RawText => RawText.Word;

        public override IReadOnlyList<string> StringMethods => NativeTypes.Searchable;

        public override IReadOnlyList<string> Completions => new[] { "@hourly", "@daily", "@weekly", "@monthly", "*/15 * * * *", "0 9 * * mon-fri" };

        public override Equality Equality => Equality.By(SameSchedule);

        public override Ordering Ordering => Ordering.None;

        // A schedule runs for ever, so a for loop over it would never end.
        public override Iteration Iteration => Iteration.None;

        protected override void DeclareOperators(NativeOperators o)
        {
        }

        protected override void DeclareMembers(NativeMembers m)
        {
            const string Let = "<let name=\"x\" type=\"cron\" value=\"'0 9 * * mon-fri'\" />\n";
            m.Method("next", "after?: date", "The next time the schedule runs, after a date or after now.", KType.Date, (t, c) =>
                Next((NativeText)t, c.Arg(0) is DateTime d ? d : DateTime.Now),
                Let + "<echo>{ $x->next('2026-11-21T10:00:00'->to_date()) }</echo>");
            m.Method("next_times", "count: int, after?: date", "The next few times it runs.", KType.List, (t, c) =>
            {
                var times = new List<object>();
                DateTime at = c.Arg(1) is DateTime d ? d : DateTime.Now;
                for (int i = 0; i < Math.Clamp(c.Int(0), 0, 1000); i++) times.Add(at = Next((NativeText)t, at));
                return times;
            }, Let + "<echo>{ $x->next_times(3, '2026-11-21T10:00:00'->to_date()) }</echo>");
        }

        public override (object Value, string Reason) Parse(string s)
        {
            string t = string.Join(" ", s.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
            string expanded = Macros.TryGetValue(t, out string macro) ? macro : t;
            string[] fields = expanded.Split(' ');
            if (fields.Length != 5) return (null, "it needs five fields: minute hour day month weekday (or @daily, @hourly, ...)");
            var names = new[] { "minute", "hour", "day", "month", "weekday" };
            var ranges = new[] { (0, 59), (0, 23), (1, 31), (1, 12), (0, 7) };
            var sets = new List<object>();
            for (int f = 0; f < 5; f++)
            {
                var allowed = new HashSet<int>();
                foreach (string item in fields[f].ToLowerInvariant().Split(','))
                {
                    string range = item, step = null;
                    int slash = item.IndexOf('/');
                    if (slash >= 0) { range = item.Substring(0, slash); step = item.Substring(slash + 1); }
                    (int lo, int hi) = ranges[f];
                    int from = lo, to = hi;
                    if (range != "*")
                    {
                        string[] ends = range.Split('-');
                        if (ends.Length > 2 || !Value(ends[0], f, out from) || !Value(ends[^1], f, out to))
                            return (null, $"'{item}' isn't a {names[f]} (use * , - / and {lo}-{hi}{(f == 3 ? " or jan-dec" : f == 4 ? " or sun-sat" : string.Empty)})");
                        if (ends.Length == 1 && step != null) to = hi;
                    }
                    if (from < lo || to > hi || from > to) return (null, $"'{item}' is outside {lo}-{hi} for the {names[f]}");
                    int by = 1;
                    if (step != null && (!int.TryParse(step, NumberStyles.None, CultureInfo.InvariantCulture, out by) || by == 0)) return (null, $"'{step}' isn't a step for the {names[f]}");
                    for (int v = from; v <= to; v += by) allowed.Add(f == 4 && v == 7 ? 0 : v);
                }
                sets.Add(allowed);
            }
            return (new NativeText("cron", t, new Dictionary<string, object>
            {
                ["sets"] = sets,
                ["any_day"] = fields[2] == "*",
                ["any_weekday"] = fields[4] == "*",
            }), null);
        }

        /// <summary>The next time after <paramref name="after"/> that the schedule fires, to the minute.</summary>
        public static DateTime Next(NativeText cron, DateTime after)
        {
            HashSet<int>[] sets = Sets(cron);
            bool anyDay = (bool)cron.Part("any_day"), anyWeekday = (bool)cron.Part("any_weekday");
            DateTime t = new DateTime(after.Year, after.Month, after.Day, after.Hour, after.Minute, 0, after.Kind).AddMinutes(1);
            DateTime limit = t.AddYears(5);
            while (t < limit)
            {
                if (!sets[3].Contains(t.Month)) { t = new DateTime(t.Year, t.Month, 1, 0, 0, 0, t.Kind).AddMonths(1); continue; }
                bool day = sets[2].Contains(t.Day), weekday = sets[4].Contains((int)t.DayOfWeek);
                bool dayOk = anyDay && anyWeekday ? true : anyDay ? weekday : anyWeekday ? day : day || weekday;
                if (!dayOk) { t = t.Date.AddDays(1); continue; }
                if (!sets[1].Contains(t.Hour)) { t = t.Date.AddHours(t.Hour + 1); continue; }
                if (!sets[0].Contains(t.Minute)) { t = t.AddMinutes(1); continue; }
                return t;
            }
            throw new KScriptException($"The cron schedule '{cron.Text}' never runs.", "Value");
        }

        private static HashSet<int>[] Sets(NativeText cron) => ((List<object>)cron.Part("sets")).Cast<HashSet<int>>().ToArray();

        /// <summary>The same minutes, hours, days, months and weekdays: @daily and '0 0 * * *', or mon-fri and 1-5.</summary>
        private static bool SameSchedule(object a, object b)
        {
            NativeText x = (NativeText)a, y = (NativeText)b;
            if (x.Text == y.Text) return true;
            if (!Equals(x.Part("any_day"), y.Part("any_day")) || !Equals(x.Part("any_weekday"), y.Part("any_weekday"))) return false;
            return Sets(x).Zip(Sets(y)).All(p => p.First.SetEquals(p.Second));
        }

        private static bool Value(string text, int field, out int value)
        {
            if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value)) return true;
            value = field == 3 ? Array.IndexOf(Months, text) + 1 : field == 4 ? Array.IndexOf(Days, text) : -1;
            return value > (field == 3 ? 0 : -1);
        }
    }
}
