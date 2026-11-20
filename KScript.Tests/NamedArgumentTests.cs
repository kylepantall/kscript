using KScript.Runtime;

using static KScript.Tests.ScriptRunner;

namespace KScript.Tests;

/// <summary>Named arguments: <c>@known_folder(name: 'downloads')</c> for commands, methods, &lt;fn&gt; functions and lambdas.</summary>
public class NamedArgumentTests
{
    private const string Greet = "<fn name=\"greet\" params=\"who, greeting = 'Hello', end = '!'\" value=\"$greeting + ', ' + $who + $end\" />";

    [Fact]
    public void CommandsTakeNamedArguments()
    {
        Assert.Equal(Eval("@known_folder('downloads')", "KScript.IO"), Eval("@known_folder(name: 'downloads')", "KScript.IO"));
        Assert.Equal("[1,3]", Eval("@range(start: 1, end: 5, step: 2)->to_json()"));
    }

    [Fact]
    public void NamedArgumentsMixWithPositionalOnes()
    {
        Assert.Equal("[1,3]", Eval("@range(1, step: 2, end: 5)->to_json()"));
    }

    [Fact]
    public void NamingALaterParameterLeavesTheOnesBetweenEmpty()
    {
        var info = new KScript.Metadata.CallableInfo(KScript.Metadata.CallableKind.Command, "t", "", new[] { KScript.Metadata.Signature.Parse("a, b?, c?") });
        CallContext call = ArgumentBinder.Bind(info, new CallContext(null, "t", new object[] { 1.0 }, new Dictionary<string, object> { ["c"] = 3.0 }));
        Assert.True(call.Has(0));
        Assert.False(call.Has(1));
        Assert.Equal("x", call.Str(1, "x"));
        Assert.Equal(3.0, call.Arg(2));
        Assert.Equal(7, call.Int(1, 7));
        Assert.Contains("needs a value for 'b'", Assert.Throws<KScriptException>(() => call.Need(1)).Message);
        Assert.Equal("**abc", Eval("'abc'->pad_left(char: '*', width: 5)"));
    }

    [Fact]
    public void MethodsTakeNamedArguments()
    {
        Assert.Equal("a-b", Eval("'a b'->replace(old: ' ', new: '-')"));
        Assert.Equal("a-b", Eval("'a b' |> replace(new: '-', old: ' ')"));
    }

    [Fact]
    public void FunctionsTakeNamedArgumentsInAnyOrder()
    {
        Assert.Equal("Hi, Kyle!\n", RunBody(Greet + "<echo>{ @greet(greeting: 'Hi', who: 'Kyle') }</echo>"));
        Assert.Equal("Hello, Kyle?\n", RunBody(Greet + "<echo>{ @greet('Kyle', end: '?') }</echo>"));
        Assert.Equal("Hello, Kyle?\n", RunBody(Greet + "<echo>{ 'Kyle' |> @greet(end: '?') }</echo>"));
    }

    [Fact]
    public void LambdasTakeNamedArguments()
    {
        Assert.Equal("9\n", RunBody("<let name=\"sub\" value=\"fn($a, $b) => $a - $b\" /><echo>{ $sub->call(b: 1, a: 10) }</echo>"));
    }

    [Theory]
    [InlineData("@known_folder(nme: 'home')", "no parameter named 'nme'. Did you mean name?")]
    [InlineData("@known_folder('home', name: 'temp')", "given 'name' twice")]
    [InlineData("@range(end: 3)", "needs a value for 'start'")]
    [InlineData("'x'->pad_left(wdth: 3)", "Did you mean width?")]
    public void CommandAndMethodMistakesAreExplained(string expression, string expected)
    {
        KScriptException ex = Fails($"<echo>{{ {expression} }}</echo>", "KScript.IO");
        Assert.Contains(expected, ex.Message);
        Assert.Equal("Argument", ex.Type);
    }

    [Theory]
    [InlineData("@greet(greting: 'Hi', who: 'K')", "no parameter named 'greting'. Did you mean greeting?")]
    [InlineData("@greet('K', who: 'J')", "given 'who' twice")]
    [InlineData("@greet(greeting: 'Hi')", "needs a value for 'who'")]
    [InlineData("@greet(who: 'K', 'Hi')", "Positional arguments must come before named ones")]
    public void FunctionMistakesAreExplained(string call, string expected)
    {
        KScriptException ex = Fails(Greet + $"<echo>{{ {call} }}</echo>");
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void KnownFoldersAreAbsolute()
    {
        foreach (string name in new[] { "home", "desktop", "documents", "downloads", "music", "pictures", "videos", "temp" })
        {
            Assert.True(Path.IsPathRooted(Eval($"@known_folder(name: '{name}')", "KScript.IO")), name);
        }
    }
}
