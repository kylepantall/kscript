using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Syntax;

namespace KScript.Language.Tests
{
    /// <summary>Messages and quick fixes in .kscript files are written in that format, and declared types are held to.</summary>
    public class CodeFileDiagnosticsTests
    {
        private static readonly LanguageService Service = new LanguageService(files: EmptyFileSystem.Instance);

        private static DocumentAnalysis Analyze(string text, string file) => Service.Analyze(text, Path.Combine(Path.GetTempPath(), file));

        [Fact]
        public void ATextArgumentsFixedWordsAreWrittenBare()
        {
            // paper: takes a4, letter... or any size in quotes, so its own words need no quotes, as an enum's don't.
            const string bare = "import KScript.Document\n\nDocument(to: \"a.pdf\", paper: letter) {\n}\n";
            Assert.DoesNotContain(Analyze(bare, "a.kscript").Diagnostics, d => d.Code == DiagnosticCodes.QuoteText || d.Severity == DiagnosticSeverity.Error);
            Diagnostic other = Analyze(bare.Replace("paper: letter", "paper: huge"), "a.kscript").Diagnostics.Single(d => d.Code == DiagnosticCodes.QuoteText);
            Assert.Contains("paper: \"huge\"", other.Message);
        }

        [Fact]
        public void MissingImportsSuggestTheKscriptWay()
        {
            const string text = "// tests\nimport KScript.Math\n\ndescribe(name: \"x\") {\n}\n";
            Diagnostic d = Analyze(text, "a.kscript").Diagnostics.Single(x => x.Code == DiagnosticCodes.MissingImport);
            Assert.Contains("Add import KScript.Test at the top", d.Message);
            Assert.DoesNotContain("<import", d.Message);
            Assert.Equal("Add import KScript.Test", d.Fixes[0].Title);
            string fixedText = AnalyzerTests.Apply(text, d.Fixes[0].Changes);
            Assert.Equal("// tests\nimport KScript.Math\nimport KScript.Test\n\ndescribe(name: \"x\") {\n}\n", fixedText);
            Assert.DoesNotContain(Analyze(fixedText, "a.kscript").Diagnostics, x => x.Severity == DiagnosticSeverity.Error);

            Diagnostic top = Analyze("describe(name: \"x\") {\n}\n", "b.kscript").Diagnostics.Single(x => x.Code == DiagnosticCodes.MissingImport);
            Assert.Equal("import KScript.Test\ndescribe(name: \"x\") {\n}\n", AnalyzerTests.Apply("describe(name: \"x\") {\n}\n", top.Fixes[0].Changes));
        }

        [Fact]
        public void TestFilesGetKScriptTestWithoutAnImport()
        {
            Assert.DoesNotContain(Analyze("describe(name: \"x\") {\n  test(name: \"y\") {\n    expect(value: 1, equals: 1)\n  }\n}\n", "a.test.kscript").Diagnostics,
                x => x.Severity == DiagnosticSeverity.Error);
        }

        [Fact]
        public void UnknownFunctionsAreDeclaredTheKscriptWay()
        {
            Diagnostic d = Analyze("@nope()\n", "a.kscript").Diagnostics.First(x => x.Code == DiagnosticCodes.UnknownCommand);
            Assert.Contains("fn nope()", d.Message);
            Assert.DoesNotContain("<fn", d.Message);
            Assert.Contains("<fn name=\"nope\">", Analyze("<kscript><do>@nope()</do></kscript>", "a.ks.xml").Diagnostics.First(x => x.Code == DiagnosticCodes.UnknownCommand).Message);
        }

        [Theory]
        [InlineData("fn get(person: int) => $person->trim()->title()\n", "a.kscript")]
        [InlineData("<kscript><fn name=\"get\" params=\"person: int\" value=\"$person->trim()\" /></kscript>", "a.ks.xml")]
        [InlineData("count: int = 3\necho $count->upper()\n", "a.kscript")]
        public void DeclaredNumbersHaveNoStringMethods(string text, string file)
        {
            Diagnostic d = Analyze(text, file).Diagnostics.Single(x => x.Code == DiagnosticCodes.TypeMismatch);
            Assert.Equal(DiagnosticSeverity.Error, d.Severity);
            Assert.Contains("declared as a number", d.Message);
        }

        [Fact]
        public void UndeclaredNumbersAreWarnedAboutStringMethods()
        {
            // n = 3 holds a number at run time, which has no ->upper(), so the editor warns (not an error: its type wasn't written).
            Diagnostic d = Analyze("n = 3\necho $n->upper()\n", "a.kscript").Diagnostics.Single(x => x.Code == DiagnosticCodes.TypeMismatch);
            Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
            Assert.Contains("Use ->to_string()->upper()", d.Message);
            // A parameter without a type may be anything, so text methods are fine on it.
            Assert.DoesNotContain(Analyze("fn f(x) => $x->trim()\n", "a.kscript").Diagnostics, x => x.Code == DiagnosticCodes.TypeMismatch);
        }

        [Fact]
        public void AMatchUsedAsAValueKnowsIt()
        {
            Assert.DoesNotContain(Analyze("fn size(x) => match $x { _ when $it > 9 => 'big', _ => 'small' }\necho @size(3)\n", "a.kscript").Diagnostics,
                x => x.Severity == DiagnosticSeverity.Error);
        }

        [Fact]
        public void ChainedComparisonsAreReported()
        {
            Diagnostic d = Analyze("n = 5\necho 1 < $n < 10\n", "a.kscript").Diagnostics.Single(x => x.Code == DiagnosticCodes.TypeMismatch);
            Assert.Contains("Comparisons can't be chained", d.Message);
            Assert.DoesNotContain(Analyze("n = 5\necho 1 < $n and $n < 10\necho (1 < $n) == true\n", "a.kscript").Diagnostics, x => x.Code == DiagnosticCodes.TypeMismatch);
        }

        [Fact]
        public void ContinueInsideAFunctionIsReportedLikeTheRuntime()
        {
            // The runtime agrees: break and continue stop at the function they are in.
            Assert.Contains(Analyze("fn f() {\n  continue\n}\nfor i in 0..2 {\n  @f()\n}\n", "a.kscript").Diagnostics, x => x.Code == DiagnosticCodes.MisplacedElement);
        }
    }
}
