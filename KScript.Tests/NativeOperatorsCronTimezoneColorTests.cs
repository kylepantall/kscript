using KScript.Runtime;

namespace KScript.Tests;

/// <summary>
/// What cron, timezone and color declare on the NativeType contract: their equality and ordering, the operators
/// they leave out (and the errors that say so), plus how dates and durations work together.
/// </summary>
public class NativeOperatorsCronTimezoneColorTests
{
    private static string Code(string code) => ScriptRunner.Run(code, path: Path.Combine(Path.GetTempPath(), "natives-ops-test.kscript")).TrimEnd('\n');

    private static string Fails(string code) => Assert.Throws<KScriptException>(() => Code(code)).Message;

    // ---- cron ---------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("a: cron = '@daily'\nb: cron = '0 0 * * *'\necho $a == $b", "true")]
    [InlineData("a: cron = '0 9 * * mon-fri'\nb: cron = '0 9 * * 1-5'\necho $a == $b", "true")]
    [InlineData("a: cron = '0 0 * * 0'\nb: cron = '0 0 * * 7'\necho $a == $b", "true")]
    [InlineData("a: cron = '@weekly'\nb: cron = '@daily'\necho $a == $b", "false")]
    [InlineData("a: cron = '@hourly'\necho $a == '0 * * * *'", "true")]
    [InlineData("a: cron = '@hourly'\necho $a != '0 9 * * *'", "true")]
    public void CronSchedulesAreEqualWhenTheyRunAtTheSameTimes(string code, string expected) => Assert.Equal(expected, Code(code));

    [Fact]
    public void CronSchedulesHaveNoOrder()
    {
        Assert.Contains("Cron values have no order", Fails("a: cron = '@daily'\nb: cron = '@hourly'\necho $a < $b"));
    }

    [Fact]
    public void CronSchedulesTakeNoOperators()
    {
        Assert.Contains("Cron values have no '-'", Fails("a: cron = '@daily'\necho $a - 1"));
        Assert.Contains("Cron values have no '*'", Fails("a: cron = '@daily'\necho $a * 2"));
    }

    [Fact]
    public void CronSchedulesCantBeWalked()
    {
        Assert.Contains("cron values can't be walked", Fails("a: cron = '@daily'\nfor t in $a { echo $t }"));
        Assert.Equal("Mon, Tue", Code("a: cron = '0 9 * * mon-fri'\necho $a->next_times(2, '2026-11-21T10:00:00'->to_date())->map(fn(t) => $t->format('ddd'))"));
    }

    [Fact]
    public void ACronScheduleIsAlwaysTrue()
    {
        Assert.Equal("yes", Code("a: cron = '@daily'\necho $a ? 'yes' : 'no'"));
    }

    // ---- timezone -----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("a: timezone = 'europe/london'\nb: timezone = Europe/London\necho $a == $b", "true")]
    [InlineData("a: timezone = Europe/London\necho $a == 'Europe/Paris'", "false")]
    [InlineData("a: timezone = Europe/London\nb: timezone = America/New_York\necho $b < $a", "true")]
    [InlineData("a: timezone = Europe/London\necho $a >= 'Asia/Tokyo'", "true")]
    [InlineData("z: timezone[] = ['Europe/Paris', 'UTC', 'Asia/Tokyo', 'America/New_York']\necho $z->sort()", "America/New_York, Asia/Tokyo, Europe/Paris, UTC")]
    public void TimezonesCompareAndSortByName(string code, string expected) => Assert.Equal(expected, Code(code));

    [Fact]
    public void TimezonesTakeNoOperators()
    {
        Assert.Contains("Timezone values have no '-'", Fails("a: timezone = UTC\nb: timezone = Europe/London\necho $a - $b"));
        Assert.Contains("Timezone values have no '*'", Fails("a: timezone = UTC\necho $a * 2"));
        Assert.Contains("timezone values can't be walked", Fails("a: timezone = UTC\nfor t in $a { echo $t }"));
    }

    [Fact]
    public void TimezoneTextStillJoinsWithPlus()
    {
        Assert.Equal("Zone: UTC", Code("a: timezone = UTC\necho 'Zone: ' + $a"));
    }

    // ---- color --------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("a: color = orange\necho $a == '#ffa500'", "true")]
    [InlineData("a: color = '#F80'\nb: color = 'rgb(255, 136, 0)'\necho $a == $b", "true")]
    [InlineData("a: color = red\necho $a == 'blue'", "false")]
    public void ColorsAreEqualWhenTheyAreTheSameColour(string code, string expected) => Assert.Equal(expected, Code(code));

    [Fact]
    public void ColorsHaveNoOrderSoSortByAField()
    {
        Assert.Contains("Color values have no order", Fails("a: color = red\nb: color = blue\necho $a < $b"));
        Assert.Equal("#000080, #ffa500, #ffffff",
            Code("c: color[] = ['white', 'navy', 'orange']\necho $c->sort_by(fn(x) => $x.luminance)"));
    }

    [Fact]
    public void ColorsTakeNoOperatorsButHaveMethods()
    {
        Assert.Contains("Color values have no '-'", Fails("a: color = red\nb: color = blue\necho $a - $b"));
        Assert.Contains("Color values have no '*'", Fails("a: color = red\necho $a * 2"));
        Assert.Contains("Color values have no '/'", Fails("a: color = red\necho $a / 2"));
        Assert.Equal("#800080", Code("a: color = red\necho $a->mix(blue)"));
    }

    [Fact]
    public void BlackIsStillTrue()
    {
        Assert.Equal("yes", Code("a: color = black\necho $a ? 'yes' : 'no'"));
    }

    [Fact]
    public void AColourArgumentThatDoesntFitSaysWhy()
    {
        Assert.Contains("'#ggg' isn't a color: a hex colour has 3, 4, 6 or 8 hex digits", Fails("a: color = red\necho $a->mix('#ggg')"));
    }

    // ---- dates and durations ------------------------------------------------------------------------------------

    [Theory]
    [InlineData("d = '2026-11-21T09:00:00'->to_date()\necho ($d + 1h30m)->format('yyyy-MM-dd HH:mm')", "2026-11-21 10:30")]
    [InlineData("d = '2026-11-21T09:00:00'->to_date()\necho ($d - 1d)->format('yyyy-MM-dd HH:mm')", "2026-11-20 09:00")]
    [InlineData("d = '2026-11-21T09:00:00'->to_date()\necho (2h + $d)->format('yyyy-MM-dd HH:mm')", "2026-11-21 11:00")]
    [InlineData("d = '2026-11-21T09:00:00'->to_date()\nx: duration = '45m'\necho ($d + $x)->format('HH:mm')", "09:45")]
    [InlineData("a = '2026-11-21'->to_date()\nb = '2026-11-24'->to_date()\necho $b - $a", "3")]
    [InlineData("a = '2026-11-21T00:00:00'->to_date()\nb = '2026-11-21T12:00:00'->to_date()\necho $b - $a", "0.5")]
    public void DatesMoveByDurationsAndDatesSubtractToDays(string code, string expected) => Assert.Equal(expected, Code(code));

    [Theory]
    [InlineData("a = '2026-11-21T09:00:00'->to_date()\nb = '2026-11-21T12:00:00'->to_date()\necho ($a..$b step 1h)->map(fn(t) => $t->format('HH:mm'))", "09:00, 10:00, 11:00")]
    [InlineData("a = '2026-11-23'->to_date()\nb = '2026-11-26'->to_date()\necho ($a..$b)->map(fn(t) => $t->format('ddd'))", "Mon, Tue, Wed")]
    [InlineData("a = '2026-11-21T12:00:00'->to_date()\nb = '2026-11-21T09:00:00'->to_date()\necho ($a..$b step -90m)->map(fn(t) => $t->format('HH:mm'))", "12:00, 10:30")]
    public void DateRangesStepByADuration(string code, string expected) => Assert.Equal(expected, Code(code));

    [Fact]
    public void ADateRangeCantStepByNothing()
    {
        Assert.Contains("step of 0s", Fails("a = '2026-11-21'->to_date()\nb = '2026-11-22'->to_date()\necho $a..$b step 0s"));
    }

    // ---- declarations -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("cron", "value", "none")]
    [InlineData("timezone", "text", "text")]
    [InlineData("color", "text", "none")]
    public void TheTypesDeclareWhatTheyDo(string name, string equality, string ordering)
    {
        NativeType type = NativeTypeRegistry.Get(name);
        Assert.NotNull(type);
        Assert.Equal(equality, type.Equality.Kind);
        Assert.Equal(ordering, type.Ordering.Kind);
        Assert.Null(type.Iteration.ItemType);
        Assert.Empty(type.Operators.Infos);
        Assert.Null(type.Operators.FalsySummary);
    }
}
