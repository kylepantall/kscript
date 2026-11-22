using KScript.Runtime;

namespace KScript.Tests;

/// <summary>
/// Bare words read as a declared type whose pattern fits them, with no type written: <c>key = passkey_login</c>.
/// See <see cref="BareWordTypes"/>.
/// </summary>
public class BareWordTypeTests
{
    private const string Passkey = "type PasskeySessionKey with [string] {\n  min: 12\n  normalize: $value->trim()\n  pattern: /^passkey_[a-z]+$/\n}\n";
    private const string Slug = "type Slug with [string] { pattern: /^[a-z0-9]+(-[a-z0-9]+)+$/ }\n";

    private static string Run(string code)
    {
        try
        {
            return ScriptRunner.Run(code, path: Path.Combine(Path.GetTempPath(), "bare-words.kscript")).TrimEnd('\n');
        }
        catch (KScriptException e)
        {
            return e.Message;
        }
    }

    [Theory]
    [InlineData(Passkey + "key = passkey_login\necho $key", "passkey_login")]
    [InlineData(Passkey + "key = passkey_x", "passkey_x doesn't fit PasskeySessionKey: must be at least 12 characters")]
    [InlineData(Passkey + "key: PasskeySessionKey = passkey_login\necho $key", "passkey_login")]
    [InlineData(Passkey + "key = 'passkey_login' as PasskeySessionKey\necho $key", "passkey_login")]
    [InlineData(Slug + "post = my-first-post\necho $post", "my-first-post")]
    [InlineData(Slug + "echo [my-post, other-post]->count()", "2")]
    [InlineData(Slug + "echo my-post == my-post", "true")]
    [InlineData(Slug + "fn show(s: string) => \"<$s>\"\necho @show(my-post)", "<my-post>")]
    [InlineData(Slug + "fn show(s) => \"<$s>\"\necho @show(s: my-post)", "<my-post>")]
    [InlineData(Slug + "fn list(items: string[]) => $items->join(' ')\necho @list([a-b, c-d])", "a-b c-d")]
    [InlineData("type Ver with [string] { pattern: /^\\d+\\.\\d+\\.\\d+$/ }\nv = 1.4.2\necho $v", "1.4.2")]
    [InlineData("type Host with [domain] { pattern: /^[a-z]+\\.corp\\.com$/ }\nh = api.corp.com\necho $h->type()", "domain")]
    public void WordsAreReadAsTheTypeTheirPatternFits(string code, string expected) => Assert.Contains(expected, Run(code));

    [Theory]
    // No type fits: the word is unknown, as before, and no part of it is read as a type on its own.
    [InlineData(Passkey + "key = login", "Unknown name 'login'")]
    [InlineData(Slug + "h = api.corp.com", "Unknown name 'api'")]
    [InlineData(Slug + "x = ab", "Unknown name 'ab'")]
    // The pattern must match all of the word, whatever its anchors.
    [InlineData("type K with [string] { pattern: /key_[a-z]+/ }\nk = my_key_abc", "Unknown name 'my_key_abc'")]
    // Arithmetic on numbers is never a word.
    [InlineData("type Sum with [string] { pattern: /^[0-9+]+$/ }\necho 1+2", "3")]
    // A name that means something keeps its meaning.
    [InlineData(Slug + "fn f(verbose = false) => $verbose\necho @f(verbose)", "true")]
    [InlineData("type K with [string] { pattern: /^[a-z]+$/ }\necho true", "true")]
    // Built-in types aren't guessed.
    [InlineData("c = GB", "Unknown name 'GB'")]
    // Types on other roots don't read words.
    [InlineData("type N with [int] { pattern: /^[a-z]+$/ }\nn = abc", "Unknown name 'abc'")]
    public void OtherWordsKeepTheirMeaning(string code, string expected) => Assert.Contains(expected, Run(code));

    [Fact]
    public void AWordTwoTypesFitIsAnError()
    {
        string message = Run("type A with [string] { pattern: /^x[a-z]+$/ }\ntype B with [string] { pattern: /^[a-z]+$/ }\nk = xyz");
        Assert.Contains("xyz fits more than one type (A and B)", message);
        Assert.Contains("key: A = xyz", message);
        Assert.Equal("xyz", Run("type A with [string] { pattern: /^x[a-z]+$/ }\ntype B with [string] { pattern: /^[a-z]+$/ }\nk: A = xyz\necho $k"));
    }

    [Fact]
    public void XmlScriptsReadWordsToo()
    {
        string xml = "<kscript>\n<type name=\"Slug\" with=\"string\" pattern=\"/^[a-z]+(-[a-z]+)+$/\" />\n<let name=\"s\" value=\"my-post\" />\n<echo>$s</echo>\n</kscript>";
        Assert.Equal("my-post", ScriptRunner.Run(xml, path: Path.Combine(Path.GetTempPath(), "bare-words.ks.xml")).TrimEnd('\n'));
    }

    [Theory]
    [InlineData("/^a+$/", "aaa", true)]
    [InlineData("/a+/", "baaa", false)]
    [InlineData("/^A+$/i", "aaa", true)]
    [InlineData("^b|bc$", "bc", true)]
    [InlineData("[", "x", false)]
    [InlineData("", "x", false)]
    public void PatternsMatchWholeWords(string pattern, string word, bool matches) => Assert.Equal(matches, BareWordTypes.Matches(pattern, word));

    [Fact]
    public void PatternsThatTakeTooLongDontMatch() =>
        Assert.False(BareWordTypes.Matches("/^(a+)+$/", new string('a', 40) + "!"));
}
