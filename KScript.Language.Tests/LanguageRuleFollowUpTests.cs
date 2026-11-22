using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Syntax;
using KScript.Syntax.Markup;

namespace KScript.Language.Tests
{
    /// <summary>The language's one-spelling rules, where they first missed a case.</summary>
    public class LanguageRuleFollowUpTests
    {
        private static string Folder => AnalyzerCoverageTests.Folder;

        private static DocumentAnalysis Code(string text, IFileSystem files = null, string file = "app.kscript") =>
            TestHelpers.WithoutUnused(Analyzer.Analyze(text, Path.Combine(Folder, file), files ?? EmptyFileSystem.Instance));

        [Fact]
        public void AnImportedTypeIsNamedBareToo()
        {
            var files = new AnalyzerCoverageTests.Files(("models/product.kscript", "import KScript.Data\n\nmodel Product {\n  name: string\n}\n"));
            DocumentAnalysis a = Code("""
                import KScript.Data
                import "models/product.kscript"

                echo $Product->count()
                for p in $Product->get() {
                  echo $p.name
                }
                kind = Product
                echo $kind
                """, files);
            List<Diagnostic> found = a.Diagnostics.Where(d => d.Code == DiagnosticCodes.TypeForm).ToList();
            Assert.Equal(2, found.Count);
            Assert.All(found, d => Assert.Equal("Write Product", d.Fixes[0].Title));
        }
    }
}
