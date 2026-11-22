using System.Globalization;
using System.Text;

using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// <c>timezone</c>: an IANA time zone such as Europe/London, kept under its IANA name. Zones sort by name (as a
    /// zone picker lists them) but take no operators: two zones don't add up to anything, and the times in them come
    /// from its methods ($zone->now(), $zone->convert($date)).
    /// </summary>
    public sealed class TimezoneType : NativeType
    {
        public override string Name => "timezone";

        public override string Description => "An IANA time zone such as Europe/London or America/New_York, or UTC.";

        public override string Example => "Europe/London";

        public override RawText RawText => RawText.Word;

        public override IReadOnlyList<string> StringMethods => NativeTypes.Searchable.Append("split").ToArray();

        public override IReadOnlyList<string> Completions => new[] { "UTC", "Europe/London", "Europe/Paris", "America/New_York", "America/Los_Angeles", "Asia/Tokyo", "Australia/Sydney" };

        public override Equality Equality => Equality.ByText;

        // By name: a zone's offset changes with summer time, so there is no one order by time.
        public override Ordering Ordering => Ordering.ByText;

        public override Iteration Iteration => Iteration.None;

        protected override void DeclareOperators(NativeOperators o)
        {
        }

        protected override void DeclareMembers(NativeMembers m)
        {
            const string S = KType.String, B = KType.Bool;
            const string Let = "<let name=\"x\" type=\"timezone\" value=\"'Europe/London'\" />\n";
            m.Method("offset", "at?: date", "Its offset from UTC, such as +01:00, now or at a date.", S, (t, c) =>
            {
                DateTime at = c.Arg(0) is DateTime d ? d : DateTime.UtcNow;
                TimeSpan offset = Zone(t).GetUtcOffset(at);
                return (offset < TimeSpan.Zero ? "-" : "+") + offset.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
            }, Let + "<echo>{ $x->offset('2026-07-01'->to_date()) }</echo>");
            m.Method("now", "", "The time now in this zone.", KType.Date, (t, c) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone(t)),
                Let + "<echo>{ $x->now()->type() }</echo>");
            m.Method("convert", "time: date", "The same moment as the local time in this zone.", KType.Date, (t, c) =>
                TimeZoneInfo.ConvertTime(c.Arg(0) is DateTime d ? d : throw new KScriptException("convert needs a date.", "Type"), Zone(t)),
                Let + "<echo>{ $x->convert('2026-07-01T12:00:00Z'->to_date()) }</echo>");
            m.Method("is_dst", "at?: date", "True when summer time is in effect, now or at a date.", B, (t, c) => Zone(t).IsDaylightSavingTime(c.Arg(0) is DateTime d ? d : DateTime.UtcNow),
                Let + "<echo>{ $x->is_dst('2026-01-15'->to_date()) }</echo>");
        }

        public override (object Value, string Reason) Parse(string s)
        {
            string t = s.Trim();
            if (t.Length == 0) return (null, "it is empty");
            if (t.Equals("utc", StringComparison.OrdinalIgnoreCase) || t.Equals("z", StringComparison.OrdinalIgnoreCase)) t = "UTC";
            TimeZoneInfo zone;
            try
            {
                zone = TimeZoneInfo.FindSystemTimeZoneById(t);
            }
            catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                // An id is found in the case it was written, or not at all on a first lookup: europe/london only
                // worked once Europe/London had been looked up. Find it without case, so it doesn't depend on
                // what ran before or on the system (Windows lists its own names, so match their IANA names too).
                zone = WithoutCase(t);
                if (zone == null) return (null, "it isn't an IANA time zone, such as Europe/London");
            }
            string id = zone.HasIanaId ? zone.Id : TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out string iana) ? iana : zone.Id;
            if (!t.Contains('/') && t != "UTC" && id != t) return (null, $"it isn't an IANA time zone; use {id}");
            return (new NativeText("timezone", id, new Dictionary<string, object> { ["zone"] = zone }), null);
        }

        /// <summary>The system's zone behind a timezone value.</summary>
        public static TimeZoneInfo Zone(object timezone) => (TimeZoneInfo)((NativeText)timezone).Part("zone");

        private static TimeZoneInfo WithoutCase(string id)
        {
            foreach (TimeZoneInfo z in TimeZoneInfo.GetSystemTimeZones())
            {
                if (string.Equals(z.Id, id, StringComparison.OrdinalIgnoreCase)) return z;
                if (!z.HasIanaId && TimeZoneInfo.TryConvertWindowsIdToIanaId(z.Id, out string iana) && string.Equals(iana, id, StringComparison.OrdinalIgnoreCase)) return z;
            }

            // A zone the system doesn't list under that name (an alias, or a second city of a Windows zone):
            // IANA names start each word with a capital, so try it written that way.
            var sb = new StringBuilder(id.Length);
            bool start = true;
            foreach (char c in id)
            {
                sb.Append(start ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
                start = c == '/' || c == '_' || c == '-';
            }

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(sb.ToString());
            }
            catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                return null;
            }
        }
    }
}
