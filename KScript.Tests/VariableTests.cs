namespace KScript.Tests;

public class VariableTests
{
    [Fact]
    public void Def_IsSubstituted()
    {
        Assert.Equal("5\n", ScriptRunner.RunBody("<def id=\"x\">5</def><echo>$x</echo>"));
    }

    [Fact]
    public void SimilarNames_DoNotCollide()
    {
        Assert.Equal("5 7\n", ScriptRunner.RunBody("<def id=\"x\">5</def><def id=\"xy\">7</def><echo>$x $xy</echo>"));
    }

    [Fact]
    public void DollarDigitsInValues_AreNotRegexSubstitutions()
    {
        // Values were passed to Regex.Replace as replacement patterns, so "$0" was replaced by the match.
        Assert.Equal("cost $1 or $0\n", ScriptRunner.RunBody("<def id=\"p\">cost $1 or $0</def><echo>$p</echo>"));
    }

    [Fact]
    public void SurroundingQuotes_AreStrippedOnce()
    {
        Assert.Equal("quoted\n", ScriptRunner.RunBody("<echo>'quoted'</echo>"));
    }

    [Fact]
    public void QuotesThatDoNotSurroundTheWholeValue_AreKept()
    {
        Assert.Equal("'a' and 'b'\n", ScriptRunner.RunBody("<echo>'a' and 'b'</echo>"));
    }

    [Fact]
    public void Braces_DoNotThrow()
    {
        Assert.Equal("A {0} B\n", ScriptRunner.RunBody("<echo>A {0} B</echo>"));
    }

    [Theory]
    [InlineData("Hello", "upper", "HELLO")]
    [InlineData("Hello", "lower", "hello")]
    [InlineData("Hello World", "length", "11")]
    [InlineData("abc", "reverse", "cba")]
    [InlineData("5", "increment", "6")]
    [InlineData("5", "decrement", "4")]
    [InlineData("5", "is_number", "yes")]
    public void VariableFunctions_Evaluate(string value, string function, string expected)
    {
        Assert.Equal(expected + "\n", ScriptRunner.RunBody($"<def id=\"v\">{value}</def><echo>$v->{function}()</echo>"));
    }

    [Fact]
    public void Trim_RemovesWhitespace()
    {
        Assert.Equal("[hi]\n", ScriptRunner.RunBody("<def id=\"s\">  hi  </def><echo>[$s->trim()]</echo>"));
    }

    [Fact]
    public void MultipleVariableFunctions_InOneString_AreAllApplied()
    {
        // Each replacement used to start again from the original string, so only the last one survived.
        Assert.Equal("HELLO world\n",
            ScriptRunner.RunBody("<def id=\"s\">Hello</def><def id=\"t\">World</def><echo>$s->upper() $t->lower()</echo>"));
    }

    [Fact]
    public void TiedVariableFunction_Adds()
    {
        Assert.Equal("3\n", ScriptRunner.RunBody("<def id=\"a\">1</def><def id=\"b\">2</def><echo>$a%$b->add()</echo>"));
    }

    [Theory]
    [InlineData("replace", "2")]
    [InlineData("append", "12")]
    [InlineData("add", "3")]
    public void Update_Types(string type, string expected)
    {
        Assert.Equal(expected + "\n", ScriptRunner.RunBody($"<def id=\"x\">1</def><update to=\"x\" type=\"{type}\">2</update><echo>$x</echo>"));
    }

    [Fact]
    public void Defm_DefinesSeveral()
    {
        Assert.Equal("same same\n", ScriptRunner.RunBody("<defm ids=\"a,b\">same</defm><echo>$a $b</echo>"));
    }

    [Fact]
    public void Input_ReadsFromStdin()
    {
        Assert.Equal("Name? Hello, Kai!\n",
            ScriptRunner.RunBody("<input to=\"name\" type=\"text\">Name? </input><echo>Hello, $name!</echo>", input: "Kai\n"));
    }
}
