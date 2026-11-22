using KScript.Language.Analysis;

using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests
{
    /// <summary>How the analyzer treats test files (name.test.ks.xml) and the KScript.Test elements.</summary>
    public class TestFileAnalysisTests
    {
        private const string Tests = """
            <describe name="cart">
              <let name="cart" value="[]" />
              <before_each><set name="cart" value="['apple']" /></before_each>
              <for each="case" at="i" in="[{ n: 1 }]">
                <test name="has {$case.n} items">
                  <expect value="$cart" count="$case.n" />
                  <assert test="$i ge 0" />
                </test>
              </for>
              <test name="errors">
                <expect_error type="Validation" as="err"><throw type="Validation" message="no" /></expect_error>
                <expect value="$err.message" equals="'no'" />
                <capture into="out"><echo>hi</echo></capture>
                <expect value="$out" equals="'hi'" />
                <do>@assert(true); @fail('x')</do>
                <let name="r" value="@run_script('../hello.ks.xml')" />
              </test>
            </describe>
            """;

        [Theory]
        [InlineData("cart.test.ks.xml")]
        public void TestFilesGetKScriptTestWithoutAnImport(string name)
        {
            AssertClean(Analyze(Script(Tests), name));
        }

        [Fact]
        public void OtherScriptsNeedTheImport()
        {
            DocumentAnalysis a = Analyze(Script(Tests), "cart.ks.xml");
            Assert.Contains(a.Diagnostics, d => d.Code == DiagnosticCodes.MissingImport && d.Message.Contains("KScript.Test"));
            AssertClean(Analyze(Script("<import package=\"KScript.Test\" />\n" + Tests), "cart.ks.xml"));
        }

        [Fact]
        public void TheLoopVariableOnlyExistsInsideTheLoopRoundTheTest()
        {
            DocumentAnalysis a = Analyze(Script("<for each=\"x\" in=\"[1]\"><test name=\"t {$x}\" /></for>\n<echo>{ $x }</echo>"), "a.test.ks.xml");
            Diagnostic d = AssertSingle(a, DiagnosticCodes.UndefinedVariable);
            Assert.Equal("x", a.Source[d.Span]);
        }

        [Fact]
        public void EachOnATestSaysToUseAForLoop()
        {
            DocumentAnalysis a = Analyze(Script("<test name=\"t\" each=\"x\" in=\"[1]\" />"), "a.test.ks.xml");
            Assert.All(a.Diagnostics, d => Assert.Equal(DiagnosticCodes.RetiredAttribute, d.Code));
            Assert.Contains(a.Diagnostics, d => d.Message.Contains("for loop round the test"));
        }

        [Fact]
        public void TestElementsAreChecked()
        {
            DocumentAnalysis a = Analyze(Script("<test name=\"t\"><expect value=\"1\" equal=\"1\" /></test>"), "a.test.ks.xml");
            Assert.Contains(a.Diagnostics, d => d.Message.Contains("equal"));
        }

        [Fact]
        public void ABareImportBringsInEveryFunction()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ks-bare-import");
            var files = new FakeFiles(new Dictionary<string, string>
            {
                [Path.Combine(dir, "lib", "strings.ks.xml")] = "<kscript><fn name=\"slugify\" params=\"text\" value=\"$text\" /><fn name=\"shout\" params=\"t\" value=\"$t\" /></kscript>",
            });
            DocumentAnalysis a = Analyzer.Analyze(Script("<import path=\"../lib/strings.ks.xml\" />\n<echo>{ @slugify('a') } { @shout('b') }</echo>"), Path.Combine(dir, "tests", "s.test.ks.xml"), files);
            AssertClean(a);
            a = Analyzer.Analyze(Script("<import path=\"../lib/strings.ks.xml\" />\n<echo>{ @nope('a') }</echo>"), Path.Combine(dir, "tests", "s.test.ks.xml"), files);
            AssertSingle(a, DiagnosticCodes.UnknownCommand);
        }

        [Fact]
        public void KScriptJsonTestPackagesApplyToTestFilesOnly()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ks-test-packages");
            var files = new FakeFiles(new Dictionary<string, string>
            {
                [Path.Combine(dir, "kscript.json")] = "{ \"test\": { \"packages\": [\"KScript.Math\"] } }",
            });
            string body = Script("<echo>{ @sqrt(4) }</echo>");
            AssertClean(Analyzer.Analyze(body, Path.Combine(dir, "tests", "a.test.ks.xml"), files));
            AssertSingle(Analyzer.Analyze(body, Path.Combine(dir, "a.ks.xml"), files), DiagnosticCodes.MissingImport);
        }

        private sealed class FakeFiles : IFileSystem
        {
            private readonly Dictionary<string, string> files;
            public FakeFiles(Dictionary<string, string> files) => this.files = files.ToDictionary(k => Path.GetFullPath(k.Key), k => k.Value);
            public bool FileExists(string path) => files.ContainsKey(Path.GetFullPath(path));
            public bool DirectoryExists(string path) => files.Keys.Any(f => f.StartsWith(Path.GetFullPath(path) + Path.DirectorySeparatorChar, StringComparison.Ordinal));
            public string ReadText(string path) => files.TryGetValue(Path.GetFullPath(path), out string t) ? t : null;
            public IEnumerable<string> ListFiles(string directory, string pattern) => Enumerable.Empty<string>();
        }
    }
}
