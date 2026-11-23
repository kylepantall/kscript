using KScript.Runtime;

namespace KScript.Tests;

/// <summary>
/// slash_path (names joined by /, written bare as /docs/start, filled in with $name and { }) and date_format (how a
/// date is written, checked where it is made), and the bare route paths and dotted arguments that came with them.
/// </summary>
public class SlashPathAndDateFormatTests
{
    private static string Code(string code) => ScriptRunner.Run(code, path: Path.Combine(Path.GetTempPath(), "slash-path-test.kscript")).TrimEnd('\n');

    private static string Fails(string code) => Assert.Throws<KScriptException>(() => Code(code)).Message;

    [Theory]
    [InlineData("'/docs/start'", "/docs/start")]
    [InlineData("' / My  Apps // Notes / '", "/My Apps/Notes")]
    [InlineData("'/docs/./guides/../start'", "/docs/start")]
    [InlineData("'home'", "/home")]
    [InlineData("'home/'", "/home")]
    [InlineData("''", "/")]
    [InlineData("'/'", "/")]
    [InlineData("/blog/", "/blog")]
    [InlineData("/My Applications/Something", "/My Applications/Something")]
    [InlineData("/my-website", "/my-website")]
    [InlineData("/v1/users/12", "/v1/users/12")]
    [InlineData("/docs/mix", "/docs/mix")]
    public void PathsAreTidiedWhenTheyAreMade(string value, string expected) =>
        Assert.Equal(expected, Code($"p: slash_path = {value}\necho $p"));

    [Theory]
    [InlineData("'/a/../..'", "goes above /")]
    [InlineData("'C:\\Apps'", "not \\")]
    public void PathsThatCantBeMadeSayWhy(string value, string message) =>
        Assert.Contains(message, Fails($"p: slash_path = {value}"));

    [Theory]
    [InlineData("echo $p.segments", "My Apps, Notes, Today")]
    [InlineData("echo $p.name", "Today")]
    [InlineData("echo \"{ $p.parent } { $p.parent->type() } { $p.depth } { $p.is_root }\"", "/My Apps/Notes slash_path 3 false")]
    [InlineData("r: slash_path = /\necho \"{ $r.name }|{ $r.parent }|{ $r.depth }|{ $r.is_root }\"", "||0|true")]
    [InlineData("echo $p->join('../Tomorrow')", "/My Apps/Notes/Tomorrow")]
    [InlineData("echo \"{ $p->is_within('/My Apps') } { $p->is_within(/My) } { $p->is_within('/my apps') } { $p->is_within(/) }\"", "true false false true")]
    [InlineData("echo \"{ $p + 'Later' } { ($p + '..')->type() }\"", "/My Apps/Notes/Today/Later slash_path")]
    [InlineData("echo \"{ /a/b == '/a/b/' } { /a == /b }\"", "true false")]
    [InlineData("for part at i in /home/joan/downloads {\n  echo \"{$i} {$part} { $part->type() }\"\n}", "0 /home slash_path\n1 /joan slash_path\n2 /downloads slash_path")]
    public void PathsHaveFieldsAndMethods(string code, string expected) =>
        Assert.Equal(expected, Code("p: slash_path = '/My Apps/Notes/Today'\n" + code));

    [Theory]
    [InlineData("id = 42\necho /people/$id/edit", "/people/42/edit")]
    [InlineData("section = 'Guides'\necho /docs/{ $section->lower() }/start", "/docs/guides/start")]
    [InlineData("user = 'joan'\necho /home/{$user}/downloads", "/home/joan/downloads")]
    [InlineData("echo /people/{id}/{rest*}", "/people/{id}/{rest*}")]
    [InlineData("sub = 'a/b'\necho /docs/$sub", "/docs/a/b")]
    [InlineData("note = { id: 7 }\np: slash_path = /notes/$note.id\necho $p", "/notes/7")]
    [InlineData("note = { path: 'home' }\nwhere: slash_path = $note.path\necho $where", "/home")]
    [InlineData("id = 3\nfn go(to: slash_path) => \"go {$to}\"\necho @go(/x/$id)", "go /x/3")]
    public void BarePathsFillInValues(string code, string expected) => Assert.Equal(expected, Code(code));

    [Theory]
    [InlineData("echo \"{ /^ab$/i->type() } { /blog/->type() } { 10 / 2 }\"", "regex regex 5")]
    [InlineData("x = 6\ny = 3\necho $x/$y", "2")]
    [InlineData("echo /docs/start->type()", "slash_path")]
    [InlineData("fn open(at: slash_path) => \"{$at}\"\necho \"{ @open(/blog/) } { @open('/a b/c') } { @open(a/b) }\"", "/blog /a b/c /a/b")]
    public void BarePathsAndRegexesAndDivisionDontClash(string code, string expected) => Assert.Equal(expected, Code(code));

    [Theory]
    [InlineData("fn f(x: semver) => \"{$x}\"\necho @f(1.4.2-beta.1)", "1.4.2-beta.1")]
    [InlineData("fn f(x: ip) => \"{$x}\"\necho @f(10.0.0.1)", "10.0.0.1")]
    [InlineData("fn f(x: cidr) => \"{$x}\"\necho @f(10.0.0.0/8)", "10.0.0.0/8")]
    [InlineData("echo 1.5 + 1", "2.5")]
    public void DottedValuesWorkAsArgumentsOfTypesThatReadText(string code, string expected) => Assert.Equal(expected, Code(code));

    [Fact]
    public void DottedValuesAnywhereElseSayToUseQuotes() =>
        Assert.Contains("put it in quotes: '1.4.2'", Fails("x = 1.4.2"));

    [Theory]
    [InlineData("f: date_format = dd-MM-yyyy\necho $f->format(@day())", "22-11-2026")]
    [InlineData("echo @day()->format(dd-MM-yyyy)", "22-11-2026")]
    [InlineData("echo @day()->format(yyyy)", "2026")]
    [InlineData("echo @day()->format('d MMMM yyyy, HH:mm')", "22 November 2026, 16:30")]
    [InlineData("echo @day()->format('o')->starts_with('2026-11-22T16:30:00')", "true")]
    [InlineData("echo @day()->format()", "2026-11-22")]
    [InlineData("f: date_format = d MMMM yyyy\necho $f.example", "7 March 2026")]
    [InlineData("f: date_format = yyyy-MM-ddTHH:mm:ssZ\necho $f.example", "2026-03-07T14:05:09Z")]
    [InlineData("f: date_format = \"'at' HH:mm\"\necho \"{ $f.example } { $f.has_date } { $f.has_time }\"", "at 14:05 false true")]
    [InlineData("f: date_format = dd-MM-yyyy\necho $f.tokens", "dd, -, MM, -, yyyy")]
    [InlineData("echo '22-11-2026'->to_date(dd-MM-yyyy)->format(dddd)", "Sunday")]
    [InlineData("f: date_format = dd/MM/yyyy\necho $f->parse('07/03/2026')->format(yyyy-MM-dd)", "2026-03-07")]
    public void DateFormatsWriteAndReadDates(string code, string expected) =>
        Assert.Equal(expected, Code("fn day() => '2026-11-22T16:30:00'->to_date()\n" + code));

    [Theory]
    [InlineData("f: date_format = YYYY-MM-DD", "years are y")]
    [InlineData("f: date_format = 'dd-MM-yyyy hh:mm a'", "AM/PM is tt")]
    [InlineData("f: date_format = 'ddddd'", "use at most dddd")]
    [InlineData("f: date_format = 'at'", "isn't a date letter")]
    [InlineData("f: date_format = 'dd o'", "put text in quotes")]
    [InlineData("f: date_format = '--'", "no date or time letters")]
    [InlineData("echo '2026'->to_date(DD)", "days are d")]
    [InlineData("echo '2026-11-22'->to_date()->format('YYYY')", "years are y")]
    [InlineData("f: date_format = dd-MM-yyyy\necho $f->parse('2026-11-22')", "isn't a date written dd-MM-yyyy")]
    public void DateFormatsThatCantBeMadeSayWhy(string code, string message) => Assert.Contains(message, Fails(code));
}
