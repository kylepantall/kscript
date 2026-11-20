using KScript.Language.Analysis;

using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests
{
    /// <summary>KS6001 on the unusual shapes: where it stays quiet, and where it warns without a fix.</summary>
    public class EarlyExitCoverageTests
    {
        private static Diagnostic Warning(string text, string path) =>
            Analyze(text, path).Diagnostics.FirstOrDefault(d => d.Code == DiagnosticCodes.PreferEarlyExit);

        [Theory]
        [InlineData("<fn name=\"f\"><live><if test=\"$a\"><return /></if><else><echo>b</echo></else></live></fn>")]
        // An elif chain that doesn't start with an if.
        [InlineData("<fn name=\"f\"><echo>x</echo><elif test=\"$a\"><return /></elif><else><echo>b</echo></else></fn>")]
        [InlineData("<fn name=\"f\">text<elif test=\"$a\"><return /></elif><else><echo>b</echo></else></fn>")]
        public void StaysQuiet(string body)
        {
            Assert.Null(Warning(Script(body), "test.ks.xml"));
        }

        [Theory]
        // Text after the last statement of a branch: it doesn't end early.
        [InlineData("<if test=\"$a\"><return /> more</if><else><echo>b</echo></else>")]
        // A comment between the if and the else.
        [InlineData("<if test=\"$a\"><return /></if><!-- c --><else><echo>b</echo></else>")]
        // An empty branch has nowhere to add the return.
        [InlineData("<if test=\"$a\"></if><else><echo>b</echo></else>")]
        public void WarnsWithoutAFix(string chain)
        {
            Diagnostic d = Warning(Script("<fn name=\"f\">" + chain + "</fn>"), "test.ks.xml");
            Assert.NotNull(d);
            Assert.True(d.Fixes == null || d.Fixes.Count == 0);
        }

        [Fact]
        public void AnElseWithOnlySpaceIsRemoved()
        {
            string text = Script("<fn name=\"f\">\n  <if test=\"$a\">\n    <return />\n  </if>\n  <else>   </else>\n</fn>");
            Diagnostic d = Warning(text, "test.ks.xml");
            string fixedText = AnalyzerTests.Apply(text, d.Fixes[0].Changes);
            Assert.DoesNotContain("<else>", fixedText);
        }

        [Fact]
        public void BranchesOutsideTheKscriptRootAreLeftAlone()
        {
            // Without a <kscript> root there are no siblings to look at.
            Assert.Null(Warning("<if test=\"$a\"><exit /></if><else><echo>b</echo></else>", "test.ks.xml"));
        }

        [Fact]
        public void AnElifWithoutATestHasNoFix()
        {
            Diagnostic d = Warning("fn f() {\n  if $a {\n    return\n  } elif {\n    echo \"b\"\n  }\n}\n", "test.kscript");
            Assert.NotNull(d);
            Assert.True(d.Fixes == null || d.Fixes.Count == 0);
        }
    }
}
