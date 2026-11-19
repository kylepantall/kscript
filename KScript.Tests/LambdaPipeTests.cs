using KScript.KScriptExceptions;

namespace KScript.Tests;

public class LambdaPipeTests
{
    private static string Echo(string expression, string setup = "") =>
        ScriptRunner.RunBody($"{setup}<echo>{expression}</echo>").TrimEnd('\n');

    [Fact]
    public void Pipe_IntoMethods()
    {
        Assert.Equal("KYLE", Echo("$name |> trim() |> upper()", "<def id=\"name\">  kyle  </def>"));
    }

    [Fact]
    public void Pipe_IntoCommand_PassesValueAsFirstArgument()
    {
        Assert.Equal("b", Echo("$csv |> @split(',', '1')", "<def id=\"csv\">a,b,c</def>"));
    }

    [Fact]
    public void Pipe_IntoLambda()
    {
        Assert.Equal("elyk", Echo("$name |> fn($s) => $s->lower()->reverse()", "<def id=\"name\">KYLE</def>"));
    }

    [Fact]
    public void Lambda_ParametersWithoutDollar()
    {
        Assert.Equal("HI", Echo("'hi' |> fn(s) => $s->upper()"));
    }

    [Fact]
    public void Lambda_BodyStopsAtText()
    {
        Assert.Equal("HI there", Echo("'hi' |> fn($s) => $s->upper() there"));
    }

    [Fact]
    public void StoredLambda_UsedInPipe()
    {
        Assert.Equal("HI!", Echo("'hi.' |> $shout", "<def id=\"shout\">fn($s) => $s->upper()->replace('.', '!')</def>"));
    }

    [Fact]
    public void StoredLambda_Call_WithSeveralArguments()
    {
        Assert.Equal("5", Echo("$add->call(2, 3)", "<def id=\"add\">fn($a, $b) => $a->add($b)</def>"));
    }

    [Fact]
    public void StoredLambda_WithNoParameters()
    {
        Assert.Equal("hi", Echo("$greet->call()", "<def id=\"greet\">fn() => 'hi'</def>"));
    }

    [Fact]
    public void Lambda_BracketedBodyWithNestedPipe()
    {
        Assert.Equal("21", Echo("$v |> fn($n) => ($n->mul(2) |> fn($m) => $m->add(1))", "<def id=\"v\">10</def>"));
    }

    [Fact]
    public void Lambda_ParametersDoNotLeak()
    {
        Assert.Equal("HI outer", Echo("'hi' |> fn($s) => $s->upper() $s", "<def id=\"s\">outer</def>"));
    }

    [Fact]
    public void Lambda_ParametersAreRemovedAfterCall()
    {
        Assert.Equal("no", ScriptRunner.RunBody(
            "<def id=\"r\">@to_lower('X' |> fn($tmp_param) => $tmp_param)</def><echo>@exists('tmp_param')</echo>").Trim());
    }

    [Fact]
    public void Lambda_InsideCommandArgument()
    {
        Assert.Equal("yes", Echo("@equals('ab' |> fn($s) => $s->upper(), 'AB')"));
    }

    [Fact]
    public void PipingIntoNonLambdaVariable_Throws()
    {
        var ex = Assert.Throws<KScriptException>(() => Echo("'x' |> $v", "<def id=\"v\">plain</def>"));
        Assert.Contains("is not a lambda", ex.Message);
    }

    [Fact]
    public void RecursiveLambda_IsStopped()
    {
        var ex = Assert.Throws<KScriptException>(() => Echo("'x' |> $loop", "<def id=\"loop\">fn($s) => $s |> $loop</def>"));
        Assert.Contains("nested more than", ex.Message);
    }

    [Fact]
    public void Lambda_OnItsOwn_IsItsSource()
    {
        Assert.Equal("fn($s) => $s->upper()", Echo("$f", "<def id=\"f\">fn($s) => $s->upper()</def>"));
    }
}
