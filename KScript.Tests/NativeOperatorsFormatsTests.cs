using KScript.Runtime;

namespace KScript.Tests;

/// <summary>
/// What slug, semver, mime, hex, base64, jwt and json declare on the NativeType contract: semver's own order and ==,
/// alphabetical order for slug, mime and hex, == by content for base64 and json, and the errors for what they don't take.
/// </summary>
public class NativeOperatorsFormatsTests
{
    private static string Code(string code) => ScriptRunner.Run(code, path: Path.Combine(Path.GetTempPath(), "native-operators-formats.kscript")).TrimEnd('\n');

    private static string Fails(string code) => Assert.Throws<KScriptException>(() => Code(code)).Message;

    private const string Versions = "versions: semver[] = ['1.0.0', '1.0.1-rc', '1.0.2-beta', '1.0.10']\n";

    // ---- semver: precedence, not text ------------------------------------------------------------------------

    [Theory]
    [InlineData("1.4.2", "<", "1.10.0", "true")]
    [InlineData("1.10.0", ">", "1.4.2", "true")]
    [InlineData("1.0.0-beta", "<", "1.0.0", "true")]
    [InlineData("1.0.0-beta.2", "<", "1.0.0-beta.10", "true")]
    [InlineData("1.0.0-alpha", "<", "1.0.0-alpha.1", "true")]
    [InlineData("1.0.0-alpha.beta", "<", "1.0.0-beta", "true")]
    [InlineData("1.0.0-rc.1", "<", "1.0.0", "true")]
    [InlineData("2.0.0", "<=", "2.0.0+build.9", "true")]
    [InlineData("2.0.0", "<", "2.0.0+build.9", "false")]
    [InlineData("1.9.9", ">=", "1.10.0", "false")]
    public void Semver_compares_by_precedence(string a, string op, string b, string expected)
    {
        Assert.Equal(expected, Code($"a: semver = '{a}'\nb: semver = '{b}'\necho $a {op} $b"));
    }

    [Fact]
    public void Semver_compares_with_text_that_is_a_version()
    {
        Assert.Equal("true true", Code("v: semver = '1.4.2'\necho \"{ $v < '1.10.0' } { '1.10.0' > $v }\""));
    }

    [Fact]
    public void Semver_equality_ignores_build_metadata()
    {
        // SemVer 2.0.0 section 10: versions that differ only in build metadata have the same precedence.
        Assert.Equal("true true false true", Code("a: semver = '1.4.2+build.5'\nb: semver = '1.4.2'\n" +
            "echo \"{ $a == $b } { $a == '1.4.2' } { $a != $b } { $b != '1.4.2-rc.1' }\""));
        // The text it prints keeps the build metadata.
        Assert.Equal("1.4.2+build.5", Code("a: semver = '1.4.2+build.5'\necho $a"));
    }

    [Fact]
    public void Semver_lists_sort_by_precedence()
    {
        Assert.Equal("1.0.0, 1.0.1-rc, 1.0.2-beta, 1.0.10", Code(Versions + "echo $versions->sort()"));
        Assert.Equal("1.0.0-beta, 1.0.0, 1.4.2, 1.10.0, 2.0.0", Code("vs: semver[] = ['2.0.0', '1.10.0', '1.0.0', '1.4.2', '1.0.0-beta']\necho $vs->sort()"));
    }

    [Fact]
    public void Semver_lists_have_a_max_and_min_by_precedence()
    {
        Assert.Equal("1.0.10", Code(Versions + "echo $versions->max()"));
        Assert.Equal("1.0.0", Code(Versions + "echo $versions->min()"));
        Assert.Equal("1.0.10", Code(Versions + "echo $versions->filter(fn(v) => $v.prerelease == '')->max()"));
        Assert.Equal("1.0.0-rc.1", Code("vs: semver[] = ['1.0.0', '1.0.0-rc.1']\necho $vs->min()"));
    }

    [Fact]
    public void Semver_keeps_its_methods()
    {
        Assert.Equal("-1 true 1.5.0", Code("v: semver = '1.4.2'\necho \"{ $v->compare('1.10.0') } { $v->satisfies('^1.2.0') } { $v->bump('minor') }\""));
    }

    [Fact]
    public void Semver_takes_no_arithmetic()
    {
        Assert.Contains("Semver values have no '+'", Fails("v: semver = '1.4.2'\necho $v + 1"));
        Assert.Contains("Semver values have no '-'", Fails("a: semver = '1.4.2'\nb: semver = '1.0.0'\necho $a - $b"));
    }

    [Fact]
    public void Semver_compared_with_text_that_isnt_a_version_says_so()
    {
        Assert.Contains("both sides need to be a semver", Fails("v: semver = '1.4.2'\necho $v < 'soon'"));
    }

    [Fact]
    public void Semver_can_not_be_walked()
    {
        Assert.Contains("can't be walked", Fails("v: semver = '1.4.2'\nfor x in $v {\n  echo $x\n}"));
    }

    // ---- slug, mime, hex: alphabetical -----------------------------------------------------------------------

    [Fact]
    public void Slugs_sort_and_compare_alphabetically()
    {
        Assert.Equal("true false", Code("a: slug = 'apple-pie'\nb: slug = 'banana-bread'\necho \"{ $a < $b } { $a > $b }\""));
        Assert.Equal("apple-pie, banana-bread, cherry", Code("s: slug[] = ['cherry', 'apple-pie', 'banana-bread']\necho $s->sort()"));
        Assert.Equal("true", Code("a: slug = 'my-post'\necho $a == 'my-post'"));
    }

    [Fact]
    public void Slugs_join_text_with_plus_but_take_no_other_operators()
    {
        Assert.Equal("my-post-2", Code("a: slug = 'my-post'\necho $a + '-2'"));
        Assert.Contains("Slug values have no '+'", Fails("a: slug = 'my-post'\nb: slug = 'two'\necho $a + $b"));
        Assert.Contains("Slug values have no '*'", Fails("a: slug = 'my-post'\necho $a * 2"));
    }

    [Fact]
    public void Mime_types_sort_alphabetically()
    {
        Assert.Equal("application/json, image/png, text/html", Code("m: mime[] = ['text/html', 'image/png', 'application/json']\necho $m->sort()"));
        Assert.Equal("true", Code("a: mime = 'Text/HTML'\nb: mime = 'text/plain'\necho $a < $b"));
        Assert.Equal("true", Code("a: mime = 'Text/HTML'\necho $a == 'text/html'"));
        Assert.Contains("Mime values have no '+'", Fails("a: mime = 'text/html'\necho $a + 1"));
    }

    [Fact]
    public void Hex_values_sort_by_text()
    {
        Assert.Equal("00ff, a3f9, ff00", Code("h: hex[] = ['FF00', '0x00ff', 'a3f9']\necho $h->sort()"));
        Assert.Equal("true true", Code("a: hex = '0xA3'\necho \"{ $a == 'a3' } { $a < 'b0' }\""));
        Assert.Contains("Hex values have no '+'", Fails("a: hex = 'ff'\necho $a + 1"));
    }

    // ---- base64, jwt, json: no order -------------------------------------------------------------------------

    [Fact]
    public void Base64_is_equal_when_it_holds_the_same_bytes()
    {
        // Padding and the URL-safe letters are spelling: these all hold the same bytes.
        Assert.Equal("true true false", Code("a: base64 = 'aGk='\nb: base64 = 'aGk'\nc: base64 = 'Pz8-'\nd: base64 = 'Pz8+'\n" +
            "echo \"{ $a == $b } { $c == $d } { $a == $c }\""));
        Assert.Equal("aGk", Code("b: base64 = 'aGk'\necho $b"));
    }

    [Fact]
    public void Base64_has_no_order_or_operators()
    {
        Assert.Contains("Base64 values have no order", Fails("a: base64 = 'aGk='\nb: base64 = 'aGVsbG8='\necho $a < $b"));
        Assert.Contains("Base64 values have no '+'", Fails("a: base64 = 'aGk='\nb: base64 = 'aGVsbG8='\necho $a + $b"));
    }

    [Fact]
    public void Jwt_compares_by_token_and_has_no_order()
    {
        const string Token = "eyJhbGciOiJub25lIn0.eyJzdWIiOiIxIn0.";
        Assert.Equal("true", Code($"a: jwt = '{Token}'\nb: jwt = 'Bearer {Token}'\necho $a == $b"));
        Assert.Contains("Jwt values have no order", Fails($"a: jwt = '{Token}'\nb: jwt = '{Token}'\necho $a < $b"));
        Assert.Contains("Jwt values have no '+'", Fails($"a: jwt = '{Token}'\necho $a + 1"));
    }

    [Fact]
    public void Json_is_equal_when_it_holds_the_same_data()
    {
        Assert.Equal("true false true", Code("a: json = '{\"a\": 1, \"b\": [1, 2]}'\nb: json = '{\"b\":[1,2],\"a\":1}'\nc: json = '[2, 1]'\n" +
            "echo \"{ $a == $b } { $c == '[1, 2]' } { $c == '[2,1]' }\""));
    }

    [Fact]
    public void Json_has_no_order_or_operators()
    {
        Assert.Contains("Json values have no order", Fails("a: json = '1'\nb: json = '2'\necho $a < $b"));
        Assert.Contains("Json values have no '+'", Fails("a: json = '1'\nb: json = '2'\necho $a + $b"));
        // A value of another type is never read as a duration: hex 10 + hex 20 is not 30s.
        Assert.Contains("Hex values have no '+'", Fails("a: hex = '10'\nb: hex = '20'\necho $a + $b"));
        Assert.Contains("Json values have no '+'", Fails("a: json = '1'\nb: json = '2'\necho $a + $b"));
    }

    // ---- what the editor reads -------------------------------------------------------------------------------

    [Theory]
    [InlineData("slug", "text", "text")]
    [InlineData("semver", "value", "value")]
    [InlineData("mime", "text", "text")]
    [InlineData("hex", "text", "text")]
    [InlineData("base64", "value", "none")]
    [InlineData("jwt", "text", "none")]
    [InlineData("json", "value", "none")]
    public void The_types_declare_their_equality_and_order(string type, string equality, string ordering)
    {
        NativeType t = NativeTypeRegistry.Get(type);
        Assert.NotNull(t);
        Assert.Equal(equality, t.Info.Equality);
        Assert.Equal(ordering, t.Info.Ordering);
        Assert.Empty(t.Info.Operators);
        Assert.Null(t.Info.Iterates);
        Assert.Null(t.Info.Falsy);
    }
}
