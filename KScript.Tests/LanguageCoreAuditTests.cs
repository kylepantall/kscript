using KScript.Runtime;

namespace KScript.Tests;

/// <summary>Regressions from the language core audit: each test is one thing a script got wrong before.</summary>
public sealed class LanguageCoreAuditTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "kscript-core-audit-" + Guid.NewGuid().ToString("N"));

    public LanguageCoreAuditTests() => Directory.CreateDirectory(dir);

    public void Dispose() => Directory.Delete(dir, recursive: true);

    private string Run(string code) => ScriptRunner.Run(code, path: Path.Combine(dir, "main.kscript"));

    private KScriptException Fails(string code) => Assert.Throws<KScriptException>(() => Run(code));

    private string RunXml(string body) => ScriptRunner.Run("<kscript>" + body + "</kscript>", path: Path.Combine(dir, "main.ks.xml"));

    [Fact]
    public void A_type_named_like_a_built_in_error_makes_the_type()
    {
        string code = """
            type Config {
              host: string
              port: int = 80
            }
            type Name with [string] { min: 2 }
            c = Config({ host: 'x' })
            d = Config(host: 'y')
            echo "{ $c->type() } { $c.host } { $d.port } { Name('ab') }"
            """;
        Assert.Equal("Config x 80 ab\n", Run(code));
        Assert.Equal("Shape", Fails("type Name with [string] { min: 2 }\nn = Name('a')").Type);
    }

    [Fact]
    public void Quoted_echo_text_in_a_code_file_is_printed_as_written()
    {
        string code = """
            echo "  - item"
            pad = 'x'->pad_left(5)
            echo "{ $pad }|"
            echo "{ $pad }"
            lines = ['    if x:', '        return 1']->join("
            ")
            echo "{ $lines }"
            echo " "
            """;
        Assert.Equal("  - item\n    x|\n    x\n    if x:\n        return 1\n \n", Run(code));
    }

    [Fact]
    public void A_text_block_after_echo_keeps_the_indentation_inside_it()
    {
        string code = "echo ```\n  first\n    indented\n  last\n  ```\n";
        Assert.Equal("first\n  indented\nlast\n", Run(code));
    }

    [Fact]
    public void Xml_element_text_is_tidied_but_the_values_put_into_it_are_not()
    {
        string body = """
            <let name="pad" value="'x'->pad_left(5)" />
            <let name="code" value="['    if x:', '        return 1']->join(@char_of(10))" />
            <echo>
              Value: { $pad }|
            </echo>
            <echo>{ $pad }</echo>
            <echo>
              { $code }
            </echo>
            """;
        Assert.Equal("Value:     x|\n    x\n    if x:\n        return 1\n", RunXml(body));
    }

    [Fact]
    public void Dates_that_differ_by_seconds_are_not_equal()
    {
        string code = """
            a = '2026-03-29 10:00:45'->to_date()
            b = '2026-03-29 10:00:10'->to_date()
            echo "{ $a == $b } { $a != $b } { [$a]->contains($b) } { $a in [$b] } { $a == '2026-03-29 10:00:45'->to_date() }"
            """;
        Assert.Equal("false true false false true\n", Run(code));
    }

    [Fact]
    public void A_function_the_script_declares_wins_over_an_imported_one_and_imported_consts_stay_const()
    {
        File.WriteAllText(Path.Combine(dir, "lib.kscript"), "const greeting = 'hi'\nfn helper() => 'imported'\nfn other() => 'other'\n");
        Assert.Equal("local other hi\n", Run("import \"lib.kscript\"\nimport other from \"lib.kscript\"\nfn helper() => 'local'\necho \"{ @helper() } { @other() } { $greeting }\""));
        Assert.Equal("Const", Fails("import \"lib.kscript\"\ngreeting = 'changed'").Type);
    }

    [Fact]
    public void A_default_after_two_question_marks_does_not_hide_an_error_inside_a_function()
    {
        Assert.Equal("Name", Fails("fn f() {\n  return @nope()\n}\necho \"{ @f().y ?? 'hidden' }\"").Type);
        string code = """
            fn none() {
              return null
            }
            m = { a: null }
            echo "{ $never ?? 'a' } { $m.a.b ?? 'b' } { $m.zz.b.c ?? 'c' } { @none().name ?? 'd' } { @none().a.b ?? 'e' }"
            """;
        Assert.Equal("a b c d e\n", Run(code));
    }

    [Theory]
    [InlineData("0..1 step 0.1", 10)]
    [InlineData("0..0.3 step 0.1", 3)]
    [InlineData("0..10", 10)]
    [InlineData("5..0", 5)]
    [InlineData("0..10 step 3", 4)]
    [InlineData("1..1", 0)]
    public void A_range_never_includes_its_end(string range, int count)
    {
        Assert.Equal(count + "\n", Run($"echo \"{{ ({range})->count() }}\""));
    }

    [Fact]
    public void Arithmetic_written_tight_against_an_aliased_call_is_not_an_email()
    {
        Assert.Equal("3 8\n", Run("import KScript.Math as math\necho \"{ 1+@math.sqrt(4) } { 10-@math.sqrt(4) }\""));
        Assert.Equal("email\n", Run("echo \"{ joan+tag@example.com->type() }\""));
    }

    [Fact]
    public void A_match_used_as_a_value_has_it()
    {
        Assert.Equal("big small\n", Run("fn size(x) => match $x { _ when $it > 9 => 'big', _ => 'small' }\necho \"{ @size(12) } { @size(3) }\""));
    }

    [Fact]
    public void A_named_argument_given_twice_is_an_error()
    {
        KScriptException error = Fails("fn f(a: int) => $a\necho \"{ @f(a: 1, a: 2) }\"");
        Assert.Equal("Argument", error.Type);
        Assert.Contains("'a' is given twice", error.Message);
    }

    [Theory]
    [InlineData("[1, 2, 3]->take(3000000000)")]
    [InlineData("'abc'->truncate(3000000000)")]
    [InlineData("'a'->repeat(3000000000)")]
    public void A_count_too_large_for_a_method_is_an_error_not_a_wrong_answer(string expression)
    {
        Assert.Equal("Argument", Fails($"echo \"{{ {expression} }}\"").Type);
    }

    [Fact]
    public void Repeating_text_too_many_times_and_far_indexes_do_not_wrap_around()
    {
        Assert.Equal("Limit", Fails("echo \"{ 'ab' * 3000000000 }\"").Type);
        Assert.Equal("abab none\n", Run("l = [1, 2]\necho \"{ 'ab' * 2 } { $l[3000000000] ?? 'none' }\""));
    }

    [Theory]
    [InlineData("'NaN' as int")]
    [InlineData("'Infinity' as number")]
    [InlineData("'1.5' as date")]
    [InlineData("'1.5'->to_date()")]
    public void Words_and_plain_numbers_are_not_numbers_or_dates(string expression)
    {
        Assert.Contains(Fails($"echo \"{{ {expression} }}\"").Type, new[] { "Type", "Format" });
    }

    [Fact]
    public void Typed_data_for_an_int_must_be_a_whole_number()
    {
        string types = "type Repo {\n  stars: int\n}\ntype Age with [int] { min: 0 }\n";
        KScriptException field = Fails(types + "r = Repo({ stars: '12.9' })");
        Assert.Equal("Shape", field.Type);
        Assert.Contains("needs a whole number", field.Message);
        Assert.Equal("Shape", Fails(types + "a = Age(-0.5)").Type);
        Assert.Equal("12 7 3\n", Run(types + "r = Repo({ stars: '12' })\necho \"{ $r.stars } { Age(7.0) } { 3.9 as int }\""));
    }

    [Theory]
    [InlineData("fn f() {\n  continue\n}\nfor i in 0..2 {\n  @f()\n}", "continue", 2)]
    [InlineData("fn f() {\n  break\n}\nwhile true {\n  @f()\n}", "break", 2)]
    [InlineData("x = 1\nbreak", "break", 2)]
    public void A_break_or_continue_never_reaches_a_loop_outside_its_function(string code, string word, int line)
    {
        KScriptException error = Fails(code);
        Assert.Equal("Syntax", error.Type);
        Assert.Contains(word, error.Message);
        Assert.Equal(line, error.Line);
    }

    [Theory]
    [InlineData("@today()->add_months(1000000)")]
    [InlineData("@today()->add_days(100000000000)")]
    [InlineData("@today() + 100000000000")]
    [InlineData("@today() - 100000000000")]
    public void A_date_moved_out_of_range_says_so_in_plain_words(string expression)
    {
        KScriptException error = Fails($"echo \"{{ {expression} }}\"");
        Assert.Equal("Argument", error.Type);
        Assert.Contains("years 1 to 9999", error.Message);
        Assert.DoesNotContain("Parameter", error.Message);
    }

    [Fact]
    public void A_comparison_error_names_the_symbol_and_a_bool_is_not_compared_with_a_number()
    {
        Assert.Contains("using '<'", Fails("echo \"{ null < 1 }\"").Message);
        KScriptException chained = Fails("echo \"{ 1 < 2 < 3 }\"");
        Assert.Equal("Type", chained.Type);
        Assert.Contains("Can't compare a bool (true) with a number (3) using '<'", chained.Message);
    }

    [Fact]
    public void Only_scripts_can_be_imported()
    {
        File.WriteAllText(Path.Combine(dir, "notes.txt"), "hello");
        KScriptException error = Fails("import \"notes.txt\"\necho 'x'");
        Assert.Equal("Import", error.Type);
        Assert.Contains("isn't a script", error.Message);
    }

    [Fact]
    public void Two_functions_with_one_name_in_a_file_is_an_error()
    {
        KScriptException error = Fails("fn f() => 1\nfn f() => 2\necho \"{ @f() }\"");
        Assert.Equal("Syntax", error.Type);
        Assert.Contains("A function named 'f' is already declared", error.Message);
        Assert.Equal(2, error.Line);
    }
}
