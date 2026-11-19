namespace KScript.Tests;

public class SmokeTests
{
    [Fact]
    public void Echo_PrintsText()
    {
        Assert.Equal("Hello\n", ScriptRunner.RunBody("<echo>Hello</echo>"));
    }
}
