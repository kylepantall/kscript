using KScript.Pages;
using KScript.Testing;

using static KScript.Tests.ScriptRunner;

namespace KScript.Tests;

/// <summary>Running <c>&lt;if $ready&gt;</c> and the other shorthand start tags, in scripts, pages and tests.</summary>
public class ShorthandConditionRunTests
{
    [Fact]
    public void ScriptsRunShorthandConditions()
    {
        string output = RunBody("""
            <let name="user" value="{ is_logged_in: true, name: 'Joan' }" />
            <let name="n" value="5" />
            <if $user.is_logged_in><echo>Hello {$user.name}</echo></if>
            <if $n > 10><echo>big</echo></if>
            <elif $n >= 3 and $n < 8><echo>medium</echo></elif>
            <else><echo>small</echo></else>
            <let name="i" value="0" />
            <while $i lt 3><set name="i" value="$i + 1" /></while>
            <echo>{$i}</echo>
            <match $n><case is="5"><echo>five</echo></case></match>
            <if $user.name == "Joan"><echo>quotes</echo></if>
            <if test="$n eq 5"><echo>long form</echo></if>
            """);
        Assert.Equal("Hello Joan\nmedium\n3\nfive\nquotes\nlong form\n", output);
    }

    [Fact]
    public void ScriptErrorsPointIntoTheShorthand()
    {
        var ex = Fails("<if $nope gt 1><echo>x</echo></if>");
        Assert.Contains("nope", ex.Message);
    }

    private static string Render(string template) => PageRenderer.RenderString(template).Body;

    [Fact]
    public void PagesRunShorthandConditions()
    {
        Assert.Equal("<ul><li><a href=\"/profile\">Profile</a></li></ul>",
            Render("<let name=\"user\" value=\"{ is_logged_in: true }\" /><ul><if $user.is_logged_in><li><a href=\"/profile\">Profile</a></li></if></ul>"));
        Assert.Equal("<p>mid</p>",
            Render("<let name=\"n\" value=\"5\" /><if $n > 10><p>big</p></if><elif $n >= 3><p>mid</p></elif><else><p>small</p></else>"));
        Assert.Equal("<b>2</b>",
            Render("<let name=\"n\" value=\"0\" /><while $n < 2><set name=\"n\" value=\"$n + 1\" /></while><b>{$n}</b>"));
        Assert.Equal("<i>two</i>",
            Render("<let name=\"c\" value=\"2\" /><match $c><case is=\"1\"><i>one</i></case><case is=\"2\"><i>two</i></case></match>"));
    }

    [Fact]
    public void PageShorthandsKeepTheirCase()
    {
        Assert.Equal("<p>yes</p>", Render("<let name=\"isReady\" value=\"true\" /><IF $isReady><p>yes</p></IF>"));
    }

    [Fact]
    public void AnUnclosedPageShorthandIsText()
    {
        Assert.Equal("a <if $x", Render("a <if $x"));
    }

    [Fact]
    public void APageIfWithNoConditionSaysHowToWriteOne()
    {
        var ex = Assert.Throws<PageException>(() => Render("<if><p>x</p></if>"));
        Assert.Contains("<if $count gt 0>", ex.Message);
    }

    [Fact]
    public void AssertTakesItsConditionInTheStartTag()
    {
        using var site = new TempSite();
        site.Write("example.test.ks.xml", """
            <kscript>
              <test name="passes"><let name="total" value="30" /><assert $total eq 30 /></test>
              <test name="fails"><let name="total" value="25" /><assert $total eq 30 /></test>
            </kscript>
            """);
        TestFileResult file = TestRunner.RunFile(Path.Combine(site.Root, "example.test.ks.xml"), new TestRunSettings { BaseDirectory = site.Root });
        Assert.Null(file.Error);
        Assert.Equal(TestOutcome.Passed, file.Tests[0].Outcome);
        Assert.Equal(TestOutcome.Failed, file.Tests[1].Outcome);
        Assert.Equal("Expected $total eq 30 to be true.\n  $total is 25", file.Tests[1].Message);
    }
}
