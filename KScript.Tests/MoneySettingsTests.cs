using KScript.Runtime;

namespace KScript.Tests;

public sealed class MoneySettingsTests : IDisposable
{
    private readonly string project = Directory.CreateTempSubdirectory("kscript-money-settings-").FullName;

    public MoneySettingsTests()
    {
        Directory.CreateDirectory(Path.Combine(project, "scripts"));
        Directory.CreateDirectory(Path.Combine(project, "settings"));
        File.WriteAllText(Path.Combine(project, "kscript.json"), """{"config":{"root":"settings","environment":"stage"}}""");
    }

    public void Dispose() => Directory.Delete(project, recursive: true);

    private void Settings(string file, string text) => File.WriteAllText(Path.Combine(project, "settings", file), text);

    private string Run(string script) => ScriptRunner.Run(script, path: Path.Combine(project, "scripts", "main.kscript"));

    [Fact]
    public void NestedScriptUsesLayeredProjectRatesAndPerRunOverrides()
    {
        Settings("config.yaml", "money:\n  exchange_rates:\n    EUR:\n      GBP: 0.8\n    USD:\n      GBP: 0.5\n");
        Settings("config.stage.json", """{"money":{"exchange_rates":{"eur":{"gbp":0.9}}}}""");
        string add = "echo GBP1 + EUR1 + USD1";
        Assert.Equal("GBP 2.40\nGBP 1.75\n", Run(add + "\n@set_exchange_rate(to: 'GBP', rate: 0.25, from: 'EUR')\n" + add));
        Assert.Equal("GBP 2.40\n", Run(add));
    }

    [Theory]
    [InlineData("[]", "money.exchange_rates")]
    [InlineData("""{"EUR":0.80}""", "money.exchange_rates.EUR")]
    [InlineData("""{"ZZZ":{"GBP":1}}""", "money.exchange_rates.ZZZ")]
    [InlineData("""{"EUR":{"ZZZ":1}}""", "money.exchange_rates.EUR.ZZZ")]
    [InlineData("""{"EUR":{"GBP":0}}""", "money.exchange_rates.EUR.GBP")]
    [InlineData("""{"EUR":{"GBP":"0.80"}}""", "money.exchange_rates.EUR.GBP")]
    [InlineData("""{"EUR":{"EUR":2}}""", "money.exchange_rates.EUR.EUR")]
    public void InvalidProjectRatesIdentifyTheSetting(string table, string path)
    {
        Settings("config.json", """{"money":{"exchange_rates":""" + table + "}}");
        var error = Assert.Throws<KScriptException>(() => Run("echo $config.money.exchange_rates"));
        Assert.Equal("Config", error.Type);
        Assert.Contains(path, error.Message);
    }

    [Fact]
    public void InvalidReplacementLeavesThePreviousRateAvailable()
    {
        Assert.Equal("GBP 1.80\n", Run("""
            @set_exchange_rate('EUR', 'GBP', 0.80)
            try {
              @set_exchange_rate('EUR', 'GBP', -1)
            } catch Argument {
              echo GBP1 + EUR1
            }
            """));
    }

    [Fact]
    public void ASeparatelyLoadedConfigCanUseItsOwnMoneySchema()
    {
        File.WriteAllText(Path.Combine(project, "scripts", "partner.json"), """{"money":{"exchange_rates":"available on request"}}""");
        Assert.Equal("available on request\n", Run("""
            config(files: ['partner.json'], into: partner)
            echo $partner.money.exchange_rates
            """));
    }
}
