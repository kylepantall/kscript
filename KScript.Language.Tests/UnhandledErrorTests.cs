using KScript.Language.Analysis;
using KScript.Syntax;

namespace KScript.Language.Tests;

public class UnhandledErrorTests
{
    private static Diagnostic[] Warnings(string source, string path = "entry.kscript") =>
        Analyzer.Analyze(source, path, EmptyFileSystem.Instance).Diagnostics
            .Where(d => d.Code == DiagnosticCodes.UnhandledError).ToArray();

    [Fact]
    public void OnlyTheExecutingCallGetsTheWarning()
    {
        const string source = """
            fn fetch() { throw Network "offline" }
            fn forward() => @fetch()
            fn unused() { throw Validation "bad" }
            echo @forward()
            """;
        var warning = Assert.Single(Warnings(source));
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal("forward", source.Substring(warning.Span.Start, warning.Span.Length));
        Assert.Contains("Network", warning.Message);
        Assert.DoesNotContain("Validation", warning.Message);
    }

    [Fact]
    public void CatchesFilterTypesAtTheScriptBoundary()
    {
        const string source = """
            error Offline extends Network {}
            fn fetch(valid: bool) {
              if $valid { throw Offline "connection lost" }
              throw Validation "invalid"
            }
            try { @fetch(true) } catch Network {}
            try { @fetch(false) } catch Error {}
            """;
        var warning = Assert.Single(Warnings(source));
        Assert.Contains("Validation", warning.Message);
        Assert.DoesNotContain("Offline", warning.Message);
        Assert.Equal(source.IndexOf("try", StringComparison.Ordinal), warning.Span.Start);
    }

    [Theory]
    [InlineData("catch Network {}")]
    [InlineData("catch Network { return null }")]
    [InlineData("catch Network { echo 'logging failure' }")]
    public void AnUnrecoveredDeclarationErrorReachesTheCaller(string clause)
    {
        string source = $"fn fetch() {{ throw Network \"offline\" }} {clause}\necho @fetch()\n";
        Assert.Contains("Network", Assert.Single(Warnings(source)).Message);
        Assert.Empty(Warnings(source.Replace("echo @fetch()", "try { @fetch() } catch Network {}")));
    }

    [Fact]
    public void TestsAndExpectedFailuresHaveTheirOwnBoundaries()
    {
        const string source = """
            import KScript.Test
            fn fetch() { throw Network "offline" }
            test(name: 'test runner handles failures') { @fetch() }
            expect_error(type: Network) { @fetch() }
            expect(@fetch(), throws: Network)
            """;
        Assert.Empty(Warnings(source));
    }

    [Fact]
    public void DynamicCallbackInvocationsCarryNoInferredContract()
    {
        Assert.Empty(Warnings("""
            fn fetch() { throw Network "offline" }
            callback = @fetch
            echo $callback()
            """));
    }

    [Fact]
    public void XmlInvocationsAndScriptsWithoutFunctionsAreCovered()
    {
        const string xml = """
            <kscript>
              <fn name="fetch"><throw type="Network">offline</throw></fn>
              <call fn="fetch" />
            </kscript>
            """;
        var warning = Assert.Single(Warnings(xml, "entry.ks.xml"));
        Assert.Equal("fetch", xml.Substring(warning.Span.Start, warning.Span.Length));
        Assert.Contains("Network", warning.Message);
        Assert.Contains("Network", Assert.Single(Warnings("throw Network \"failed\"")).Message);
    }
}
