using KScript.Language.Features;

using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests;

public class TestHookScopeTests
{
    [Fact]
    public void ASetupDeclaredAfterItsTestsIsVisibleInNestedSetupTestAndTeardown()
    {
        const string code = """
            describe(name: 'outer') {
              describe(name: 'inner') {
                test(name: 'reads the prepared map') { echo $sample.key }
                before_each { sample = { key: $label } }
                after_each { echo $label }
              }
              before_each { label = 'ready' }
            }
            """;
        var analysis = Analyze(code, "hooks.test.kscript");
        AssertClean(analysis);
        var service = new LanguageService();
        var use = code.IndexOf("$sample.key", StringComparison.Ordinal);
        var definition = Assert.Single(service.GetDefinition(analysis, use + 1));
        Assert.Equal(code.IndexOf("sample = ", StringComparison.Ordinal), definition.Span.Start);
    }

    [Fact]
    public void TestSetupDoesNotBindNamesInSiblingGroupsOrWhileRegisteringATest()
    {
        const string code = """
            describe(name: 'first') {
              before_each { label = 'ready' }
              test(name: 'reads the label') { echo $label }
              echo $label
            }
            describe(name: 'second') {
              test(name: 'outside the fixture') { echo $label }
            }
            """;
        var analysis = Analyze(code, "groups.test.kscript");
        var diagnostics = analysis.Diagnostics.OrderBy(d => d.Span.Start).ToList();
        Assert.Equal(2, diagnostics.Count);
        Assert.All(diagnostics, d => Assert.Equal(DiagnosticCodes.UndefinedVariable, d.Code));
        Assert.True(diagnostics[0].Span.Start >= code.IndexOf("  echo $label\n", StringComparison.Ordinal));
        Assert.True(diagnostics[1].Span.Start > code.IndexOf("'outside the fixture'", StringComparison.Ordinal));
    }

    [Fact]
    public void ConditionallyRegisteredSetupIsOnlyGuaranteedAlongThatPath()
    {
        const string code = """
            enabled = $args->count() > 0
            if $enabled {
              before_each { label = 'ready' }
              test(name: 'registered together') { echo $label }
            }
            test(name: 'always registered') { echo $label }
            """;
        var analysis = Analyze(code, "conditional.test.kscript");
        var diagnostic = Assert.Single(analysis.Diagnostics);
        Assert.Equal(DiagnosticCodes.UndefinedVariable, diagnostic.Code);
        Assert.True(diagnostic.Span.Start > code.IndexOf("'always registered'", StringComparison.Ordinal));
    }

    [Fact]
    public void OnlyDirectlyDeclaredSetupBindingsAreVisibleToAnotherHook()
    {
        const string code = """
            <kscript>
              <before_each>
                <let name="label" value="'ready'" />
                <if test="true"><let name="hidden" value="1" /><echo>{ $hidden }</echo></if>
              </before_each>
              <before_each><let name="size" value="$label->count()" /></before_each>
              <test name="reads"><echo>{ $size } { $hidden }</echo></test>
            </kscript>
            """;
        var analysis = Analyze(code, "hooks.test.ks.xml");
        var diagnostic = Assert.Single(analysis.Diagnostics);
        Assert.Equal(DiagnosticCodes.UndefinedVariable, diagnostic.Code);
        Assert.Contains("hidden", diagnostic.Message);
    }
}
