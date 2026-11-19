using KScript.KScriptExceptions;

namespace KScript.Tests;

public class MethodTests
{
    private static string Echo(string expression, string setup = "") =>
        ScriptRunner.RunBody($"{setup}<echo>{expression}</echo>").TrimEnd('\n');

    [Theory]
    [InlineData("'hello'->upper()", "HELLO")]
    [InlineData("'Hello'->lower()", "hello")]
    [InlineData("'  x  '->trim()->length()", "1")]
    [InlineData("'abc'->reverse()", "cba")]
    [InlineData("'hello'->contains('ell')", "yes")]
    [InlineData("'hello'->starts_with('he')", "yes")]
    [InlineData("'hello'->ends_with('x')", "no")]
    [InlineData("'hello'->index_of('l')", "2")]
    [InlineData("'a.b.c'->replace('.', '/')", "a/b/c")]
    [InlineData("'hello'->substring(1, 3)", "ell")]
    [InlineData("'hello'->substring(3)", "lo")]
    [InlineData("'hello'->char_at(1)", "e")]
    [InlineData("'a,b,c'->split(',', 2)", "c")]
    [InlineData("'banana'->count('a')", "3")]
    [InlineData("'ab'->repeat(3)", "ababab")]
    [InlineData("'7'->pad_left(3, '0')", "007")]
    [InlineData("'x'->pad_right(3, '.')", "x..")]
    [InlineData("''->default('none')", "none")]
    [InlineData("'NULL'->is_empty()", "yes")]
    [InlineData("'http://x.com'->is_url()", "yes")]
    [InlineData("'a'->equals('A', 'yes')", "yes")]
    public void StringMethods(string expression, string expected)
    {
        Assert.Equal(expected, Echo(expression));
    }

    [Theory]
    [InlineData("5->add(3)", "8")]
    [InlineData("5->sub(7)", "-2")]
    [InlineData("1.5->mul(2)", "3")]
    [InlineData("7->div(2)", "3.5")]
    [InlineData("7->mod(3)", "1")]
    [InlineData("2->pow(10)", "1024")]
    [InlineData("3.14159->round(2)", "3.14")]
    [InlineData("2.5->round()", "3")]
    [InlineData("2.7->floor()", "2")]
    [InlineData("2.1->ceil()", "3")]
    [InlineData("'-4'->abs()", "4")]
    [InlineData("3->min(1)", "1")]
    [InlineData("3->max(10)", "10")]
    [InlineData("3->lt(4)", "yes")]
    [InlineData("4->lte(4)", "yes")]
    [InlineData("5->mt(4)", "yes")]
    [InlineData("4->mte(5)", "no")]
    [InlineData("5->between(1, 10)", "yes")]
    [InlineData("0->is_zero()", "yes")]
    [InlineData("'-1'->negative()", "yes")]
    [InlineData("5->increment()", "6")]
    [InlineData("5->increment(5)", "10")]
    [InlineData("5->decrement()", "4")]
    public void NumberMethods(string expression, string expected)
    {
        Assert.Equal(expected, Echo(expression));
    }

    [Theory]
    [InlineData("'yes'->not()", "no")]
    [InlineData("'yes'->and('no')", "no")]
    [InlineData("'no'->or('true')", "yes")]
    [InlineData("'yes'->xor('yes')", "no")]
    public void BoolMethods(string expression, string expected)
    {
        Assert.Equal(expected, Echo(expression));
    }

    [Theory]
    [InlineData("42", "number")]
    [InlineData("3.5", "number")]
    [InlineData("yes", "bool")]
    [InlineData("hello", "string")]
    [InlineData("NULL", "null")]
    public void Type_IsInferred(string value, string expected)
    {
        Assert.Equal(expected, Echo("$v->type()", $"<def id=\"v\">{value}</def>"));
    }

    [Fact]
    public void Increment_OnVariable_UpdatesIt()
    {
        Assert.Equal("6\n6\n", ScriptRunner.RunBody("<def id=\"i\">5</def><echo>$i->increment()</echo><echo>$i</echo>"));
    }

    [Fact]
    public void Increment_OnLiteral_DoesNotUpdateAnything()
    {
        Assert.Equal("6 5", Echo("@k('null')->default('5')->increment() $i", "<def id=\"i\">5</def>"));
    }

    [Fact]
    public void Methods_WithArgumentsOnVariables()
    {
        Assert.Equal("yes", Echo("$i->lt(10)", "<def id=\"i\">5</def>"));
    }

    [Fact]
    public void Methods_Chain()
    {
        Assert.Equal("[KYLE]", Echo("[$name->trim()->upper()]", "<def id=\"name\">  kyle  </def>"));
    }

    [Fact]
    public void Methods_OnCommandResults()
    {
        Assert.Equal("Total: 40 units", Echo("Total: @math('2+2')->mul(10) units"));
    }

    [Fact]
    public void Methods_InsideCommandArguments()
    {
        Assert.Equal("yes", Echo("@not(@equals($a->upper(), 'x'))", "<def id=\"a\">b</def>"));
    }

    [Fact]
    public void ForLoop_WithMethodConditions()
    {
        Assert.Equal("0\n1\n2\n", ScriptRunner.RunBody(
            "<for to=\"i\" while=\"$i->lt(3)\" math=\"$i->increment()\"><echo>$i</echo></for>"));
    }

    [Fact]
    public void LegacyVariableFunctions_StillWork()
    {
        Assert.Equal("yes", Echo("$list->to_array()", "<def id=\"list\">a,b</def>"));
        Assert.Equal("3", Echo("$a%$b->add()", "<def id=\"a\">1</def><def id=\"b\">2</def>"));
    }

    [Fact]
    public void WrongType_GivesClearError()
    {
        var ex = Assert.Throws<KScriptException>(() => Echo("'abc'->add(1)"));
        Assert.Contains("'add()' expects a number but got 'abc'", ex.Message);
    }

    [Fact]
    public void WrongArgumentCount_GivesClearError()
    {
        var ex = Assert.Throws<KScriptException>(() => Echo("'abc'->replace('a')"));
        Assert.Contains("'replace()' takes 2 argument(s) but was given 1", ex.Message);
    }

    [Fact]
    public void UnknownMethod_ListsAvailableMethods()
    {
        var ex = Assert.Throws<KScriptException>(() => Echo("'abc'->nope()"));
        Assert.Contains("Unknown method 'nope()'", ex.Message);
        Assert.Contains("upper", ex.Message);
    }

    [Fact]
    public void DivideByZero_GivesClearError()
    {
        Assert.Throws<KScriptException>(() => Echo("1->div(0)"));
    }
}
