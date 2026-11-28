using KScript.Data;
using KScript.Projects;
using KScript.Runtime;

namespace KScript.Tests;

/// <summary>Dates are stored as the clock time in the project's time zone ("timezone" in kscript.json, Europe/London by default).</summary>
public sealed class DataTimeZoneTests : DataFixture
{
    private const string Notes = """
        model Note extends Timestamps {
          text: string
        }
        $db->create_table(Note)
        Note->create(text: 'a')
        """;

    private static TimeZoneInfo Zone(string name) => ProjectConfig.FindZone(name);

    private static void At(string machine, DateTime clock)
    {
        StoredDates.Machine = () => Zone(machine);
        Rows.Clock = () => clock;
    }

    [Fact]
    public void TheDefaultTimeZoneIsEuropeLondon()
    {
        FileOf("kscript.json", "{ }");
        ProjectConfig project = ProjectConfig.Load(Path.Combine(Project, "kscript.json"));
        Assert.Equal("Europe/London", project.TimeZone);
        Assert.Equal(TimeSpan.FromHours(1), project.Zone.GetUtcOffset(new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(TimeSpan.Zero, project.Zone.GetUtcOffset(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void KscriptJsonSetsTheTimeZoneAndAnUnknownNameIsAnError()
    {
        FileOf("kscript.json", "{ \"timezone\": \"America/New_York\" }");
        ProjectConfig project = ProjectConfig.Load(Path.Combine(Project, "kscript.json"));
        Assert.Equal("America/New_York", project.TimeZone);
        Assert.Equal(TimeSpan.FromHours(-4), project.Zone.GetUtcOffset(new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc)));
        Assert.Empty(project.Errors);

        FileOf("kscript.json", "{ \"timezone\": \"Mars/Olympus\" }");
        ProjectConfig wrong = ProjectConfig.Load(Path.Combine(Project, "kscript.json"));
        Assert.Contains("timezone: there is no time zone called 'Mars/Olympus' on this computer. Use a name such as Europe/London, America/New_York or UTC.", wrong.Errors);
        Assert.Equal("Europe/London", wrong.TimeZone);
    }

    [Fact]
    public void ADateIsStoredAsTheClockTimeInTheProjectsZone()
    {
        StoredDates.Zone = Zone("Europe/London");
        StoredDates.Machine = () => Zone("America/New_York");
        // 8 in the morning in New York in July is 1 in the afternoon in London.
        DateTime stored = StoredDates.ToStored(new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Local));
        Assert.Equal(new DateTime(2026, 7, 1, 13, 0, 0), stored);
        Assert.Equal(DateTimeKind.Unspecified, stored.Kind);
        Assert.Equal(new DateTime(2026, 7, 1, 13, 0, 0), StoredDates.ToStored(new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc)));

        // The same row read on a computer in Tokyo is the same moment.
        StoredDates.Machine = () => Zone("Asia/Tokyo");
        DateTime read = StoredDates.FromStored(stored);
        Assert.Equal(new DateTime(2026, 7, 1, 21, 0, 0), read);
        Assert.Equal(DateTimeKind.Local, read.Kind);
    }

    [Fact]
    public void AnHourTheClocksSkipIsReadAsTheHourAfter()
    {
        StoredDates.Zone = Zone("UTC");
        StoredDates.Machine = () => Zone("Europe/London");
        // 01:30 on 29 March 2026 doesn't exist in London: the clocks go from 01:00 to 02:00.
        Assert.Equal(new DateTime(2026, 3, 29, 1, 30, 0), StoredDates.ToStored(new DateTime(2026, 3, 29, 1, 30, 0)));
        Assert.Equal(new DateTime(2026, 3, 29, 1, 30, 0), StoredDates.ToStored(new DateTime(2026, 3, 29, 2, 30, 0)));
    }

    [Fact]
    public void ARowWrittenInOneZoneIsTheSameMomentReadInAnother()
    {
        At("America/New_York", new DateTime(2026, 7, 1, 8, 0, 0));
        Run(Notes);

        StoredDates.Machine = () => Zone("Asia/Tokyo");
        Database.CloseAll();
        FileOf("read.kscript", """
            import KScript.Data
            model Note extends Timestamps {
              text: string
            }
            echo Note->find(1).created_at->format('yyyy-MM-dd HH:mm')
            echo Note->where(fn(n) => $n.created_at >= '2026-07-01 21:00'->to_date())->count()
            echo Note->where(fn(n) => $n.created_at > '2026-07-01 21:00'->to_date())->count()

            """);
        var output = new StringWriter { NewLine = "\n" };
        new Interpreter(KScriptHost.ForText(output)).RunFile(Path.Combine(Project, "read.kscript"));
        Assert.Equal("2026-07-01 21:00\n1\n0\n", output.ToString().Replace("\r\n", "\n"));
    }

    [Fact]
    public void TheStoredTextFollowsTheTimeZoneInKscriptJson()
    {
        if (Server != null) return;   // The text of a stored date is only there to read in SQLite.
        At("UTC", new DateTime(2026, 7, 1, 12, 0, 0));
        Assert.StartsWith("2026-07-01 13:00:00", Run(Notes + "\necho @sql('select created_at from notes')[0].created_at").Trim());

        FileOf("kscript.json", "{ \"timezone\": \"Asia/Tokyo\" }");
        Assert.StartsWith("2026-07-01 21:00:00", Run(Notes + "\necho @sql('select created_at from notes')[0].created_at").Trim());

        FileOf("kscript.json", "{ \"timezone\": \"UTC\" }");
        Assert.StartsWith("2026-07-01 12:00:00", Run(Notes + "\necho @sql('select created_at from notes')[0].created_at").Trim());
    }
}
