using KScript.Runtime;

using static KScript.Tests.ScriptRunner;

namespace KScript.Tests;

/// <summary><c>&lt;enum&gt;</c>, <c>&lt;const&gt;</c>, bare enum values, yes/no flags and positional arguments after named ones.</summary>
public class EnumAndFlagTests
{
    private const string Pack = "<enum name=\"Size\" values=\"small, medium, large\" />"
        + "<fn name=\"pack\" params=\"size: Size, gift: bool = false\" value=\"$size + ($gift ? '+gift' : '')\" />";

    [Theory]
    [InlineData("@pack(large)", "large")]
    [InlineData("@pack(size: small)", "small")]
    [InlineData("@pack('medium')", "medium")]
    [InlineData("@pack('LARGE')", "large")]
    [InlineData("@pack(large, gift)", "large+gift")]
    [InlineData("@pack(large, not gift)", "large")]
    [InlineData("@pack(size: large, gift)", "large+gift")]
    [InlineData("@pack(gift, small)", "small+gift")]
    [InlineData("$Size.medium", "medium")]
    [InlineData("'large' in $Size", "true")]
    [InlineData("$Size->keys()->join(',')", "small,medium,large")]
    public void EnumsAndFlags(string expression, string expected)
    {
        Assert.Equal(expected + "\n", RunBody(Pack + $"<echo>{{ {expression} }}</echo>"));
    }

    [Theory]
    [InlineData("@pack(huge)", "'huge' isn't a Size. Use small, medium or large.")]
    [InlineData("@pack('huge')", "'huge' isn't a Size.")]
    [InlineData("@pack(large, giift)", "no flag 'giift'. Did you mean gift?")]
    [InlineData("@pack(large, not gfit)", "no flag 'gfit'")]
    [InlineData("@pack(large, gift, gift)", "twice")]
    public void EnumAndFlagMistakesAreExplained(string expression, string expected)
    {
        Assert.Contains(expected, Fails(Pack + $"<echo>{{ {expression} }}</echo>").Message);
    }

    [Fact]
    public void BuiltInEnumValuesCanBeBare()
    {
        Assert.Equal(Eval("@known_folder('downloads')", "KScript.IO"), Eval("@known_folder(downloads)", "KScript.IO"));
        Assert.Equal(Eval("@known_folder('home')", "KScript.IO"), Eval("@known_folder(name: home)", "KScript.IO"));
        Assert.Contains("Did you mean downloads?", Fails("<echo>{ @known_folder(dowloads) }</echo>", "KScript.IO").Message);
        Assert.Contains("Did you mean downloads?", Fails("<echo>{ @known_folder('dowloads') }</echo>", "KScript.IO").Message);
    }

    [Fact]
    public void BuiltInFlags()
    {
        Assert.Equal("[\n  1\n]", Eval("[1]->to_json(indented)"));
        Assert.Equal("[1]", Eval("[1]->to_json(not indented)"));
    }

    [Fact]
    public void PositionalArgumentsAfterNamedOnesFillTheRest()
    {
        string sep = Path.DirectorySeparatorChar.ToString();
        Assert.Equal("base" + sep + "archive" + sep + "x.txt", Eval("@join_path(base: 'base', ['archive', 'x.txt'])", "KScript.IO"));
        Assert.Equal("a" + sep + "b", Eval("@join_path('a', 'b')", "KScript.IO"));
        Assert.Equal("Hi, K!\n", RunBody("<fn name=\"greet\" params=\"who, greeting = 'Hello'\" value=\"$greeting + ', ' + $who + '!'\" /><echo>{ @greet(greeting: 'Hi', 'K') }</echo>"));
    }

    [Fact]
    public void ConstsCantChangeAndAreExported()
    {
        Assert.Equal("10\n", RunBody("<const name=\"max\" value=\"10\" /><echo>$max</echo>"));
        Assert.Contains("const", Fails("<const name=\"max\" value=\"10\" /><set name=\"max\" value=\"2\" />").Message);
    }

    [Theory]
    [InlineData("<enum name=\"E\" values=\"\" />", "needs its values")]
    [InlineData("<enum name=\"E\" values=\"a, 2b\" />", "'2b' can't be an enum value")]
    [InlineData("<enum name=\"E\" values=\"a, A\" />", "twice")]
    [InlineData("<fn name=\"f\" params=\"x: Nope\" value=\"$x\" /><echo>{ @f(1) }</echo>", "Unknown type 'Nope'")]
    public void BadEnumsAreExplained(string body, string expected)
    {
        Assert.Contains(expected, Fails(body).Message);
    }
}
