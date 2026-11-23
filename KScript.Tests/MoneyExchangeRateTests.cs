using KScript.Runtime;

namespace KScript.Tests;

public class MoneyExchangeRateTests
{
    private static string Run(string source, string directory = null) => ScriptRunner.Run(source,
        path: Path.Combine(directory ?? Path.GetTempPath(), "money-exchange.kscript")).TrimEnd('\n');

    [Theory]
    [InlineData(6e-29)]
    [InlineData(1.25e-28)]
    public void AnUnrepresentableRateIsRejectedInsteadOfChangingItsQuote(double rate)
    {
        Assert.Equal("Argument", Assert.Throws<KScriptException>(() => MoneyType.RequireRate(rate)).Type);
    }

    [Fact]
    public void TheSmallestExactDecimalRateCanBeUsed()
    {
        const string code = """
            balance: money = 'GBP 0'
            incoming: money = 'USD 10000000000000000000000000000'
            echo $balance->add($incoming, rate: 0.0000000000000000000000000001)
            """;
        Assert.Equal("GBP 1.00", Run(code));
    }

    [Theory]
    [InlineData("echo GBP999999999999999999999999999.99", "Syntax")]
    [InlineData("balance: money = 'GBP 999999999999999999999999999.99'", "Type")]
    [InlineData("echo GBP1000000000000000000000000000->add(EUR0.01, rate: 1)", "Math")]
    public void MoneyRefusesToSilentlyDropMinorUnits(string code, string errorType)
    {
        Assert.Equal(errorType, Assert.Throws<KScriptException>(() => Run(code)).Type);
    }

    [Fact]
    public void ConversionRoundsTheExactProductOnce()
    {
        const string code = "echo GBP0->add(EUR1000000000000000000000000001, rate: 0.004999999999999999)";
        Assert.Equal("GBP 4999999999999999000000000.00", Run(code));
    }

    [Fact]
    public void AConvertedDebitCanCancelALargeBalanceBeforeCheckingTheResultRange()
    {
        const string code = """
            balance: money = 'GBP 79228162514264337593543950335'
            debit: money = 'EUR -39614081257132168796771975168'
            echo $balance->add($debit, rate: 2)
            """;
        Assert.Equal("GBP -1.00", Run(code));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{ EUR: '0.80' }")]
    [InlineData("{ EUR: false }")]
    [InlineData("{ EUR: 0 }")]
    [InlineData("{ EUR: -1 }")]
    [InlineData("{ XYZ: 1 }")]
    [InlineData("{ EUR: 1, eur: 2 }")]
    public void InvalidExchangeTablesFailWhenBindingTheMoneyValue(string table)
    {
        var error = Assert.Throws<KScriptException>(() => Run($"amount: money(rates: {table}) = 'GBP 1'\necho $amount"));
        Assert.Equal("Type", error.Type);
        Assert.Contains("rate", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("USD 1", "0")]
    [InlineData("USD 1", "-0.5")]
    [InlineData("GBP 2", "1.5")]
    public void AnInvalidInlineRateFailsEvenWithAUsableStoredRate(string incoming, string rate)
    {
        string code = $"balance: money(rates: {{ USD: 0.75 }}) = 'GBP 1'\nother: money = '{incoming}'\necho $balance->add($other, rate: {rate})";
        Assert.Equal("Argument", Assert.Throws<KScriptException>(() => Run(code)).Type);
    }

    [Theory]
    [InlineData("$balance - $euro")]
    [InlineData("$balance / $euro")]
    [InlineData("$balance > $euro")]
    public void StoredRatesDoNotImplicitlyConvertOtherOperators(string expression)
    {
        string code = $"balance: money(rates: {{ EUR: 0.8 }}) = 'GBP 1'\neuro: money = 'EUR 1'\necho {expression}";
        Assert.Equal("Type", Assert.Throws<KScriptException>(() => Run(code)).Type);
    }

    [Fact]
    public void ConfiguringABalanceSnapshotsTheRateTable()
    {
        var money = new MoneyType();
        var pounds = Assert.IsType<NativeText>(money.Coerce("GBP 1"));
        var euro = money.Coerce("EUR 1");
        var quotes = new Dictionary<string, object> { ["EUR"] = 0.8d };
        var morning = MoneyType.WithRates(pounds, quotes);
        quotes["EUR"] = 0.6d;
        var afternoon = MoneyType.WithRates(pounds, quotes);
        Assert.True(NativeTypeRegistry.TryBinary("+", morning, euro, out var earlier));
        Assert.True(NativeTypeRegistry.TryBinary("+", afternoon, euro, out var later));
        Assert.Equal("GBP 1.80", Values.ToStr(earlier));
        Assert.Equal("GBP 1.60", Values.ToStr(later));
    }

    [Fact]
    public void ARefinedMoneyTypeCanDeclareRatesForMultipleCurrencies()
    {
        const string code = """
            type InvoiceBalance with [money(rates: { eur: 0.8, USD: 0.75 })] {}
            balance: InvoiceBalance = 'GBP 1'
            euro: money = 'EUR 2'
            dollar: money = 'USD 1'
            echo $balance + $euro + $dollar
            """;
        Assert.Equal("GBP 3.35", Run(code));
    }

    [Fact]
    public void ProjectDefaultsAndExplicitTablesSurvivePassingAnExistingValueToAFunction()
    {
        string project = Directory.CreateTempSubdirectory("kscript-money-rates-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(project, "kscript.json"), """{"types":{"money":{"rates":{"EUR":0.80,"USD":0.75}}}}""");
            const string declarations = """
                fn credit(balance: money, amount: money) => $balance->add($amount)
                standard: money = 'GBP 1'
                override_quote: money(rates: { EUR: 0.5 }) = 'GBP 1'
                unquoted: money(rates: {}) = 'GBP 1'
                """;
            string RunWith(string statement) => Run(declarations + "\n" + statement, project);
            Assert.Equal("GBP 1.80", RunWith("echo @credit($standard, 'EUR 1')"));
            Assert.Equal("GBP 1.75", RunWith("echo @credit($standard, 'USD 1')"));
            Assert.Equal("GBP 1.50", RunWith("echo @credit($override_quote, 'EUR 1')"));
            Assert.Equal("Type", Assert.Throws<KScriptException>(() => RunWith("echo @credit($override_quote, 'USD 1')")).Type);
            Assert.Equal("Type", Assert.Throws<KScriptException>(() => RunWith("echo @credit($unquoted, 'EUR 1')")).Type);
        }
        finally { Directory.Delete(project, recursive: true); }
    }
}
