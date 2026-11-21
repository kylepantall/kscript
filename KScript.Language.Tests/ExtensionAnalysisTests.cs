using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Syntax;

using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests
{
    /// <summary>The editor side of <c>extend</c>: extension methods are offered and checked only where they apply.</summary>
    public class ExtensionAnalysisTests
    {
        private static readonly LanguageService Service = new LanguageService();

        private const string Dates = "extend date {\n  /// The date for an API.\n  fn json() => $this->format('yyyy-MM-dd')\n  fn is_weekend() => $this->format('ddd') in ['Sat', 'Sun']\n}\n";

        private static string TempProject(params (string Path, string Text)[] files)
        {
            string dir = Path.Combine(Path.GetTempPath(), "kscript-ext-lsp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            foreach ((string path, string text) in files)
            {
                string full = Path.Combine(dir, path);
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                File.WriteAllText(full, text);
            }
            return dir;
        }

        private static DocumentAnalysis Code(string text, string path = "/tmp/kscript-ext-lsp/main.kscript") => WithoutUnused(Service.Analyze(text, path));

        private static List<string> Labels(string text, string path = "/tmp/kscript-ext-lsp/main.kscript")
        {
            (string t, int offset) = WithCaret(text);
            return Service.GetCompletions(Service.Analyze(t, path), offset).Select(i => i.Label).ToList();
        }

        [Fact]
        public void ExtensionMethodsAreKnownInTheirFile()
        {
            DocumentAnalysis a = Code(Dates + "extend list {\n  fn second() => $this[1]\n}\necho \"{ @today()->json() } { @today()->is_weekend() } { [1, 2]->second() }\"\n");
            AssertClean(a);
            Assert.Contains(a.Symbols, s => s.Name == "this");
        }

        [Fact]
        public void TheyAreOnlyForTheTypeTheyExtend()
        {
            DocumentAnalysis a = Code(Dates + "echo \"{ [1, 2]->is_weekend() }\"\n");
            Assert.Contains(a.Diagnostics, d => d.Message.Contains("is_weekend"));
            a = Code("echo \"{ @today()->is_weekend() }\"\n");
            Assert.Contains(a.Diagnostics, d => d.Code == DiagnosticCodes.UnknownMethod);
        }

        [Fact]
        public void CompletionOffersThemOnlyWhereTheyApply()
        {
            List<string> labels = Labels(Dates + "echo \"{ @today()->‸ }\"\n");
            Assert.Contains("is_weekend", labels);
            Assert.Contains("json", labels);
            Assert.Single(labels, l => l == "json");
            Assert.DoesNotContain("is_weekend", Labels(Dates + "echo \"{ [1]->‸ }\"\n"));
            Assert.DoesNotContain("is_weekend", Labels("echo \"{ @today()->‸ }\"\n"));
        }

        [Fact]
        public void HoverShowsTheDocs()
        {
            string text = Dates + "echo \"{ @today()->json() }\"\n";
            DocumentAnalysis a = Service.Analyze(text, "/tmp/kscript-ext-lsp/main.kscript");
            HoverResult hover = Service.GetHover(a, text.LastIndexOf("json", StringComparison.Ordinal) + 1);
            Assert.NotNull(hover);
            Assert.Contains("The date for an API.", hover.Markdown);
            Assert.Contains("date->json()", hover.Markdown);
            Assert.Contains("Added to date by `extend` in this file.", hover.Markdown);
        }

        [Fact]
        public void ArgumentsAreChecked()
        {
            DocumentAnalysis a = Code("extend number {\n  fn times_by(n) => $this * $n\n}\necho \"{ 2->times_by() }\"\n");
            AssertSingle(a, DiagnosticCodes.ArgumentCount);
        }

        [Fact]
        public void OverridesAndMistakesAreReported()
        {
            Assert.Contains("A date already has ->to_json(). To replace it, write override fn to_json(...)",
                AssertSingle(Code("extend date {\n  fn to_json() => 1\n}\n"), DiagnosticCodes.DuplicateDeclaration).Message);
            AssertClean(Code("extend date {\n  override fn to_json() => 1\n}\necho \"{ @today()->to_json() }\"\n"));
            Assert.Contains("has no built-in ->zzz() to override",
                AssertSingle(Code("extend date {\n  override fn zzz() => 1\n}\n"), DiagnosticCodes.InvalidAttributeValue).Message);
            Assert.Contains("There is no type called dat to extend. Did you mean date?",
                AssertSingle(Code("extend dat {\n  fn x() => 1\n}\n"), DiagnosticCodes.InvalidAttributeValue).Message);
            Assert.Contains("holds fn methods only", Code("extend date {\n  echo 'hi'\n}\n").Diagnostics.Single(d => d.Code == DiagnosticCodes.MisplacedElement).Message);
            Assert.Contains("already added to date", Code("extend date {\n  fn x() => 1\n}\nextend date {\n  fn x() => 2\n}\n").Diagnostics.Single(d => d.Code == DiagnosticCodes.DuplicateDeclaration).Message);
            Assert.Contains("top level", Code("if true {\n  extend date {\n    fn x() => 1\n  }\n}\n").Diagnostics.Single(d => d.Code == DiagnosticCodes.MisplacedElement).Message);
            Assert.Contains("<extend> needs the 'type' attribute", Analyze(Script("<extend><fn name=\"x\" value=\"1\" /></extend>")).Diagnostics.First(d => d.Code == DiagnosticCodes.MissingAttribute).Message);
            Assert.Contains("override=\"yes\"", Analyze(Script("<extend type=\"date\"><fn name=\"to_json\" value=\"1\" /></extend>")).Diagnostics.Single(d => d.Code == DiagnosticCodes.DuplicateDeclaration).Message);
            AssertClean(Analyze(Script("<extend type=\"date\"><fn name=\"to_json\" override=\"yes\" value=\"1\" /></extend>")));
        }

        [Fact]
        public void DeclaredTypesCanBeExtended()
        {
            DocumentAnalysis a = Code("type User {\n  name: string\n}\nextend User {\n  fn hello() => 'Hi ' + $this.name\n}\nu = $User->from({ name: 'Kyle' })\necho \"{ $u->hello() }\"\n");
            AssertClean(a);
            Assert.Contains("A User already has ->keys()", AssertSingle(Code("type User {\n  name: string\n}\nextend User {\n  fn keys() => 1\n}\n"), DiagnosticCodes.DuplicateDeclaration).Message);
        }

        [Fact]
        public void ImportsAndTheProjectBringExtensions()
        {
            string dir = TempProject(
                ("kscript.json", "{}"),
                ("extensions/date.kscript", Dates),
                ("extensions/text/slug.kscript", "extend string {\n  /// A URL slug.\n  fn slug() => $this->lower()\n}\n"),
                ("lib/lists.kscript", "extend list {\n  fn second() => $this[1]\n}\n"),
                ("main.kscript", string.Empty));
            string main = Path.Combine(dir, "main.kscript");
            string text = "import \"lib/lists.kscript\"\necho \"{ @today()->json() } { 'A B'->slug() } { [1, 2]->second() } { 'x'->slug(1) }\"\n";
            DocumentAnalysis a = Code(text, main);
            Assert.Equal(DiagnosticCodes.ArgumentCount, Assert.Single(a.Diagnostics).Code);
            Assert.Contains("slug", Labels("echo \"{ 'x'->‸ }\"\n", main));

            HoverResult hover = Service.GetHover(Service.Analyze(text, main), text.IndexOf("slug", StringComparison.Ordinal) + 1);
            Assert.Contains("A URL slug.", hover.Markdown);
            Assert.Contains("Added to string by `extend` in slug.kscript.", hover.Markdown);
            SourceLocation definition = Assert.Single(Service.GetDefinition(Service.Analyze(text, main), text.IndexOf("json", StringComparison.Ordinal) + 1));
            Assert.EndsWith("date.kscript", definition.Path);

            // Outside the project, nothing is added.
            Assert.Contains(Code("echo \"{ 'x'->slug() }\"\n", Path.Combine(Path.GetTempPath(), "elsewhere", "main.kscript")).Diagnostics, d => d.Code == DiagnosticCodes.UnknownMethod);
        }

        [Fact]
        public void TwoExtensionFilesAddingTheSameMethodIsReported()
        {
            string dir = TempProject(
                ("kscript.json", "{ \"extensions\": [\"ext\"] }"),
                ("ext/a.kscript", "extend string {\n  fn slug() => 1\n}\n"),
                ("ext/b.kscript", "extend string {\n  fn slug() => 2\n}\n"));
            string b = Path.Combine(dir, "ext", "b.kscript");
            Assert.Contains("Another extension file (a.kscript) already adds ->slug() to string",
                AssertSingle(Code(File.ReadAllText(b), b), DiagnosticCodes.DuplicateDeclaration).Message);
        }

        [Fact]
        public void ExtensionMethodsAreNotFunctionsOrExports()
        {
            Assert.Contains(Code(Dates + "echo @is_weekend()\n").Diagnostics, d => d.Code == DiagnosticCodes.UndefinedFunction || d.Code == DiagnosticCodes.UnknownCommand);
            DocumentAnalysis full = Service.Analyze(Dates, "/tmp/kscript-ext-lsp/lib.kscript");
            Assert.DoesNotContain(full.Diagnostics, d => d.Code == DiagnosticCodes.UnusedFunction);
        }

        [Fact]
        public void ConvertWritesExtendBothWays()
        {
            const string code = "/// Dates.\nextend date {\n  fn json() => $this->format('o')\n  override fn to_json() => 'x'\n}\n";
            string xml = KScript.Syntax.Markup.ScriptConverter.Convert(code, "a.kscript").Text;
            Assert.Contains("<extend type=\"date\" description=\"Dates.\">", xml);
            Assert.Contains("<fn name=\"to_json\" override=\"yes\" value=\"'x'\" />", xml);
            Assert.Equal(code, KScript.Syntax.Markup.ScriptConverter.Convert(xml, "a.ks.xml").Text);
        }
    }
}
