using KScript.Runtime;

namespace KScript.Tests;

public class CompactMoneyLiteralTests
{
    [Theory]
    [InlineData("GBP0.001", "at most 2 decimal places")]
    [InlineData("JPY1.00", "whole numbers")]
    [InlineData("USD99999999999999999999999999999999", "too large")]
    public void CompactMoneyUsesTheNativePrecisionAndRangeChecks(string literal, string reason)
    {
        var error = Assert.Throws<KScriptException>(() => ScriptRunner.Run("price = " + literal, path: "price.kscript"));
        Assert.Equal("Syntax", error.Type);
        Assert.Contains(reason, error.Message);
    }
}
