using KScript.KScriptExceptions;

namespace KScript.Tests;

public class ControlFlowTests
{
    [Theory]
    [InlineData("yes", "A\n")]
    [InlineData("no", "")]
    [InlineData("@equals('a','a')", "A\n")]
    public void If_RunsWhenTrue(string condition, string expected)
    {
        Assert.Equal(expected, ScriptRunner.RunBody($"<if condition=\"{condition}\"><echo>A</echo></if>"));
    }

    [Fact]
    public void If_CheckforNo_RunsWhenFalse()
    {
        Assert.Equal("ran\n", ScriptRunner.RunBody("<if condition=\"no\" checkfor=\"no\"><echo>ran</echo></if>"));
    }

    [Fact]
    public void If_ConditionIsEvaluatedOnce()
    {
        // The condition was evaluated by both the element and its handler, repeating side effects such as input.
        string output = ScriptRunner.RunBody(
            "<def id=\"n\">0</def><if condition=\"@equals(@math('$n'),'0')\"><update to=\"n\">@math('$n+1')</update></if><echo>$n</echo>");
        Assert.Equal("1\n", output);
    }

    [Theory]
    [InlineData("no", "B\n")]
    [InlineData("yes", "A\n")]
    public void Else_RunsWhenPrecedingIfWasFalse(string condition, string expected)
    {
        Assert.Equal(expected, ScriptRunner.RunBody($"<if condition=\"{condition}\"><echo>A</echo></if><else><echo>B</echo></else>"));
    }

    [Fact]
    public void Else_WithoutIf_Throws()
    {
        Assert.Throws<KScriptException>(() => ScriptRunner.RunBody("<else><echo>B</echo></else>"));
    }

    [Fact]
    public void For_LoopsWhileConditionHolds()
    {
        Assert.Equal("0\n1\n2\n", ScriptRunner.RunBody(
            "<for to=\"i\" while=\"@more_less($i,'lt','3')\" math=\"@math('$i+1')\"><echo>$i</echo></for>"));
    }

    [Fact]
    public void For_DoesNotRunWhenConditionIsInitiallyFalse()
    {
        Assert.Equal("done\n", ScriptRunner.RunBody(
            "<for to=\"i\" while=\"@more_less($i,'lt','0')\" math=\"@math('$i+1')\"><echo>never</echo></for><echo>done</echo>"));
    }

    [Fact]
    public void For_RestoresLoopVariable()
    {
        Assert.Equal("9\n", ScriptRunner.RunBody(
            "<def id=\"i\">9</def><for to=\"i\" while=\"@more_less($i,'lt','12')\" math=\"@math('$i+1')\"></for><echo>$i</echo>"));
    }

    [Fact]
    public void Break_InsideIf_StopsLoop()
    {
        Assert.Equal("0\n1\n", ScriptRunner.RunBody(
            "<for to=\"i\" while=\"@more_less($i,'lt','5')\" math=\"@math('$i+1')\"><if condition=\"@equals($i,'2')\"><break/></if><echo>$i</echo></for>"));
    }

    [Fact]
    public void Break_InsideNestedIfs_StopsLoop()
    {
        // The outer <if> used to reset the break flag, so the loop carried on.
        Assert.Equal("0\n1\n", ScriptRunner.RunBody(
            "<for to=\"i\" while=\"@more_less($i,'lt','5')\" math=\"@math('$i+1')\">" +
            "<if condition=\"yes\"><if condition=\"@equals($i,'2')\"><break/></if></if><echo>$i</echo></for>"));
    }

    [Fact]
    public void Break_OnlyStopsInnermostLoop()
    {
        Assert.Equal("a0\na1\n", ScriptRunner.RunBody(
            "<for to=\"i\" while=\"@more_less($i,'lt','2')\" math=\"@math('$i+1')\">" +
            "<for to=\"j\" while=\"@more_less($j,'lt','5')\" math=\"@math('$j+1')\"><break/><echo>never</echo></for>" +
            "<echo>a$i</echo></for>"));
    }

    [Fact]
    public void Continue_SkipsRestOfIteration()
    {
        Assert.Equal("0\n2\n3\n", ScriptRunner.RunBody(
            "<for to=\"i\" while=\"@more_less($i,'lt','4')\" math=\"@math('$i+1')\"><if condition=\"@equals($i,'1')\"><continue/></if><echo>$i</echo></for>"));
    }

    [Fact]
    public void DoWhile_Loops()
    {
        Assert.Equal("0\n1\n2\n", ScriptRunner.RunBody(
            "<def id=\"n\">0</def><dowhile condition=\"@more_less($n,'lt','3')\"><echo>$n</echo><update to=\"n\">@math('$n+1')</update></dowhile>"));
    }

    [Fact]
    public void Forever_StopsOnBreak()
    {
        Assert.Equal("3\n", ScriptRunner.RunBody(
            "<def id=\"n\">0</def><forever><update to=\"n\">@math('$n+1')</update><if condition=\"@equals($n,'3')\"><break/></if></forever><echo>$n</echo>"));
    }

    [Fact]
    public void Forever_StopsOnEnd()
    {
        // <end/> stopped child objects from running but the loop itself spun forever.
        var task = Task.Run(() => ScriptRunner.RunBody("<forever><echo>once</echo><end/></forever><echo>never</echo>"));
        Assert.True(task.Wait(TimeSpan.FromSeconds(10)), "forever loop did not stop after <end/>");
        Assert.Equal("once\n", task.Result);
    }

    [Fact]
    public void End_StopsScript()
    {
        Assert.Equal("", ScriptRunner.RunBody("<end/><echo>should not print</echo>"));
    }

    [Fact]
    public void Afor_IteratesArray()
    {
        Assert.Equal("a\nb\nc\n", ScriptRunner.RunBody("<array id=\"arr\">a,b,c</array><afor from=\"arr\" to=\"item\"><echo>$item</echo></afor>"));
    }

    [Fact]
    public void Array_IndexCommand()
    {
        Assert.Equal("b\n", ScriptRunner.RunBody("<array id=\"arr\">a,b,c</array><echo>@array('arr','1')</echo>"));
    }

    [Fact]
    public void Method_IsOnlyRunWhenCalled()
    {
        // <method> bodies used to run inline at declaration because the handler matched the wrong type.
        Assert.Equal("before\nHi Bob\n", ScriptRunner.RunBody(
            "<method name=\"greet\" params=\"who\"><echo>Hi $greet.who</echo></method><echo>before</echo><call method=\"greet\" args=\"Bob\"/>"));
    }

    [Fact]
    public void Call_BindsArgumentsInDeclaredOrder()
    {
        Assert.Equal("Hello, World!\n", ScriptRunner.RunBody(
            "<method name=\"greet\" params=\"who,greeting\"><echo>$greet.greeting, $greet.who!</echo></method>" +
            "<call method=\"greet\" args=\"'World', 'Hello'\"/>"));
    }

    [Fact]
    public void Call_ArgumentsMayContainCommasInQuotesAndCommands()
    {
        Assert.Equal("[a, b] [x]\n", ScriptRunner.RunBody(
            "<method name=\"show\" params=\"first,second\"><echo>[$show.first] [$show.second]</echo></method>" +
            "<call method=\"show\" args=\"'a, b', @to_lower('X')\"/>"));
    }

    [Fact]
    public void Call_DoesNotBindParametersOfMethodsWithSimilarNames()
    {
        Assert.Equal("[1]\n", ScriptRunner.RunBody(
            "<method name=\"greeting\" params=\"x\"><echo>never</echo></method>" +
            "<method name=\"greet\" params=\"y\"><echo>[$greet.y]</echo></method>" +
            "<call method=\"greet\" args=\"1\"/>"));
    }

    [Fact]
    public void Call_TooManyArguments_Throws()
    {
        Assert.Throws<KScriptException>(() => ScriptRunner.RunBody(
            "<method name=\"m\" params=\"a\"><echo>$m.a</echo></method><call method=\"m\" args=\"1,2\"/>"));
    }

    [Fact]
    public void MultiArray_LookupInLoop()
    {
        const string script = @"
<marray id=""people"">
  <item><item key=""age"">28</item><item key=""name"">Joseph</item></item>
  <item><item key=""age"">82</item><item key=""name"">Joan</item></item>
  <item><item key=""age"">12</item><item key=""name"">Ellie</item></item>
</marray>
<def id=""max"">0</def>
<def id=""who"">0</def>
<for to=""i"" math=""@math('$i+1')"" while=""@more_less($i,'lt',@acount('~people'))"">
  <if condition=""@more_less(~people[$i]['age'],'mt',$max)"">
    <update to=""max"">~people[$i]['age']</update>
    <update to=""who"">$i</update>
  </if>
</for>
<echo>~people[$who]['name'] is $max</echo>";
        Assert.Equal("Joan is 82\n", ScriptRunner.RunBody(script));
    }
}
