using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Syntax;

namespace KScript.Language.Tests
{
    /// <summary>KS5005 on a fence's value is an error: only a fence makes one, so a missing method always fails (#187).</summary>
    public class FenceMethodDiagnosticsTests
    {
        private static DocumentAnalysis Analyze(string code) =>
            new LanguageService(files: EmptyFileSystem.Instance).Analyze(code, "test.kscript");

        private const string Paper = "\n\nfn paper() => ```latex\n  \\section{Draft}\n  ```\n\nfn notes() => ```md\n  # Notes\n  ```\n";

        [Theory]
        [InlineData("@paper()->iso()", "works on dates, but this value is a latex.")]
        [InlineData("@paper()->upper()", "Use .text->upper() to work on its source text.")]
        [InlineData("@paper()->html()", "KScript doesn't render it. Write a Markdown fence")]
        [InlineData("@paper()->latex()", "It is LaTeX already: .text is its source.")]
        public void A_method_a_fence_does_not_have_is_an_error(string call, string message)
        {
            Diagnostic d = Assert.Single(Analyze("import KScript.Document\n\necho " + call + Paper).Diagnostics, x => x.Code == DiagnosticCodes.TypeMismatch);
            Assert.Equal(DiagnosticSeverity.Error, d.Severity);
            Assert.Contains(message, d.Message);
        }

        [Fact]
        public void Using_a_markdown_fence_s_document_methods_uses_the_import()
        {
            DocumentAnalysis analysis = Analyze("import KScript.Document\n\necho @notes()->html()\n@paper()->save('paper')" + Paper);
            Assert.DoesNotContain(analysis.Diagnostics, d => d.Code is DiagnosticCodes.TypeMismatch or DiagnosticCodes.UnusedImport);
        }
    }
}
