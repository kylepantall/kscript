using KScript.Language.Analysis;
using KScript.Projects;

using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests
{
    /// <summary>KS6001: else and elif in scripts, where an early return, continue or break reads better.</summary>
    public class EarlyExitTests
    {
        private static DocumentAnalysis Xml(string text) => Analyze(text, "test.ks.xml");

        private static DocumentAnalysis Code(string text) => Analyze(text, "test.kscript");

        /// <summary>Applies the KS6001 fix and checks the result has no KS6001 left on that spot and no errors.</summary>
        private static string Fix(DocumentAnalysis a, string text, string path)
        {
            Diagnostic d = a.Diagnostics.First(x => x.Code == DiagnosticCodes.PreferEarlyExit);
            Assert.True(d.Fixes.Count == 1, "Expected a fix:\n" + Describe(a));
            string fixedText = AnalyzerTests.Apply(text, d.Fixes[0].Changes);
            DocumentAnalysis after = Analyze(fixedText, path);
            Assert.DoesNotContain(after.Diagnostics, x => x.Severity == DiagnosticSeverity.Error);
            return fixedText;
        }

        [Fact]
        public void WarnsOnElseInAFunction()
        {
            DocumentAnalysis a = Xml(Script(
                "<fn name=\"sign\" params=\"n\">\n" +
                "  <if test=\"$n lt 0\">\n" +
                "    <return value=\"-1\" />\n" +
                "  </if>\n" +
                "  <else>\n" +
                "    <return value=\"1\" />\n" +
                "  </else>\n" +
                "</fn>"));
            Diagnostic d = AssertSingle(a, DiagnosticCodes.PreferEarlyExit);
            Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
            Assert.Equal("else", a.Source[d.Span]);
            Assert.Contains("isn't needed", d.Message);
        }

        [Fact]
        public void RemovesARedundantElse()
        {
            string text = Script(
                "<fn name=\"sign\" params=\"n\">\n" +
                "  <if test=\"$n lt 0\">\n" +
                "    <return value=\"-1\" />\n" +
                "  </if>\n" +
                "  <else>\n" +
                "    <echo>positive</echo>\n" +
                "    <return value=\"1\" />\n" +
                "  </else>\n" +
                "</fn>");
            string fixedText = Fix(Xml(text), text, "test.ks.xml");
            Assert.Equal(Script(
                "<fn name=\"sign\" params=\"n\">\n" +
                "  <if test=\"$n lt 0\">\n" +
                "    <return value=\"-1\" />\n" +
                "  </if>\n" +
                "  <echo>positive</echo>\n" +
                "  <return value=\"1\" />\n" +
                "</fn>"), fixedText);
            AssertClean(Xml(fixedText));
        }

        [Fact]
        public void AddsAReturnWhenTheChainEndsTheFunction()
        {
            string text = Script(
                "<fn name=\"report\" params=\"n\">\n" +
                "  <if test=\"$n gt 9\">\n" +
                "    <echo>big</echo>\n" +
                "  </if>\n" +
                "  <else>\n" +
                "    <echo>small</echo>\n" +
                "  </else>\n" +
                "</fn>");
            DocumentAnalysis a = Xml(text);
            Assert.Contains("return early", AssertSingle(a, DiagnosticCodes.PreferEarlyExit).Message);
            string fixedText = Fix(a, text, "test.ks.xml");
            Assert.Equal(Script(
                "<fn name=\"report\" params=\"n\">\n" +
                "  <if test=\"$n gt 9\">\n" +
                "    <echo>big</echo>\n" +
                "    <return />\n" +
                "  </if>\n" +
                "  <echo>small</echo>\n" +
                "</fn>"), fixedText);
            AssertClean(Xml(fixedText));
        }

        [Fact]
        public void AddsAContinueInALoop()
        {
            string text = Script(
                "<for each=\"i\" in=\"0..5\">\n" +
                "  <if test=\"$i % 2 eq 0\">\n" +
                "    <echo>even</echo>\n" +
                "  </if>\n" +
                "  <else>\n" +
                "    <echo>odd</echo>\n" +
                "  </else>\n" +
                "</for>");
            DocumentAnalysis a = Xml(text);
            Assert.Contains("continue", AssertSingle(a, DiagnosticCodes.PreferEarlyExit).Message);
            Assert.Equal(Script(
                "<for each=\"i\" in=\"0..5\">\n" +
                "  <if test=\"$i % 2 eq 0\">\n" +
                "    <echo>even</echo>\n" +
                "    <continue />\n" +
                "  </if>\n" +
                "  <echo>odd</echo>\n" +
                "</for>"), Fix(a, text, "test.ks.xml"));
        }

        [Fact]
        public void NoFixWhenCodeFollowsTheChain()
        {
            DocumentAnalysis a = Xml(Script(
                "<fn name=\"f\" params=\"n\">\n" +
                "  <if test=\"$n gt 9\"><echo>big</echo></if>\n" +
                "  <else><echo>small</echo></else>\n" +
                "  <echo>done</echo>\n" +
                "</fn>"));
            Assert.Empty(AssertSingle(a, DiagnosticCodes.PreferEarlyExit).Fixes);
        }

        [Fact]
        public void NoFixWhenAnEarlierBranchFallsThrough()
        {
            // Moving the else out would run it after the first branch too.
            DocumentAnalysis a = Xml(Script(
                "<fn name=\"f\" params=\"n\">\n" +
                "  <if test=\"$n gt 9\"><echo>big</echo></if>\n" +
                "  <elif test=\"$n gt 5\"><return value=\"1\" /></elif>\n" +
                "  <else><echo>small</echo></else>\n" +
                "  <echo>done</echo>\n" +
                "</fn>"));
            List<Diagnostic> found = a.Diagnostics.Where(d => d.Code == DiagnosticCodes.PreferEarlyExit).ToList();
            Assert.Equal(2, found.Count);
            Assert.All(found, d => Assert.Empty(d.Fixes));
        }

        [Fact]
        public void SplitsAnElifIntoASeparateIf()
        {
            string text = Script(
                "<fn name=\"grade\" params=\"score\">\n" +
                "  <if test=\"$score ge 90\">\n" +
                "    <return value=\"'A'\" />\n" +
                "  </if>\n" +
                "  <elif test=\"$score ge 75\">\n" +
                "    <return value=\"'B'\" />\n" +
                "  </elif>\n" +
                "  <return value=\"'C'\" />\n" +
                "</fn>");
            string fixedText = Fix(Xml(text), text, "test.ks.xml");
            Assert.Equal(Script(
                "<fn name=\"grade\" params=\"score\">\n" +
                "  <if test=\"$score ge 90\">\n" +
                "    <return value=\"'A'\" />\n" +
                "  </if>\n" +
                "  <if test=\"$score ge 75\">\n" +
                "    <return value=\"'B'\" />\n" +
                "  </if>\n" +
                "  <return value=\"'C'\" />\n" +
                "</fn>"), fixedText);
            AssertClean(Xml(fixedText));
        }

        [Fact]
        public void FixesAWholeChainOneStepAtATime()
        {
            string text = Script(
                "<fn name=\"grade\" params=\"score\">\n" +
                "  <if test=\"$score ge 90\">\n" +
                "    <return value=\"'A'\" />\n" +
                "  </if>\n" +
                "  <elif test=\"$score ge 75\">\n" +
                "    <return value=\"'B'\" />\n" +
                "  </elif>\n" +
                "  <else>\n" +
                "    <return value=\"'C'\" />\n" +
                "  </else>\n" +
                "</fn>");
            for (int i = 0; i < 3 && Xml(text).Diagnostics.Any(d => d.Code == DiagnosticCodes.PreferEarlyExit); i++)
            {
                text = Fix(Xml(text), text, "test.ks.xml");
            }
            AssertClean(Xml(text));
            Assert.DoesNotContain("else", text);
            Assert.DoesNotContain("elif", text);
        }

        [Theory]
        // Top of a script with nothing to return from.
        [InlineData("<let name=\"n\" value=\"1\" /><if test=\"$n gt 0\"><echo>a</echo></if><else><echo>b</echo></else>")]
        [InlineData("<let name=\"n\" value=\"1\" /><if test=\"$n gt 0\"><echo>a</echo></if><elif test=\"$n lt 0\"><echo>b</echo></elif>")]
        // An empty-list else after a loop.
        [InlineData("<fn name=\"f\" params=\"xs\"><for each=\"x\" in=\"$xs\"><echo>$x</echo></for><else><echo>none</echo></else></fn>")]
        public void QuietWhereElseIsTheOnlyWay(string body) => AssertClean(Xml(Script(body)));

        [Fact]
        public void WarnsAtTheTopWhenElseIsRedundant()
        {
            DocumentAnalysis a = Xml(Script("<let name=\"n\" value=\"1\" /><if test=\"$n lt 0\"><exit code=\"1\" /></if><else><echo>ok</echo></else>"));
            Assert.Single(AssertSingle(a, DiagnosticCodes.PreferEarlyExit).Fixes);
        }

        [Fact]
        public void QuietInPagesAndLiveBlocks()
        {
            string page = "<let name=\"n\" value=\"1\" /><if test=\"$n gt 0\"><p>yes</p></if><else><p>no</p></else>" +
                "<live><let name=\"on\" value=\"false\" /><if test=\"$on\"><p>on</p></if><else><p>off</p></else></live>";
            DocumentAnalysis a = Analyze(page, "index.html");
            Assert.DoesNotContain(a.Diagnostics, d => d.Code == DiagnosticCodes.PreferEarlyExit);
        }

        [Fact]
        public void CodeFormatRemovesElse()
        {
            string text = "fn sign(n) {\n  if $n < 0 {\n    return -1\n  } else {\n    echo \"positive\"\n    return 1\n  }\n}\n";
            DocumentAnalysis a = Code(text);
            Diagnostic d = AssertSingle(a, DiagnosticCodes.PreferEarlyExit);
            Assert.Equal("else", a.Source[d.Span]);
            Assert.DoesNotContain("<", d.Message);
            Assert.Equal("fn sign(n) {\n  if $n < 0 {\n    return -1\n  }\n  echo \"positive\"\n  return 1\n}\n", Fix(a, text, "test.kscript"));
        }

        [Fact]
        public void CodeFormatAddsContinue()
        {
            string text = "for i in 0..5 {\n  if $i % 2 == 0 {\n    echo \"even\" // say so\n  } else {\n    echo \"odd\"\n  }\n}\n";
            Assert.Equal("for i in 0..5 {\n  if $i % 2 == 0 {\n    echo \"even\" // say so\n    continue\n  }\n  echo \"odd\"\n}\n",
                Fix(Code(text), text, "test.kscript"));
        }

        [Fact]
        public void CodeFormatSplitsElseIf()
        {
            string text = "fn grade(score) {\n  if $score >= 90 {\n    return 'A'\n  } else if $score >= 75 {\n    return 'B'\n  }\n  return 'C'\n}\n";
            DocumentAnalysis a = Code(text);
            Diagnostic d = AssertSingle(a, DiagnosticCodes.PreferEarlyExit);
            Assert.StartsWith("Else if can be a separate if", d.Message);
            string fixedText = Fix(a, text, "test.kscript");
            Assert.Equal("fn grade(score) {\n  if $score >= 90 {\n    return 'A'\n  }\n  if $score >= 75 {\n    return 'B'\n  }\n  return 'C'\n}\n", fixedText);
            AssertClean(Code(fixedText));
        }

        [Fact]
        public void CodeFormatOneLiners()
        {
            string text = "fn f(n) {\n  if $n > 9 { echo \"big\" } else { echo \"small\" }\n}\n";
            Assert.Equal("fn f(n) {\n  if $n > 9 { echo \"big\"; return }\n  echo \"small\"\n}\n", Fix(Code(text), text, "test.kscript"));
        }

        [Fact]
        public void CanBeTurnedOffInKscriptJson()
        {
            string dir = Path.Combine(Path.GetTempPath(), "kscript-ks6001-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "kscript.json"), "{ \"lint\": { \"rules\": { \"KS6001\": \"off\" } } }");
                ProjectConfig config = ProjectConfig.Load(Path.Combine(dir, "kscript.json"));
                string path = Path.Combine(dir, "a.ks.xml");
                DocumentAnalysis a = Analyze(Script("<fn name=\"f\" params=\"n\"><if test=\"$n\"><echo>a</echo></if><else><echo>b</echo></else></fn>"), path);
                Assert.Contains(a.Diagnostics, d => d.Code == DiagnosticCodes.PreferEarlyExit);
                Assert.DoesNotContain(LintRules.Apply(a.Diagnostics, config, path), d => d.Code == DiagnosticCodes.PreferEarlyExit);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
