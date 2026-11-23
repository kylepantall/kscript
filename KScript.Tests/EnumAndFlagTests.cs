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
    [InlineData("Size.medium", "medium")]
    [InlineData("'large' in Size", "true")]
    [InlineData("Size->keys()->join(',')", "small,medium,large")]
    public void EnumsAndFlags(string expression, string expected)
    {
        Assert.Equal(expected + "\n", RunBody(Pack + $"<echo>{{ {expression} }}</echo>"));
    }

    [Theory]
    [InlineData("@pack(huge)", "'huge' isn't a Size. Use small, medium or large.")]
    [InlineData("@pack(Size.huge)", "'Size.huge' isn't a Size. Use small, medium or large.")]
    [InlineData("@pack('huge')", "'huge' isn't a Size.")]
    [InlineData("@pack(large, giift)", "no flag 'giift'. Did you mean gift?")]
    [InlineData("@pack(large, not gfit)", "no flag 'gfit'")]
    [InlineData("@pack(large, gift, gift)", "twice")]
    [InlineData("@pack(gift: 1, large, 5)", "takes 2 arguments, and has no parameter left for a number (5)")]
    [InlineData("@pack(gift: 1, huge)", "has no flag 'huge'")]
    [InlineData("@pack(5)", "The 'size' argument of @pack() must be a Size: small, medium, large. It was a number (5).")]
    [InlineData("@wrap()", "must be a Size: small, medium, large. It was the string 'smal'. Did you mean small?")]
    [InlineData("@round(1.5, digits: two)", "Unknown name 'two' in @round()")]
    [InlineData("@round(1.5, zzzzqq)", "a bare word must be one of its parameters (number, digits) used as a flag.")]
    [InlineData("@files('.', pattern: '*', recursive, recursive)", "@files() was given 'recursive' twice.")]
    public void EnumAndFlagMistakesAreExplained(string expression, string expected)
    {
        Assert.Contains(expected, Fails(Pack + "<fn name=\"wrap\" params=\"size: Size = 'smal'\" value=\"$size\" />" + $"<echo>{{ {expression} }}</echo>", "KScript.Math", "KScript.IO").Message);
    }

    private const string Sizes = "<enum name=\"Size\" values=\"small, medium, large\" />"
        + "<fn name=\"pack_all\" params=\"sizes: Size[]\" value=\"$sizes->join(',')\" />"
        + "<fn name=\"plain\" params=\"items\" value=\"$items->join(',')\" />";

    [Theory]
    [InlineData("@pack_all([large, small])", "large,small")]
    [InlineData("@pack_all(sizes: [medium])", "medium")]
    [InlineData("@pack_all([LARGE, 'small'])", "large,small")]
    [InlineData("@pack_all([])", "")]
    public void ArraysOfEnumValuesCanBeBare(string expression, string expected)
    {
        Assert.Equal(expected + "\n", RunBody(Sizes + $"<echo>{{ {expression} }}</echo>"));
    }

    [Theory]
    [InlineData("@pack_all([large, huge])", "'huge' isn't a Size. Use small, medium or large.")]
    [InlineData("@plain([large])", "Unknown name 'large'")]
    [InlineData("@pack_all(sizes: [huge])", "'huge' isn't a Size.")]
    public void BareWordsInArraysMustBeEnumValues(string expression, string expected)
    {
        Assert.Contains(expected, Fails(Sizes + $"<echo>{{ {expression} }}</echo>").Message);
    }

    [Fact]
    public void Late_enum_values_and_bare_words_print_as_written()
    {
        Assert.Equal("1large\n", RunBody(Pack + "<fn name=\"f\" params=\"a, size: Size\" value=\"$a + $size\" /><echo>{ @f(a: 1, large) }</echo>"));
        Assert.Equal("not recursive", new BareWord("recursive", negated: true, afterNamed: false).ToString());
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
