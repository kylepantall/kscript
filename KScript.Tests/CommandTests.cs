using KScript.KScriptExceptions;

namespace KScript.Tests;

public class CommandTests
{
    private static string Echo(string expression, string setup = "") =>
        ScriptRunner.RunBody($"{setup}<echo>{expression}</echo>").TrimEnd('\n');

    [Theory]
    [InlineData("@to_lower('HELLO')", "hello")]
    [InlineData("@to_lower(@to_lower('X')) and @to_lower('Y')", "x and y")]
    [InlineData("@not(@equals('a','b'))", "yes")]
    [InlineData("@equals('a', 'A', 'true')", "yes")]
    [InlineData("@equals('a', 'A')", "no")]
    [InlineData("@length('hello')", "5")]
    [InlineData("@split('a,b,c', ',', '1')", "b")]
    [InlineData("@ifnull('', 'fallback')", "fallback")]
    [InlineData("@contains('hello', 'ell')", "yes")]
    [InlineData("@and('yes', 'no')", "no")]
    [InlineData("@any('yes', 'no')", "yes")]
    [InlineData("@atindex('hello', '1')", "e")]
    [InlineData("@range('5', '1', '10')", "yes")]
    [InlineData("@char('65')", "A")]
    [InlineData("@from_base64(@to_base64('hi'))", "hi")]
    [InlineData("@to_md5('hi')", "49F68A5C8493EC2C0BF489821C21FC3B")]
    [InlineData("@extension_of('a.txt')", "txt")]
    [InlineData("@average('1', '2', '3')", "2")]
    [InlineData("@random('4', '4')", "4")]
    public void Commands_Evaluate(string expression, string expected)
    {
        Assert.Equal(expected, Echo(expression));
    }

    [Theory]
    [InlineData("@math('1+2')", "3")]
    [InlineData("@math('7/2')", "3.5")]
    [InlineData("@math(2*(3+4))", "14")]
    public void Math_Evaluates(string expression, string expected)
    {
        Assert.Equal(expected, Echo(expression));
    }

    [Theory]
    [InlineData("@more_less('1', 'lt', '2')", "yes")]
    [InlineData("@more_less('3', 'mt', '2')", "yes")]
    [InlineData("@more_less('2', 'mte', '2')", "yes")]
    [InlineData("@more_less('2', 'lte', '1')", "no")]
    [InlineData("@more_less('1', '2')", "yes")]
    [InlineData("@more_less('1.5', 'lt', '2.25')", "yes")]
    public void MoreLess_Compares(string expression, string expected)
    {
        Assert.Equal(expected, Echo(expression));
    }

    [Fact]
    public void WhitespaceAroundArguments_IsIgnored()
    {
        Assert.Equal("b", Echo("@split( 'a,b,c' ,  ','  , '1' )"));
    }

    [Fact]
    public void CommandNames_AreCaseInsensitive()
    {
        Assert.Equal("hello", Echo("@TO_LOWER('HELLO')"));
    }

    [Fact]
    public void EmailAddresses_AreNotTreatedAsCommands()
    {
        Assert.Equal("Email me@example.com (work)", Echo("Email me@example.com (work)"));
    }

    [Fact]
    public void ApostrophesInText_DoNotBreakCommands()
    {
        Assert.Equal("Don't x", Echo("Don't @to_lower('X')"));
    }

    [Fact]
    public void UnknownCommand_ThrowsClearError()
    {
        var ex = Assert.Throws<KScriptException>(() => Echo("@no_such_command('a')"));
        Assert.Contains("Unknown command '@no_such_command'", ex.Message);
    }

    [Fact]
    public void WrongArgumentCount_ThrowsClearError()
    {
        var ex = Assert.Throws<KScriptException>(() => Echo("@split('a,b', ',')"));
        Assert.Contains("does not accept 2 argument(s)", ex.Message);
        Assert.Contains("@split(str, split_str, index)", ex.Message);
    }

    [Fact]
    public void FailingCommand_ReportsTheCommand()
    {
        var ex = Assert.Throws<KScriptException>(() => Echo("@split('a,b', ',', '9')"));
        Assert.Contains("@split('a,b', ',', '9')", ex.Message);
    }

    [Fact]
    public void Errors_ArePrintedWhenExceptionsAreNotThrown()
    {
        string output = ScriptRunner.Run("<kscript throw_exceptions=\"no\"><echo>@no_such_command()</echo><echo>after</echo></kscript>");
        Assert.Contains("Unknown command '@no_such_command'", output);
        Assert.Contains("after", output);
    }

    [Fact]
    public void Guid_HasNoArguments()
    {
        Assert.True(Guid.TryParse(Echo("@guid()"), out _));
    }

    [Fact]
    public void Variables_AreSubstitutedInsideArguments()
    {
        Assert.Equal("hello world", Echo("@to_lower('$greeting')", "<def id=\"greeting\">HELLO WORLD</def>"));
    }
}
