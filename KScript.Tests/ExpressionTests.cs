using static KScript.Tests.ScriptRunner;

namespace KScript.Tests;

/// <summary>Expressions: operators, literals, members, null handling, ranges, match and methods.</summary>
public class ExpressionTests
{
    [Theory]
    [InlineData("1 + 2 * 3", "7")]
    [InlineData("(1 + 2) * 3", "9")]
    [InlineData("7 % 3", "1")]
    [InlineData("2 ** 10", "1024")]
    [InlineData("10 / 4", "2.5")]
    [InlineData("'a' + 1", "a1")]
    [InlineData("'ab' * 3", "ababab")]
    [InlineData("3 lt 4 and 'x' ne 'y'", "true")]
    [InlineData("not (1 == 1)", "false")]
    [InlineData("1 == '1'", "true")]
    [InlineData("1 === '1'", "false")]
    [InlineData("3 ge 3", "true")]
    [InlineData("'.cs' in ['.cs', '.xml']", "true")]
    [InlineData("null ?? 'fallback'", "fallback")]
    [InlineData("true ? 'y' : 'n'", "y")]
    [InlineData("match 2 { 1 => 'one', 2 => 'two', _ => '?' }", "two")]
    [InlineData("{ a: { b: [10, 20] } }.a.b[1]", "20")]
    [InlineData("[1, 2, 3][-1]", "3")]
    [InlineData("(1..4)->join(',')", "1,2,3")]
    public void Evaluates(string expression, string expected)
    {
        Assert.Equal(expected, Eval(expression));
    }

    [Fact]
    public void NullSafeMembers()
    {
        Assert.Equal("guest\n", RunBody("<let name=\"user\" value=\"null\" /><echo>{ $user?.name ?? 'guest' }</echo>"));
    }

    [Fact]
    public void BracesAreEscapedByDoubling()
    {
        Assert.Equal("Use { and }\n", RunBody("<echo>Use {{ and }}</echo>"));
    }

    [Fact]
    public void UndefinedVariablesSuggestTheClosestName()
    {
        var ex = Fails("<let name=\"total\" value=\"1\" /><echo>{ $totl }</echo>");
        Assert.Contains("$total", ex.Message);
    }

    [Theory]
    [InlineData("'Hello'->upper()", "HELLO")]
    [InlineData("'  x  '->trim()", "x")]
    [InlineData("'a,b'->split(',')->count()", "2")]
    [InlineData("'hello world'->title()", "Hello World")]
    [InlineData("'hello'->replace(old: 'l', new: 'L')", "heLLo")]
    [InlineData("'abc'->contains('b')", "true")]
    [InlineData("'42'->to_number() + 1", "43")]
    [InlineData("[3, 1, 2]->sort()->join('')", "123")]
    [InlineData("[1, 2, 3]->reduce(fn(t, x) => $t + $x, initial: 0)", "6")]
    [InlineData("[1, 2, 2, 3]->unique()->count()", "3")]
    [InlineData("{ a: 1, b: 2 }->keys()->join(',')", "a,b")]
    [InlineData("{ a: 1 }->merge({ b: 2 })->to_json()", "{\"a\":1,\"b\":2}")]
    [InlineData("3.14159->round(digits: 2)", "3.14")]
    [InlineData("1234567->format(pattern: 'N0')", "1,234,567")]
    [InlineData("'{\"a\": [1, 2]}'->json().a[1]", "2")]
    [InlineData("[]->type()", "list")]
    [InlineData("@max(3, 9, 2)", "9")]
    [InlineData("@range(3)->join(',')", "0,1,2")]
    public void Methods(string expression, string expected)
    {
        Assert.Equal(expected, Eval(expression));
    }

    [Fact]
    public void UnknownMethodsSuggestTheClosestName()
    {
        var ex = Fails("<echo>{ 'x'->uper() }</echo>");
        Assert.Contains("upper", ex.Message);
    }

    [Fact]
    public void DatesHaveMethods()
    {
        Assert.Equal("2026-01-31", Eval("'2026-01-30'->to_date()->add_days(1)->format(pattern: 'yyyy-MM-dd')"));
    }

    [Fact]
    public void MembersAndIndexesCanBeAssigned()
    {
        Assert.Equal("{\"a\":5,\"list\":[1,9]}\n", RunBody("<let name=\"m\" value=\"{ a: 1, list: [1, 2] }\" /><do>$m.a = 5; $m.list[1] = 9</do><echo>{ $m->to_json() }</echo>"));
    }
}
