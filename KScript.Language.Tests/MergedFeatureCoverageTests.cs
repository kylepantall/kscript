using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Language.Formatting;
using KScript.Syntax;
using KScript.Syntax.Markup;

namespace KScript.Language.Tests
{
    /// <summary>
    /// Edge cases of import path completion, quick fixes, the formatter and kscript convert that the v0.4.0 pull
    /// requests left without a test.
    /// </summary>
    public sealed class MergedFeatureCoverageTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "kscript-merged-lang-" + Guid.NewGuid().ToString("N"));
        private readonly LanguageService service = new LanguageService();

        public MergedFeatureCoverageTests() => Directory.CreateDirectory(dir);

        public void Dispose()
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }

        private void Write(string name, string text)
        {
            string path = Path.Combine(dir, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text);
        }

        private List<string> ImportLabels(string file)
        {
            const string text = "import \"\"";
            var analysis = service.Analyze(text, Path.Combine(dir, file));
            return (service.GetCompletions(analysis, text.Length - 1) ?? new List<CompletionItem>()).Select(i => i.Label).ToList();
        }

        [Fact]
        public void Only_kscript_and_ks_xml_files_are_offered_as_scripts()
        {
            // No kscript.json anywhere above: the project is the file's own folder.
            Write("main.kscript", "echo \"hi\"");
            Write("old.xml", "<?xml version=\"1.0\"?>\n<kscript>\n</kscript>");
            Write("data.xml", "<people><person /></people>");
            Write("sub/older.ks", "<kscript />");
            Write("sub/lib.ks.xml", "<kscript />");

            // One extension per form: a plain .xml or .ks file is not a script, whatever it holds.
            List<string> labels = ImportLabels("main.kscript");
            Assert.Contains("sub/lib.ks.xml", labels);
            Assert.DoesNotContain("old.xml", labels);
            Assert.DoesNotContain("sub/older.ks", labels);
            Assert.DoesNotContain("data.xml", labels);
        }

        [Fact]
        public void Project_scripts_stop_at_a_depth()
        {
            Write("kscript.json", "{}");
            Write("main.kscript", "echo \"hi\"");
            Write("a/b/c/d/e/f/g/h/i/j/deep.kscript", "x = 1");
            Write("a/b/c/d/e/f/g/h/shallow.kscript", "x = 1");

            List<string> labels = ImportLabels("main.kscript");
            Assert.Contains("a/b/c/d/e/f/g/h/shallow.kscript", labels);
            Assert.DoesNotContain("a/b/c/d/e/f/g/h/i/j/deep.kscript", labels);
        }

        [Fact]
        public void Project_scripts_stop_at_a_limit()
        {
            Write("kscript.json", "{}");
            Write("main.kscript", "echo \"hi\"");
            for (int i = 0; i < 320; i++) Write($"many/s{i:D3}.kscript", "x = 1");

            List<string> labels = ImportLabels("main.kscript");
            Assert.Contains("many/s000.kscript", labels);
            Assert.DoesNotContain("many/s319.kscript", labels);
        }

        [Fact]
        public void A_missing_import_in_a_window_goes_inside_the_window()
        {
            const string text = "KScript.Window(title: \"Tests\") {\n  describe(name: x) {\n  }\n}\n";
            var files = new LanguageService(files: EmptyFileSystem.Instance);
            Diagnostic d = files.Analyze(text, Path.Combine(Path.GetTempPath(), "w.kscript")).Diagnostics
                .First(x => x.Code == DiagnosticCodes.MissingImport && x.Message.Contains("KScript.Test"));
            Assert.Equal("KScript.Window(title: \"Tests\") {\n  import KScript.Test\n  describe(name: x) {\n  }\n}\n",
                AnalyzerTests.Apply(text, d.Fixes[0].Changes));
        }

        [Fact]
        public void Pages_that_start_with_text_keep_it()
        {
            FormatResult result = Formatter.Format("Hello there   \n<p>hi</p>   \n", "page.html");
            Assert.True(result.Succeeded, result.Error);
            Assert.StartsWith("Hello there", result.Text);
            Assert.Contains("<p>hi</p>", result.Text);
        }

        [Fact]
        public void Xml_examples_in_comments_that_dont_parse_are_kept_as_written()
        {
            MarkupDocument doc = MarkupParser.Parse("<kscript>\n  <!-- Not this: <let name=\"a\" name=\"b\" /> -->\n  <echo>hi</echo>\n</kscript>", DocumentKind.Script);
            string code = ScriptConverter.ToCode(doc).Text;
            Assert.Contains("// Not this: <let name=\"a\" name=\"b\" />", code);
        }
    }
}
