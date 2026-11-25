using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Syntax;

using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests
{
    public class IssueBatchEditorTests
    {
        [Theory]
        [InlineData("id: int", null)]
        [InlineData("slug: int", "has no {slug}")]
        [InlineData("id int", "Write name: rule pairs")]
        [InlineData("id: '['", "isn't a valid pattern")]
        public void RoutesCheckConstraintsWithoutRunning(string where, string error)
        {
            string text = "import KScript.Http\nRoutes {\n  Group(prefix: /people/{id}) {\n    Get(path: /profile, where: { "
                + where + " }) { return 'found' }\n  }\n}\n";
            var analysis = Analyzer.Analyze(text, "routes.kscript", EmptyFileSystem.Instance);
            if (error == null) AssertClean(analysis);
            else Assert.Contains(analysis.Diagnostics, d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains(error));
        }

        [Fact]
        public void RouteConstraintsCanReferToTheConfiguredFilePrefix()
        {
            string directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "route-constraint-prefix"));
            string path = Path.Combine(directory, "routes.kscript");
            const string code = """
                import KScript.Http
                Routes {
                  Get(path: /profile, where: { id: int }) { return 'found' }
                }
                """;
            var files = new MemoryFiles().Add(path, code).Add(Path.Combine(directory, "kscript.json"),
                """{"http":{"routes":[{"file":"routes.kscript","prefix":"/people/{id}"}]}}""");
            AssertClean(Analyzer.Analyze(code, path, files));
        }

        [Fact]
        public void BareUuidCallSuggestsValidationWithoutReplacingItWithGeneration()
        {
            const string text = "input = '3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f'\necho uuid($input)\n";
            var analysis = Analyzer.Analyze(text, "uuid.kscript", EmptyFileSystem.Instance);
            var error = Assert.Single(analysis.Diagnostics, d => d.Code == DiagnosticCodes.UnknownCommand);
            Assert.Contains("value as uuid", error.Message);
            Assert.Contains("@uuid() to generate", error.Message);
            AssertClean(Analyzer.Analyze(text.Replace("uuid($input)", "$input as uuid"), "uuid.kscript", EmptyFileSystem.Instance));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TextareaInterpolationCompletesVariablesInPagesAndComponents(bool component)
        {
            const string markup = "<textarea>{ $dra‸ }</textarea>";
            string source = component
                ? "import KScript.Web\ntype Editor extends Component {\n  draft: string\n  fn render(): Component => ```html\n  "
                    + markup + "\n  ```\n}\n"
                : "<let name='draft' value=\"'hello'\" />\n" + markup;
            string path = component ? "editor.kscript" : "editor.html";
            var service = new LanguageService(files: EmptyFileSystem.Instance);

            (string text, int offset) = WithCaret(source);
            var document = service.Analyze(text, path);
            var suggestion = Assert.Single(service.GetCompletions(document, offset), item => item.Label == "draft");
            Assert.Equal("dra", text.Substring(suggestion.ReplaceSpan.Start, suggestion.ReplaceSpan.Length));
            AssertClean(service.Analyze(text.Remove(suggestion.ReplaceSpan.Start, suggestion.ReplaceSpan.Length)
                .Insert(suggestion.ReplaceSpan.Start, suggestion.InsertText ?? suggestion.Label), path));

            (text, offset) = WithCaret(source.Replace("{ $dra‸ }", "$dra‸"));
            Assert.DoesNotContain(service.GetCompletions(service.Analyze(text, path), offset), item => item.Label is "draft" or "$draft");
        }
    }
}
