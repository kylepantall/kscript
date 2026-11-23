using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Syntax;

using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests;

public class MoneyLiteralTests
{
    [Fact]
    public void CompactMoneyInfersItsTypeAndHighlightsTheWholeAmount()
    {
        var analysis = Analyze("price = GBP1.0 + GBP0.25\necho $price.currency\necho JPY5", "money.kscript");
        AssertClean(analysis);
        Assert.Equal("money", Assert.Single(analysis.Symbols, s => s.Name == "price").Shape.Type);
        var tokens = new LanguageService().GetSemanticTokens(analysis);
        Assert.Equal(new[] { "GBP1.0", "GBP0.25", "JPY5" }, tokens.Where(t => t.Type == SemanticTokenType.Number).Select(t => analysis.Source[t.Span]));
    }

    [Theory]
    [InlineData("GBP0.001", "at most 2")]
    [InlineData("JPY1.0", "whole numbers")]
    public void InvalidPrecisionIsReportedAtTheWholeLiteral(string amount, string message)
    {
        var analysis = Analyze("price = " + amount, "money.kscript");
        var diagnostic = AssertSingle(analysis, DiagnosticCodes.InvalidTypedValue);
        Assert.Equal(amount, analysis.Source[diagnostic.Span]);
        Assert.Contains(message, diagnostic.Message);
    }

    [Theory]
    [InlineData("XYZ2")]
    [InlineData("USDPrice")]
    [InlineData("Model2")]
    [InlineData("gbp1")]
    public void NamesOutsideTheCompactFormRemainIdentifiers(string name)
    {
        var analysis = Analyze("echo " + name, "money.kscript");
        Assert.Contains(analysis.Diagnostics, d => d.Message.Contains(name));
        Assert.DoesNotContain(new LanguageService().GetSemanticTokens(analysis), t => t.Type == SemanticTokenType.Number);
    }

    [Fact]
    public void DeclaredCurrencyLikeNamesRemainUsable()
    {
        var analysis = Analyze("type GBP1 { title: string }\nentry = GBP1(title: 'Invoice')\nUSD2 = 7\necho $entry.title\necho $USD2", "money.kscript");
        var naming = Assert.Single(analysis.Diagnostics);
        Assert.Equal(DiagnosticCodes.NamingStyle, naming.Code);
        Assert.Equal("USD2", analysis.Source[naming.Span]);
    }
}
