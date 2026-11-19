using KScript.Language.Analysis;

using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests
{
    /// <summary>The analyzer's view of packages: what needs an import, and what an import brings in.</summary>
    public class PackageAnalysisTests
    {
        [Theory]
        [InlineData("<input to=\"x\">?</input>", "KScript.Terminal", "input")]
        [InlineData("<echo>{ @sqrt(2) }</echo>", "KScript.Math", "@sqrt")]
        [InlineData("<echo>{ $pi }</echo>", "KScript.Math", "pi")]
        [InlineData("<echo>{ [1, 2]->median() }</echo>", "KScript.Math", "median")]
        [InlineData("<move from=\"'a'\" to=\"'b'\" />", "KScript.IO", "move")]
        [InlineData("<let name=\"r\" value=\"@fetch('https://example.com')\" />", "KScript.Net", "@fetch")]
        public void PackageFeaturesNeedAnImport(string body, string package, string marked)
        {
            DocumentAnalysis a = Analyze(Script(body));
            Diagnostic d = AssertSingle(a, DiagnosticCodes.MissingImport);
            Assert.Equal(marked, a.Source[d.Span]);
            Assert.Contains(package, d.Message);
            Assert.Contains($"<import package=\"{package}\" />", d.Fixes.Single().Changes.Single().NewText);
        }

        [Fact]
        public void TheQuickFixAddsTheImportAfterTheOthers()
        {
            string text = "<kscript>\n  <import package=\"KScript.IO\" />\n  <input to=\"x\">?</input>\n</kscript>";
            Diagnostic d = AssertSingle(Analyze(text), DiagnosticCodes.MissingImport);
            TextChange change = d.Fixes.Single().Changes.Single();
            string fixedText = text.Insert(change.Span.Start, change.NewText);
            Assert.Equal("<kscript>\n  <import package=\"KScript.IO\" />\n  <import package=\"KScript.Terminal\" />\n  <input to=\"x\">?</input>\n</kscript>", fixedText);
            AssertClean(Analyze(fixedText));
        }

        [Theory]
        [InlineData("<import package=\"KScript.Terminal\" /><input to=\"x\">?</input><echo>$x</echo>")]
        [InlineData("<import package=\"Math\" /><echo>{ @sqrt(2) } { $pi }</echo>")]
        [InlineData("<import package=\"KScript.Math\" as=\"math\" /><echo>{ @math.sqrt(2) } { math.pow(2, 3) }</echo>")]
        [InlineData("<import package=\"KScript.Terminal\" /><echo format=\"markdown\">**hi**</echo>")]
        public void ImportsBringPackagesIn(string body)
        {
            AssertClean(Analyze(Script(body)));
        }

        [Fact]
        public void APrefixedImportChecksItsNames()
        {
            DocumentAnalysis a = Analyze(Script("<import package=\"KScript.Math\" as=\"math\" /><echo>{ @math.sqr(2) }</echo>"));
            Diagnostic d = AssertSingle(a, DiagnosticCodes.UndefinedFunction);
            Assert.Equal("sqr", a.Source[d.Span]);
            Assert.Contains(d.Fixes, f => f.Changes.Single().NewText == "sqrt");
        }

        [Fact]
        public void UnknownAliasesAreReported()
        {
            AssertSingle(Analyze(Script("<echo>{ @maths.sqrt(2) }</echo>")), DiagnosticCodes.UndefinedFunction);
        }

        [Fact]
        public void UnknownPackagesSuggestTheClosest()
        {
            Diagnostic d = AssertSingle(Analyze(Script("<import package=\"KScript.Termnal\" />")), DiagnosticCodes.UnknownPackage);
            Assert.Contains("KScript.Terminal", d.Message);
        }

        [Fact]
        public void KScriptPrefixedElementsImportTheirPackage()
        {
            AssertClean(Analyze(Script("<KScript.input to=\"x\">?</KScript.input><input to=\"y\">?</input><echo>$x $y</echo>")));
        }

        [Fact]
        public void ProjectPackagesApplyToEveryScript()
        {
            string dir = Path.Combine(Path.GetTempPath(), "kscript-pkg");
            var files = new FakeFiles(new Dictionary<string, string>
            {
                [Path.Combine(dir, "kscript.json")] = "{ \"packages\": [\"KScript.Terminal\", \"Math\"] }",
            });
            AssertClean(Analyzer.Analyze(Script("<input to=\"x\">?</input><echo>{ @sqrt($x) }</echo>"), Path.Combine(dir, "scripts", "a.ks.xml"), files));
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

        [Fact]
        public void PagesDontNeedImports()
        {
            Assert.DoesNotContain(Analyze("<p>{ @read_text('x.txt') }</p>", "page.html").Diagnostics, d => d.Code == DiagnosticCodes.MissingImport);
        }
    }
}
