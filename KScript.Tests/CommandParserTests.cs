using KScript.CommandHandler;
using KScript.KScriptExceptions;

namespace KScript.Tests;

public class CommandParserTests
{
    private static List<Expr> Parse(string s) => CommandExpressionParser.Parse(s);

    private static T Single<T>(string s) where T : Expr => Assert.IsType<T>(Assert.Single(Parse(s)));

    [Fact]
    public void PlainText_IsSingleTextSegment()
    {
        Assert.Equal("just text", Single<TextExpr>("just text").Text);
    }

    [Fact]
    public void SimpleCommand_IsParsedWithQuotedArgument()
    {
        var call = Single<CommandExpr>("@to_lower('HELLO')");
        Assert.Equal("to_lower", call.Name);
        var literal = Assert.IsType<LiteralExpr>(Assert.Single(Assert.Single(call.Arguments).Parts));
        Assert.Equal("HELLO", literal.Value);
    }

    [Fact]
    public void EmptyArgumentList_HasNoArguments()
    {
        Assert.Empty(Single<CommandExpr>("@guid()").Arguments);
    }

    [Fact]
    public void CommasAndBracketsInsideQuotes_DoNotSplitArguments()
    {
        var call = Single<CommandExpr>("@split('a,b,(c)', ',', '1')");
        Assert.Equal(3, call.Arguments.Count);
        Assert.Equal("a,b,(c)", call.Arguments[0].Parts.OfType<LiteralExpr>().Single().Value);
    }

    [Fact]
    public void NestedCommands_AreParsedRecursively()
    {
        var call = Single<CommandExpr>("@not(@equals('a','b'))");
        var inner = Assert.IsType<CommandExpr>(Assert.Single(Assert.Single(call.Arguments).Parts));
        Assert.Equal("equals", inner.Name);
        Assert.Equal(2, inner.Arguments.Count);
    }

    [Fact]
    public void BalancedBracketsInRawArguments_AreKept()
    {
        var call = Single<CommandExpr>("@math(2*(3+4))");
        Assert.Equal("2*(3+4)", Assert.IsType<TextExpr>(Assert.Single(Assert.Single(call.Arguments).Parts)).Text);
    }

    [Fact]
    public void MultipleTopLevelCommands_AreSeparatedByText()
    {
        var segments = Parse("@a('x') and @b('y')");
        Assert.Equal(3, segments.Count);
        Assert.IsType<CommandExpr>(segments[0]);
        Assert.Equal(" and ", Assert.IsType<TextExpr>(segments[1]).Text);
        Assert.IsType<CommandExpr>(segments[2]);
    }

    [Theory]
    [InlineData("me@example.com (work)")]
    [InlineData("@ alone")]
    [InlineData("50@")]
    [InlineData("@name without bracket")]
    [InlineData("a -> b |> c")]
    [InlineData("$price alone")]
    [InlineData("define fn later")]
    [InlineData("x => y")]
    public void TextWithoutExpressionSyntax_IsText(string input)
    {
        Assert.False(CommandExpressionParser.ContainsCommand(input));
        Assert.Equal(input, Single<TextExpr>(input).Text);
    }

    [Fact]
    public void ApostrophesOutsideCommands_AreText()
    {
        var segments = Parse("Don't @to_lower('X')");
        Assert.Equal("Don't ", Assert.IsType<TextExpr>(segments[0]).Text);
        Assert.IsType<CommandExpr>(segments[1]);
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
    public void MethodCall_OnVariable()
    {
        var method = Single<MethodExpr>("$i->increment()");
        Assert.Equal("increment", method.Name);
        Assert.Equal("i", Assert.IsType<VariableExpr>(method.Target).Name);
    }

    [Fact]
    public void MethodCalls_Chain()
    {
        var outer = Single<MethodExpr>("$name->trim()->upper()");
        Assert.Equal("upper", outer.Name);
        Assert.Equal("trim", Assert.IsType<MethodExpr>(outer.Target).Name);
    }

    [Theory]
    [InlineData("'hello'->upper()", typeof(LiteralExpr))]
    [InlineData("5->add(3)", typeof(LiteralExpr))]
    [InlineData("1.5->round()", typeof(LiteralExpr))]
    [InlineData("@args()->is_zero()", typeof(CommandExpr))]
    public void MethodCall_OnLiteralsAndCommands(string input, Type targetType)
    {
        Assert.IsType(targetType, Single<MethodExpr>(input).Target);
    }

    [Fact]
    public void MethodCall_WithArguments()
    {
        var method = Single<MethodExpr>("$s->replace('a', @to_lower('B'))");
        Assert.Equal(2, method.Arguments.Count);
        Assert.IsType<CommandExpr>(Assert.Single(method.Arguments[1].Parts, p => p is not TextExpr));
    }

    [Fact]
    public void VariableNamesWithDots_AreSupported()
    {
        Assert.Equal("greet.who", Assert.IsType<VariableExpr>(Single<MethodExpr>("$greet.who->upper()").Target).Name);
    }

    [Fact]
    public void Pipe_IntoMethodsAndCommands()
    {
        var outer = Single<PipeExpr>("$name |> trim() |> @to_lower()");
        Assert.IsType<CommandExpr>(outer.Right);
        var inner = Assert.IsType<PipeExpr>(outer.Left);
        Assert.Equal("trim", Assert.IsType<MethodRefExpr>(inner.Right).Name);
    }

    [Fact]
    public void Pipe_IntoLambda()
    {
        var pipe = Single<PipeExpr>("$name |> fn($s) => $s->upper()");
        var lambda = Assert.IsType<LambdaExpr>(pipe.Right);
        Assert.Equal(new[] { "s" }, lambda.Parameters);
        Assert.IsType<MethodExpr>(lambda.Body);
    }

    [Fact]
    public void Lambda_BodyEndsAtText()
    {
        var segments = Parse("$a |> fn($x) => $x->upper() and more");
        Assert.IsType<PipeExpr>(segments[0]);
        Assert.Equal(" and more", Assert.IsType<TextExpr>(segments[1]).Text);
    }

    [Theory]
    [InlineData("fn() => @guid()", 0)]
    [InlineData("fn(a, $b) => $a->add($b)", 2)]
    public void StoredLambda_Parses(string source, int parameterCount)
    {
        var lambda = CommandExpressionParser.ParseLambda(source);
        Assert.NotNull(lambda);
        Assert.Equal(parameterCount, lambda.Parameters.Count);
    }

    [Fact]
    public void NonLambda_IsNotParsedAsLambda()
    {
        Assert.Null(CommandExpressionParser.ParseLambda("hello"));
    }

    [Fact]
    public void TiedVariableFunctions_AreLeftForTheVariableHandler()
    {
        Assert.Equal("$a%$b->add()", Single<TextExpr>("$a%$b->add()").Text);
    }

    [Fact]
    public void ArrayLookups_AreLeftIntact()
    {
        var call = Single<CommandExpr>("@more_less(~people[$i]['age'],'mt','1')");
        Assert.Equal("~people[$i]['age']", Assert.IsType<TextExpr>(Assert.Single(call.Arguments[0].Parts)).Text);
    }
}
