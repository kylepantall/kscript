using System;
using System.Threading;
using KScript.Projects;

namespace KScript.Data
{
    /// <summary>
    /// A database column holds a date as a clock time with no time zone, so the zone has to be agreed outside it:
    /// it is the project's ("timezone" in kscript.json, Europe/London unless it says otherwise). A date is written
    /// as the clock time it is in that zone and read back from it, so the same row is the same moment whatever the
    /// zone of the computer that reads it.
    /// </summary>
    internal static class StoredDates
    {
        private static readonly AsyncLocal<TimeZoneInfo> zone = new AsyncLocal<TimeZoneInfo>();

        /// <summary>The zone of the database in use: set each time a script asks for its database.</summary>
        internal static TimeZoneInfo Zone
        {
            get => zone.Value ?? ProjectConfig.DefaultZone;
            set => zone.Value = value;
        }

        /// <summary>The zone of this computer, which a date in a script is in. Tests replace it.</summary>
        internal static Func<TimeZoneInfo> Machine = () => TimeZoneInfo.Local;

        /// <summary>A date as the clock time to store.</summary>
        internal static DateTime ToStored(DateTime value) => Move(value, value.Kind == DateTimeKind.Utc ? TimeZoneInfo.Utc : Machine(), Zone, DateTimeKind.Unspecified);

        /// <summary>A stored clock time as the date it is on this computer.</summary>
        internal static DateTime FromStored(DateTime stored) => Move(stored, Zone, Machine(), DateTimeKind.Local);

        private static DateTime Move(DateTime value, TimeZoneInfo from, TimeZoneInfo to, DateTimeKind kind)
        {
            DateTime clock = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
            if (from.Equals(to) || from.Id == to.Id) return DateTime.SpecifyKind(clock, kind);
            // An hour the clocks skip in spring doesn't exist: it is read as the hour after, as a clock would show it.
            if (from.IsInvalidTime(clock)) clock = clock.Add(Skipped(from, clock));
            return DateTime.SpecifyKind(TimeZoneInfo.ConvertTime(clock, from, to), kind);
        }

        private static TimeSpan Skipped(TimeZoneInfo zone, DateTime clock)
        {
            foreach (TimeZoneInfo.AdjustmentRule rule in zone.GetAdjustmentRules())
                if (clock >= rule.DateStart && clock <= rule.DateEnd.AddDays(1) && rule.DaylightDelta != TimeSpan.Zero) return rule.DaylightDelta.Duration();
            return TimeSpan.FromHours(1);
        }
    }
}
