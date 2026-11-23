using KScript.Runtime;

namespace KScript.Tests;

/// <summary>
/// Pattern literals written as words joined by - + / or . (<c>my-post</c>, <c>api.corp.com</c>, <c>1.4.2</c>), and
/// pattern types built on text native types such as domain.
/// </summary>
public sealed class CompoundPatternLiteralTests
{
    private const string Slug = "type Slug with [string] { pattern: /^[a-z0-9]+(-[a-z0-9]+)+$/ }\n";

    private static string Run(string code)
    {
        try
        {
            return ScriptRunner.Run(code, path: Path.Combine(Path.GetTempPath(), "compound-words.kscript")).TrimEnd('\n');
        }
        catch (KScriptException e)
        {
            return e.Message;
        }
    }

    [Theory]
    [InlineData(Slug + "post = my-first-post\necho $post", "my-first-post")]
    [InlineData(Slug + "echo [my-post, other-post]->count()", "2")]
    [InlineData(Slug + "echo my-post == my-post", "true")]
    [InlineData(Slug + "fn show(s: string) => \"<$s>\"\necho @show(my-post)", "<my-post>")]
    [InlineData(Slug + "fn show(s) => \"<$s>\"\necho @show(s: my-post)", "<my-post>")]
    [InlineData(Slug + "echo { post: my-post }.post", "my-post")]
    [InlineData("type Ver with [string] { pattern: /^\\d+\\.\\d+\\.\\d+$/ }\nv = 1.4.2\necho $v", "1.4.2")]
    [InlineData("type Host with [domain] { pattern: /^[a-z]+\\.corp\\.com$/ }\nh = api.corp.com\necho $h->type()", "domain")]
    [InlineData("type Host with [domain] {\n  min: 20\n  pattern: /^[a-z]+\\.corp\\.com$/\n}\nh = api.corp.com", "must be at least 20 characters")]
    [InlineData("type Zone with [timezone] { pattern: /^Europe\\/[A-Za-z]+$/ }\nz = Europe/London\necho $z->type()", "timezone")]
    public void JoinedWordsAreReadAsTheirPatternType(string code, string expected) => Assert.Contains(expected, Run(code));

    [Theory]
    // No type fits the whole word: it fails at its first name, and no part of it is read as a type on its own.
    [InlineData("type Word with [string] { pattern: /^[a-z]+$/ }\nh = api.corp.com", "Unknown name 'api'")]
    [InlineData("type Word with [string] { pattern: /^[a-z]+$/ }\nx = my-post", "Unknown name 'my'")]
    // Arithmetic stays arithmetic.
    [InlineData("type Sum with [string] { pattern: /^[0-9+]+$/ }\necho 1+2", "3")]
    [InlineData(Slug + "n = 3\necho $n-1", "2")]
    [InlineData(Slug + "a = 1\necho a-b", "Unknown name 'a'")]
    // Two types that fit are named.
    [InlineData(Slug + "type Ref with [string] { pattern: /^[a-z]+-[a-z]+$/ }\nr = my-post", "matches more than one literal type: Ref, Slug")]
    // A handle type keeps its own @ form.
    [InlineData("type Admin with [handle] { pattern: /^@adm-[a-z]+$/ }\na = adm-joan", "Unknown name 'adm'")]
    public void OtherWordsKeepTheirMeaning(string code, string expected) => Assert.Contains(expected, Run(code));

    [Fact]
    public void XmlScriptsReadJoinedWordsToo()
    {
        string xml = "<kscript>\n<type name=\"Slug\" with=\"string\" pattern=\"/^[a-z]+(-[a-z]+)+$/\" />\n<let name=\"s\" value=\"my-post\" />\n<echo>$s</echo>\n</kscript>";
        Assert.Equal("my-post", ScriptRunner.Run(xml, path: Path.Combine(Path.GetTempPath(), "compound-words.ks.xml")).TrimEnd('\n'));
    }
}
