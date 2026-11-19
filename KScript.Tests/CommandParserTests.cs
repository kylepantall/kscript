using KScript.CommandHandler;
using KScript.KScriptExceptions;

namespace KScript.Tests;

public class CommandParserTests
{
    private static List<CommandSegment> Parse(string s) => CommandExpressionParser.Parse(s);

    [Fact]
    public void PlainText_IsSingleTextSegment()
    {
        var segments = Parse("just text");
        var text = Assert.IsType<TextSegment>(Assert.Single(segments));
        Assert.Equal("just text", text.Text);
    }

    [Fact]
    public void SimpleCommand_IsParsedWithQuotedArgument()
    {
        var call = Assert.IsType<CommandCall>(Assert.Single(Parse("@to_lower('HELLO')")));
        Assert.Equal("to_lower", call.Name);
        var part = Assert.Single(Assert.Single(call.Arguments).Parts);
        Assert.Equal(ArgumentPartKind.Quoted, part.Kind);
        Assert.Equal("HELLO", part.Text);
    }

    [Fact]
    public void EmptyArgumentList_HasNoArguments()
    {
        var call = Assert.IsType<CommandCall>(Assert.Single(Parse("@guid()")));
        Assert.Empty(call.Arguments);
    }

    [Fact]
    public void CommasAndBracketsInsideQuotes_DoNotSplitArguments()
    {
        var call = Assert.IsType<CommandCall>(Assert.Single(Parse("@split('a,b,(c)', ',', '1')")));
        Assert.Equal(3, call.Arguments.Count);
        Assert.Equal("a,b,(c)", call.Arguments[0].Parts.Single(p => p.Kind == ArgumentPartKind.Quoted).Text);
    }

    [Fact]
    public void NestedCommands_AreParsedRecursively()
    {
        var call = Assert.IsType<CommandCall>(Assert.Single(Parse("@not(@equals('a','b'))")));
        var inner = Assert.Single(Assert.Single(call.Arguments).Parts);
        Assert.Equal(ArgumentPartKind.Command, inner.Kind);
        Assert.Equal("equals", inner.Command.Name);
        Assert.Equal(2, inner.Command.Arguments.Count);
    }

    [Fact]
    public void BalancedBracketsInRawArguments_AreKept()
    {
        var call = Assert.IsType<CommandCall>(Assert.Single(Parse("@math(2*(3+4))")));
        Assert.Equal("2*(3+4)", Assert.Single(Assert.Single(call.Arguments).Parts).Text);
    }

    [Fact]
    public void MultipleTopLevelCommands_AreSeparatedByText()
    {
        var segments = Parse("@a('x') and @b('y')");
        Assert.Equal(3, segments.Count);
        Assert.IsType<CommandCall>(segments[0]);
        Assert.Equal(" and ", Assert.IsType<TextSegment>(segments[1]).Text);
        Assert.IsType<CommandCall>(segments[2]);
    }

    [Theory]
    [InlineData("me@example.com (work)")]
    [InlineData("@ alone")]
    [InlineData("50@")]
    [InlineData("@name without bracket")]
    public void AtSignWithoutCommandSyntax_IsText(string input)
    {
        Assert.False(CommandExpressionParser.ContainsCommand(input));
        Assert.Equal(input, string.Concat(Parse(input).Select(s => s.ToString())));
    }

    [Fact]
    public void ApostrophesOutsideCommands_AreText()
    {
        var segments = Parse("Don't @to_lower('X')");
        Assert.Equal("Don't ", Assert.IsType<TextSegment>(segments[0]).Text);
        Assert.IsType<CommandCall>(segments[1]);
    }

    [Fact]
    public void UnterminatedString_Throws()
    {
        var ex = Assert.Throws<KScriptException>(() => Parse("@to_lower('abc)"));
        Assert.Contains("Unterminated string", ex.Message);
    }

    [Fact]
    public void MissingClosingBracket_Throws()
    {
        var ex = Assert.Throws<KScriptException>(() => Parse("@to_lower('abc'"));
        Assert.Contains("missing a closing bracket", ex.Message);
    }

    [Fact]
    public void Source_IsOriginalCallText()
    {
        var call = Assert.IsType<CommandCall>(Parse("x @math( 1 + 2 ) y")[1]);
        Assert.Equal("@math( 1 + 2 )", call.Source);
    }
}
