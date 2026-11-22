using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Syntax;
using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests
{
    public class CastAnalysisTests
    {
        private static readonly LanguageService Service = new LanguageService();

        [Theory]
        [InlineData("null as Missing?")]
        [InlineData("[] as Missing[]")]
        public void TheTargetMustExistEvenWhenTheValueIsEmpty(string expression)
        {
            DocumentAnalysis analysis = Analyze("result = " + expression, "example.kscript");
            Diagnostic error = AssertSingle(analysis, DiagnosticCodes.InvalidAttributeValue);
            Assert.Equal("Missing", analysis.Source[error.Span]);
            Assert.Contains("Unknown type 'Missing'", error.Message);
        }

        [Fact]
        public void ScalarCastsUseTheSameConversionsAsTypedValues()
        {
            DocumentAnalysis analysis = Analyze("enum Size { large, small }\na = ' LARGE ' as Size\nb = '42' as int(min: 1)\nc = 'no number' as int", "example.kscript");
            Assert.Equal("'no number'", analysis.Source[AssertSingle(analysis, DiagnosticCodes.InvalidTypedValue).Span]);
        }

        [Fact]
        public void InlineRulesRetainTheListShapeAndRequireLiteralOptions()
        {
            DocumentAnalysis analysis = Analyze("words = ['ab'] as string(min: 1)[](max: 4)\nbad = 'ab' as string(min: $n)", "example.kscript");
            Assert.Equal("string", analysis.Symbols.Single(s => s.Name == "words").Shape.Element.Type);
            Assert.Contains("literal values", AssertSingle(analysis, DiagnosticCodes.InvalidAttributeValue).Message);
        }

        [Fact]
        public void CastsCheckLiteralValuesAndAnalyzeTheirOperands()
        {
            DocumentAnalysis analysis = Analyze("""
                type Key with [string] { pattern: /^passkey_[a-z]+$/ }
                type Trimmed with [string] { normalize: $value->trim(); pattern: /^ok$/ }
                kind = 'login'
                good = "passkey_{$kind}" as Key
                normalized = ' ok ' as Trimmed
                bad = 'passkey_12' as Key
                bare = word as string
                missing = $missing as string
                """, "example.kscript");
            Assert.Equal("'passkey_12'", analysis.Source[AssertSingle(analysis, DiagnosticCodes.InvalidTypedValue).Span]);
            Assert.Equal("word", analysis.Source[AssertSingle(analysis, DiagnosticCodes.BareIdentifier).Span]);
            Assert.Contains(analysis.Diagnostics, d => d.Code == DiagnosticCodes.UndefinedVariable && d.Message.Contains("missing"));
        }

        [Fact]
        public void CastsInferRecordFieldsAndPreserveTargetSuffixesOnRename()
        {
            (string text, int offset) = WithCaret("""
                type Product { title: string }
                products = [] as Pro‸duct[]?
                product = $products[0]
                echo $product.titel
                """);
            DocumentAnalysis analysis = Service.Analyze(text, "/tmp/cast.kscript");
            Assert.Equal("titel", analysis.Source[AssertSingle(analysis, DiagnosticCodes.UnknownField).Span]);
            Assert.Contains("title", analysis.Symbols.Single(s => s.Name == "products").Shape.Element.Fields.Keys);
            SourceLocation definition = Assert.Single(Service.GetDefinition(analysis, offset));
            Assert.Equal(text.IndexOf("Product", StringComparison.Ordinal), definition.Span.Start);
            RenameResult rename = Service.Rename(analysis, offset, "Item");
            Assert.Null(rename.Error);
            Assert.Contains("as Item[]?", AnalyzerTests.Apply(text, rename.Changes));
            List<SemanticToken> tokens = Service.GetSemanticTokens(analysis);
            Assert.Contains(tokens, t => analysis.Source[t.Span] == "as" && t.Type == SemanticTokenType.Keyword);
            Assert.Contains(tokens, t => t.Span.Contains(offset) && t.Type == SemanticTokenType.Type);
        }

        [Theory]
        [InlineData("type Product { title: string }\nproducts = [] as ‸", "example.kscript")]
        [InlineData("type Product { title: string }\nproducts = [] as Pr‸", "example.kscript")]
        [InlineData("<kscript><type name=\"Product\" fields=\"title\"/><let name=\"products\" value=\"[] as Pr‸\"/></kscript>", "example.ks.xml")]
        public void CastTargetsCompleteWithVisibleAndBuiltinTypes(string code, string path)
        {
            (string text, int offset) = WithCaret(code);
            DocumentAnalysis analysis = Service.Analyze(text, path);
            List<CompletionItem> completions = Service.GetCompletions(analysis, offset);
            Assert.Contains(completions, c => c.Label == "string" && c.Detail == "type");
            Assert.Contains(completions, c => c.Label == "Product" && c.Detail == "type");
            Assert.All(completions, c => Assert.Equal("type", c.Detail));
        }
    }
}
