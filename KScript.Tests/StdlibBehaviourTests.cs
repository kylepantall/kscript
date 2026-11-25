using KScript.Runtime;

namespace KScript.Tests;

/// <summary>
/// The conventions every standard package follows, at run time: fields are read with a dot, renamed methods stop
/// with what to write instead, a length of time is a duration, and a table takes the same arguments in the terminal
/// and in a document.
/// </summary>
public class StdlibBehaviourTests
{
    private static string Code(string code) => ScriptRunner.Run(code, path: Path.Combine(Path.GetTempPath(), "behaviour-test.kscript")).TrimEnd('\n');

    private static string Fails(string code) => Assert.Throws<KScriptException>(() => Code(code)).Message;

    [Theory]
    [InlineData("d = '2026-11-19 14:30'->to_date()\necho \"{ $d.year } { $d.month } { $d.hour }\"", "2026 11 14")]
    [InlineData("u: url = 'https://example.com:8443/docs?a=1'\necho \"{ $u.host } { $u.port } { $u.path }\"", "example.com 8443 /docs")]
    [InlineData("a: ip = '10.0.0.1'\necho \"{ $a.is_private } { $a.is_loopback } { $a.version }\"", "true false 4")]
    [InlineData("d = 1h30m\necho \"{ $d.minutes } { $d.seconds }\"", "90 5400")]
    public void A_plain_fact_is_a_field(string code, string expected) => Assert.Equal(expected, Code(code));

    [Fact]
    public void Calling_a_field_says_to_drop_the_brackets()
    {
        string message = Fails("d = '2026-11-19'->to_date()\necho $d->year()");
        Assert.Contains("year is a field", message);
        Assert.Contains(".year", message);
    }

    [Fact]
    public void Reading_a_method_as_a_field_says_to_call_it()
    {
        string message = Fails("d = '2026-11-19'->to_date()\necho $d.format");
        Assert.Contains("format is a method", message);
        Assert.Contains("->format(", message);
    }

    [Theory]
    [InlineData("import KScript.Cache\necho $cache->put('a', 1)", "Use ->set(key, value).")]
    [InlineData("import KScript.Cache\necho $cache->forget('a')", "Use ->remove(key).")]
    [InlineData("import KScript.Cache\necho $cache->flush()", "Use ->clear().")]
    [InlineData("p: password = 'correct horse battery'\necho $p->strong()", "Use ->is_strong().")]
    [InlineData("a: ip = '10.0.0.1'\necho $a->private()", "Read the field .is_private")]
    [InlineData("a: ip = '10.0.0.1'\necho $a->loopback()", "Read the field .is_loopback")]
    [InlineData("z: timezone = 'Europe/London'\necho $z->dst()", "Use ->is_dst().")]
    public void A_renamed_method_stops_with_what_to_write_instead(string code, string expected) => Assert.Contains(expected, Fails(code));

    [Theory]
    [InlineData("import KScript.Auth\necho @signed_in()", "@is_signed_in()")]
    [InlineData("echo @config('a')", "$config")]
    public void A_renamed_command_stops_with_what_to_write_instead(string code, string expected) => Assert.Contains(expected, Fails(code));

    [Theory]
    [InlineData("wait(ms: 5)", "duration")]
    [InlineData("wait(seconds: 1)", "duration")]
    public void An_argument_named_for_a_unit_stops_with_what_to_write_instead(string code, string expected) => Assert.Contains(expected, Fails(code));

    [Theory]
    [InlineData("30s", "30s", 30)]
    [InlineData("500ms", "0.5s", 0.5)]
    [InlineData("1h30m", "1h30m", 5400)]
    [InlineData("90m", "1h30m", 5400)]
    [InlineData("1w", "1w", 604800)]
    public void A_duration_is_written_as_a_number_and_a_unit(string written, string shown, double seconds)
    {
        Assert.Equal($"duration {shown} {seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}", Code($"d = {written}\necho \"{{ $d->type() }} {{ $d }} {{ $d.seconds }}\""));
    }

    [Theory]
    [InlineData(30d, 30)]
    [InlineData("30s", 30)]
    [InlineData("2m", 120)]
    [InlineData("1h30m", 5400)]
    public void Every_length_of_time_reads_a_duration_text_or_a_plain_number_of_seconds(object given, double seconds)
    {
        Assert.Equal(seconds, Duration.Seconds(given, "timeout"));
        Assert.Equal(seconds, Duration.Seconds(Duration.Of(seconds), "timeout"));
    }

    [Fact]
    public void A_length_of_time_that_is_not_one_says_how_to_write_it()
    {
        Assert.Contains("should be a length of time such as 30s, 5m or 1h30m (a plain number is seconds)", Fails("wait(duration: 'soon')"));
        Assert.Contains("should be a length of time", Fails("import KScript.Cache\n$cache->set('a', 1, ttl: 'soon')"));
    }

    [Fact]
    public void Wait_takes_a_duration()
    {
        Assert.Equal("done", Code("wait(duration: 1ms)\nwait(duration: 0.001)\necho 'done'"));
    }

    [Fact]
    public void A_cache_keeps_values_for_a_duration_and_says_how_long_is_left()
    {
        Assert.Equal("1 true duration", Code("import KScript.Cache\necho \"{ $cache->set('behaviour-a', 1, ttl: 5m) } { $cache->has('behaviour-a') } { $cache->ttl('behaviour-a')->type() }\""));
        Assert.Equal("true false 0", Code("import KScript.Cache\n$cache->set('behaviour-b', 1)\necho \"{ $cache->remove('behaviour-b') } { $cache->has('behaviour-b') } { [$cache->clear(), $cache->clear()][1] }\""));
    }

    [Fact]
    public void The_terminal_table_takes_rows_columns_align_and_header()
    {
        const string rows = "[{ name: 'Joan', age: 82 }, { name: 'Sam', age: 9 }]";
        string headed = Code($"import KScript.Terminal\ntable(rows: {rows}, columns: {{ Name: 'name', Age: 'age' }}, align: ['right', 'left'])");
        Assert.Contains("│ Name │ Age │", headed);
        Assert.Contains("│ Joan │ 82  │", headed);
        Assert.Contains("│  Sam │ 9   │", headed);

        string keys = Code($"import KScript.Terminal\ntable(rows: {rows}, columns: ['age'])");
        Assert.Contains("│ age │", keys);
        Assert.DoesNotContain("Joan", keys);

        string bare = Code($"import KScript.Terminal\ntable(rows: {rows}, header: false)");
        Assert.DoesNotContain("name", bare);
        Assert.Contains("│ Joan │ 82 │", bare);

        string lists = Code("import KScript.Terminal\ntable(rows: [['Name', 'Age'], ['Joan', 82]])");
        Assert.Contains("│ Name │ Age │", lists);
        Assert.Contains("│ Joan │  82 │", lists);
    }

    [Theory]
    [InlineData("table(rows: [{ a: 1 }], data: [{ a: 1 }])", "rows")]
    [InlineData("table(rows: [{ a: 1 }], headers: ['A'])", "columns")]
    [InlineData("table(rows: [{ a: 1 }], align: 'lr')", "left, center or right")]
    [InlineData("table(rows: [{ a: 1 }], align: ['middle'])", "isn't a column alignment")]
    public void The_terminal_table_says_how_to_write_what_it_no_longer_reads(string statement, string expected) =>
        Assert.Contains(expected, Fails("import KScript.Terminal\n" + statement));

    [Theory]
    [InlineData("1234567890123457", "1234567890123457")]
    [InlineData("9007199254740991", "9007199254740991")]
    [InlineData("-1234567890123457", "-1234567890123457")]
    [InlineData("10 ** 15", "1000000000000000")]
    [InlineData("10 ** 20", "100000000000000000000")]
    [InlineData("2 ** 60", "1152921504606847000")]
    [InlineData("0.1 ** 20", "0.00000000000000000001")]
    [InlineData("-0.000001234", "-0.000001234")]
    [InlineData("1.5 * 10 ** -7", "0.00000015")]
    [InlineData("0.1 + 0.2", "0.3")]
    [InlineData("1 / 3", "0.333333333333333")]
    [InlineData("1234.5", "1234.5")]
    [InlineData("42", "42")]
    [InlineData("-0.5", "-0.5")]
    public void A_number_is_written_in_plain_digits_whatever_its_size(string expression, string expected)
    {
        Assert.Equal(expected, Code($"echo {expression}"));
        Assert.Equal($"n={expected}", Code($"echo \"n={{ {expression} }}\""));
        Assert.Equal(expected, Code($"n = {expression}\necho $n->to_string()"));
        // JSON may use an exponent, but it holds the same number: reading it back prints the same digits.
        Assert.Equal(expected, Code($"n = {expression}\necho [$n]->to_json()->json()[0]"));
        Assert.Equal("true", Code($"n = {expression}\necho \"{{ [$n]->to_json()->json()[0] == $n }}\""));
    }

    [Fact]
    public void A_whole_number_keeps_every_digit_in_json()
    {
        Assert.Equal("{\"id\":1234567890123457} 1234567890123457", Code("echo \"{ { id: 1234567890123457 }->to_json() } { '{\\\"id\\\": 1234567890123457}'->json().id }\""));
    }
}
