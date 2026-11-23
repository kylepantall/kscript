using KScript.Runtime;

namespace KScript.Tests;

public sealed class IntegerMathLibraryTests
{
    private const string Source = "\"integer-math/index.kscript\"";

    private static double Calculate(string import, string call, PackageRegistry packages)
    {
        var interpreter = new Interpreter(new KScriptHost(TextWriter.Null), packages,
            new InterpreterOptions { Timeout = TimeSpan.FromSeconds(5) });
        var module = interpreter.LoadText($"import {import} as integers\nanswer = @integers.{call}",
            ScriptRunner.RepoFile("libraries", "caller.kscript"));
        interpreter.Run(module);
        Assert.True(module.Globals.TryGet("answer", out object answer));
        return Assert.IsType<double>(answer);
    }

    [Theory]
    [InlineData("gcd(b: -84, a: 30)")]
    [InlineData("gcd(0, 0)")]
    [InlineData("gcd(9007199254740991, 0)")]
    [InlineData("gcd(12.9, -18.4)")]
    [InlineData("lcm(-84, 30)")]
    [InlineData("lcm(0, -9)")]
    [InlineData("lcm(9007199254740991, 9007199254740991)")]
    [InlineData("factorial(0)")]
    [InlineData("factorial(5)")]
    [InlineData("factorial(170)")]
    public void SourceRunsWithOnlyCoreAndMatchesNativeMath(string call)
    {
        double expected = Calculate("KScript.Math", call, PackageRegistry.Default);
        Assert.Equal(expected, Calculate(Source, call, new PackageRegistry()));
    }

    [Theory]
    [InlineData("gcd(9007199254740992, 1)")]
    [InlineData("gcd(12, 'NaN')")]
    [InlineData("lcm('Infinity', 0)")]
    [InlineData("lcm(9007199254740991, 2)")]
    [InlineData("factorial(-1)")]
    [InlineData("factorial(171)")]
    public void OutOfDomainRaisesArgument(string call)
    {
        var error = Assert.Throws<KScriptException>(() => Calculate(Source, call, new PackageRegistry()));
        Assert.Equal("Argument", error.Type);
    }
}
