using KScript.Language.Analysis;
using KScript.Syntax;
using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests;

public class DuplicateLiteralPatternTests
{
    [Fact]
    public void EqualPatternsWarnBeforeEitherTypeIsUsed()
    {
        var analysis = Analyze("""
            type First with [string] { pattern: /key_[a-z]+/im }
            type Second with [string] { pattern: /key_[a-z]+/mi }
            type Different with [string] { pattern: /key_[a-z]+/ }
            chosen: First = key_one
            """, "types.kscript");

        var warning = AssertSingle(analysis, DiagnosticCodes.DuplicateLiteralPattern);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal("/key_[a-z]+/mi", analysis.Source[warning.Span]);
        Assert.Contains("First, Second", warning.Message);
        Assert.DoesNotContain("Different", warning.Message);
        Assert.DoesNotContain(analysis.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void TheImportThatBringsTwoEqualPatternsTogetherGetsTheWarning()
    {
        string directory = Path.Combine(Path.GetTempPath(), "kscript-colliding-imports");
        var files = new MemoryFiles()
            .Add(Path.Combine(directory, "alpha.kscript"), "type Alpha with [string] { pattern: /key_[a-z]+/ }")
            .Add(Path.Combine(directory, "beta.kscript"), "type Beta with [string] { pattern: /key_[a-z]+/ }");
        string path = Path.Combine(directory, "main.kscript");
        const string imports = "import \"alpha.kscript\"\nimport \"beta.kscript\"";
        var both = Analyze(imports, path, files);

        var warning = AssertSingle(both, DiagnosticCodes.DuplicateLiteralPattern);
        Assert.Equal(1, both.Source.GetPosition(warning.Span.Start).Line);
        Assert.Contains("Alpha, Beta", warning.Message);

        var qualified = Analyze(imports + " as lib", path, files);
        Assert.DoesNotContain(qualified.Diagnostics, d => d.Code == DiagnosticCodes.DuplicateLiteralPattern);
    }

    [Fact]
    public void AnInheritedPatternDoesNotDeclareAnotherLiteralSpelling()
    {
        var analysis = Analyze("""
            type Key with [string] { pattern: /key_[a-z]+/ }
            type ShortKey with [Key] { max: 12 }
            chosen = key_one
            """, "types.kscript");

        Assert.DoesNotContain(analysis.Diagnostics, d => d.Code == DiagnosticCodes.DuplicateLiteralPattern || d.Severity == DiagnosticSeverity.Error);
    }
}
