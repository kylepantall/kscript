using KScript.Runtime;

using static KScript.Tests.ScriptRunner;

namespace KScript.Tests;

/// <summary>The KScript.Core elements: variables, control flow, functions, errors and output.</summary>
public class CoreTests
{
    [Fact]
    public void EchoPrintsInterpolatedText()
    {
        Assert.Equal("Hello, Kyle! 3\n", RunBody("<let name=\"who\" value=\"'Kyle'\" /><echo>Hello, {$who}! { 1 + 2 }</echo>"));
    }

    [Fact]
    public void EchoReplacesBareVariables()
    {
        Assert.Equal("Hi Kyle and Joan\n", RunBody("<let name=\"a\" value=\"'Kyle'\" /><let name=\"p\" value=\"{ name: 'Joan' }\" /><echo>Hi $a and $p.name</echo>"));
    }

    [Fact]
    public void EchoValueAndJson()
    {
        Assert.Equal("[1, 2]\n", RunBody("<echo value=\"[1, 2]\" format=\"json\" />"));
        Assert.Equal("5\n", RunBody("<echo value=\"2 + 3\" />"));
    }

    [Fact]
    public void EchoRemovesSharedIndentation()
    {
        Assert.Equal("a\n  b\nc\n", RunBody("<echo>a\n      b\n    c\n  </echo>"));
    }

    [Fact]
    public void LetAndSet()
    {
        Assert.Equal("10\n", RunBody("<let name=\"total\" value=\"0\" /><set name=\"total\" value=\"$total + 4\" /><set name=\"total\" op=\"+=\" value=\"6\" /><echo>$total</echo>"));
    }

    [Fact]
    public void LetWithTextValue()
    {
        Assert.Equal("Joan\n", RunBody("<let name=\"people\">[{ name: 'Joan', age: 82 }]</let><echo>{$people[0].name}</echo>"));
    }

    [Fact]
    public void SetOfAnUndeclaredVariableFails()
    {
        KScriptException ex = Fails("<let name=\"count\" value=\"0\" /><set name=\"cont\" value=\"1\" />");
        Assert.Contains("count", ex.Message);
    }

    [Fact]
    public void ConstCantBeChanged()
    {
        KScriptException ex = Fails("<let name=\"x\" value=\"1\" const=\"yes\" /><set name=\"x\" value=\"2\" />");
        Assert.Contains("const", ex.Message);
    }

    [Fact]
    public void LetTypeConverts()
    {
        Assert.Equal("number 42\n", RunBody("<let name=\"n\" value=\"'42'\" type=\"int\" /><echo>{ @type($n) } $n</echo>"));
        Assert.Throws<KScriptException>(() => RunBody("<let name=\"n\" value=\"'abc'\" type=\"int\" />"));
    }

    [Fact]
    public void DoRunsStatements()
    {
        Assert.Equal("7\n", RunBody("<let name=\"x\" value=\"0\" /><do>$x = 3; $x += 4</do><echo>$x</echo>"));
    }

    [Fact]
    public void VariablesAreBlockScoped()
    {
        KScriptException ex = Fails("<if test=\"true\"><let name=\"inner\" value=\"1\" /></if><echo>$inner</echo>");
        Assert.Contains("inner", ex.Message);
    }

    [Theory]
    [InlineData("1", "one\n")]
    [InlineData("2", "two\n")]
    [InlineData("3", "other\n")]
    public void IfElifElse(string n, string expected)
    {
        Assert.Equal(expected, RunBody($"<let name=\"n\" value=\"{n}\" /><if test=\"$n eq 1\"><echo>one</echo></if><elif test=\"$n == 2\"><echo>two</echo></elif><else><echo>other</echo></else>"));
    }

    [Fact]
    public void ForEachOverListsRangesAndMaps()
    {
        Assert.Equal("a b \n", RunBody("<let name=\"out\" value=\"''\" /><for each=\"x\" in=\"['a', 'b']\"><set name=\"out\" value=\"$out + $x + ' '\" /></for><echo value=\"$out\" />"));
        Assert.Equal("0,5\n", RunBody("<echo>{ (0..10 step 5)->join(',') }</echo>"));
        Assert.Equal("a=1\nb=2\n", RunBody("<for each=\"k, v\" in=\"{ a: 1, b: 2 }\"><echo>$k=$v</echo></for>"));
    }

    [Fact]
    public void ForWithIndex()
    {
        Assert.Equal("0:a\n1:b\n", RunBody("<for each=\"x\" at=\"i\" in=\"['a', 'b']\"><echo>$i:$x</echo></for>"));
    }

    [Fact]
    public void WhileBreakAndContinue()
    {
        Assert.Equal("1\n3\n", RunBody("<let name=\"i\" value=\"0\" /><while test=\"true\"><set name=\"i\" op=\"+=\" value=\"1\" /><if test=\"$i eq 2\"><continue /></if><if test=\"$i gt 3\"><break /></if><echo>$i</echo></while>"));
    }

    [Fact]
    public void MatchCases()
    {
        const string body = "<match value=\"$n\"><case is=\"1\"><echo>one</echo></case><case is=\"2, 3\"><echo>few</echo></case><case when=\"$it gt 9\"><echo>big</echo></case><default><echo>other</echo></default></match>";
        Assert.Equal("one\n", RunBody("<let name=\"n\" value=\"1\" />" + body));
        Assert.Equal("few\n", RunBody("<let name=\"n\" value=\"3\" />" + body));
        Assert.Equal("big\n", RunBody("<let name=\"n\" value=\"12\" />" + body));
        Assert.Equal("other\n", RunBody("<let name=\"n\" value=\"5\" />" + body));
    }

    [Fact]
    public void FunctionsWithDefaultsAndReturn()
    {
        const string fn = "<fn name=\"greet\" params=\"who, greeting = 'Hello'\"><return value=\"$greeting + ', ' + $who + '!'\" /></fn>";
        Assert.Equal("Hello, World!\nHi, Kyle!\n", RunBody(fn + "<echo>@greet('World')</echo><echo>{ @greet('Kyle', greeting: 'Hi') }</echo>"));
    }

    [Fact]
    public void FunctionsCanBeCalledBeforeTheyAreDeclared()
    {
        Assert.Equal("4\n", RunBody("<echo>{ @square(2) }</echo><fn name=\"square\" params=\"n\" value=\"$n * $n\" />"));
    }

    [Fact]
    public void RecursionAndPipes()
    {
        Assert.Equal("120\n", RunBody("<fn name=\"fact\" params=\"n: int\" value=\"$n le 1 ? 1 : $n * @fact($n - 1)\" /><echo>{ 5 |> @fact() }</echo>"));
    }

    [Fact]
    public void DeepRecursionIsAnError()
    {
        KScriptException ex = Fails("<fn name=\"f\" params=\"n\" value=\"@f($n + 1)\" /><echo>{ @f(0) }</echo>");
        Assert.Contains("recursion", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeepRecursionWorksFromASmallStack()
    {
        // macOS gives secondary threads 512 KB; the interpreter must not depend on its caller's stack.
        string output = null;
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try { output = RunBody("<fn name=\"count\" params=\"n\" value=\"$n le 0 ? 0 : 1 + @count($n - 1)\" /><echo>{ @count(900) }</echo>"); }
            catch (Exception ex) { failure = ex; }
        }, 512 * 1024);
        thread.Start();
        thread.Join();
        Assert.Null(failure);
        Assert.Equal("900\n", output);
    }

    [Fact]
    public void CallRunsAFunctionForItsEffect()
    {
        Assert.Equal("hi Kyle\n", RunBody("<fn name=\"say\" params=\"who\"><echo>hi $who</echo></fn><call fn=\"say\" args=\"'Kyle'\" />"));
    }

    [Fact]
    public void LambdasAndListMethods()
    {
        Assert.Equal("2,4,6\n", RunBody("<echo>{ [1, 2, 3]->map(fn($x) => $x * 2)->join(',') }</echo>"));
        Assert.Equal("Joan\n", RunBody("<let name=\"people\" value=\"[{ name: 'Joseph', age: 28 }, { name: 'Joan', age: 82 }]\" /><echo>{ $people->max_by(fn($p) => $p.age).name }</echo>"));
        Assert.Equal("3\n", RunBody("<echo>{ [1, 2, 3, 4, 5] |> filter(fn($x) => $x gt 2) |> count() }</echo>"));
    }

    [Fact]
    public void TryCatchFinally()
    {
        const string body = "<try><throw type=\"Validation\" message=\"bad {1 + 1}\" /><catch as=\"err\" when=\"$err.type eq 'Other'\"><echo>other</echo></catch><catch as=\"err\"><echo>{$err.type}: {$err.message}</echo></catch><finally><echo>done</echo></finally></try>";
        Assert.Equal("Validation: bad 2\ndone\n", RunBody(body));
    }

    [Fact]
    public void ErrorsHaveALocation()
    {
        KScriptException ex = Assert.Throws<KScriptException>(() => Run("<kscript>\n  <echo>ok</echo>\n  <echo>{ $missing }</echo>\n</kscript>", path: "/tmp/test.ks.xml"));
        Assert.Equal(3, ex.Line);
        Assert.Contains("missing", ex.Message);
        Assert.StartsWith("test.ks.xml:3:", ex.ToString());
    }

    [Fact]
    public void UncaughtThrowStopsTheScript()
    {
        KScriptException ex = Fails("<throw type=\"Oops\" message=\"no\" /><echo>after</echo>");
        Assert.Equal("Oops", ex.Type);
    }

    [Fact]
    public void ExitReturnsTheCode()
    {
        var output = new StringWriter { NewLine = "\n" };
        int code = new Interpreter(KScriptHost.ForText(output)).RunText("<kscript><echo>a</echo><exit code=\"4\" /><echo>b</echo></kscript>");
        Assert.Equal(4, code);
        Assert.Equal("a\n", output.ToString());
    }

    [Fact]
    public void MarrayBuildsNestedValues()
    {
        Assert.Equal("1 x\n", RunBody("<collection name=\"cfg\"><item key=\"a\">1</item><item key=\"list\"><item>x</item></item></collection><echo>$cfg.a {$cfg.list[0]}</echo>"));
    }

    [Fact]
    public void SpawnRunsInTheBackground()
    {
        Assert.Equal("bg\n", RunBody("<fn name=\"work\"><echo>bg</echo></fn><spawn fn=\"work\" />"));
    }

    [Fact]
    public void ArgsAreAvailable()
    {
        Assert.Equal("2 b\n", Run("<kscript><echo>{ $args->count() } {$args[1]}</echo></kscript>", args: new[] { "a", "b" }));
    }

    [Fact]
    public void RetiredElementsExplainTheReplacement()
    {
        KScriptException ex = Assert.Throws<KScriptException>(() => Run("<kscript><def id=\"x\">1</def></kscript>"));
        Assert.Contains("<let", ex.Message);
    }

    [Fact]
    public void UnknownElementsAreFoundBeforeAnythingRuns()
    {
        var output = new StringWriter();
        Assert.Throws<KScriptException>(() => new Interpreter(KScriptHost.ForText(output)).RunText("<kscript><echo>first</echo><ecko>x</ecko></kscript>"));
        Assert.Equal(string.Empty, output.ToString());
    }

    [Fact]
    public void ScriptsHaveOneKScriptRoot()
    {
        Assert.Throws<KScriptException>(() => Run("<script><echo>x</echo></script>"));
        Assert.Throws<KScriptException>(() => Run("<kscript /><kscript />"));
    }

    [Fact]
    public void StepLimitStopsRunawayLoops()
    {
        Assert.Throws<KScriptException>(() => Run("<kscript><while test=\"true\"><do>$x = 1</do></while></kscript>", options: new InterpreterOptions { MaxSteps = 1000 }));
    }
}
