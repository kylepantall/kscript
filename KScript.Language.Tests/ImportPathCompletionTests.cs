using KScript.Language.Features;

namespace KScript.Language.Tests
{
    /// <summary>Completion of import paths: scripts in the project, in both formats, relative to the current file.</summary>
    public sealed class ImportPathCompletionTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "kscript-import-paths-" + Guid.NewGuid().ToString("N"));
        private readonly LanguageService service = new LanguageService();

        public ImportPathCompletionTests()
        {
            Write("kscript.json", "{}");
            Write("main.ks.xml", "<kscript />");
            Write("tour.kscript", "echo \"hi\"");
            Write("lib/strings.ks.xml", "<kscript />");
            Write("lib/maths.kscript", "fn double(x) => $x * 2");
            Write("lib/deep/util.ks", "<kscript />");
            Write("lib/notes.txt", "not a script");
            Write("node_modules/pkg/x.ks.xml", "<kscript />");
        }

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

        private List<string> Labels(string textWithCaret, string file)
        {
            int offset = textWithCaret.IndexOf('‸');
            string text = textWithCaret.Remove(offset, 1);
            var analysis = service.Analyze(text, Path.Combine(dir, file));
            return (service.GetCompletions(analysis, offset) ?? new List<CompletionItem>()).Select(i => i.Label).ToList();
        }

        [Theory]
        [InlineData("<kscript>\n  <import src=\"‸\" />\n</kscript>", "main.ks.xml")]
        [InlineData("import \"‸\"", "tour.kscript")]
        [InlineData("import double from \"‸\"", "tour.kscript")]
        public void EmptyPathListsEveryScriptInTheProject(string text, string file)
        {
            List<string> labels = Labels(text, file);
            Assert.Contains("lib/strings.ks.xml", labels);
            Assert.Contains("lib/maths.kscript", labels);
            Assert.Contains("lib/deep/util.ks", labels);
            Assert.Contains("lib/", labels);
            Assert.DoesNotContain("lib/notes.txt", labels);
            Assert.DoesNotContain(labels, l => l.Contains("node_modules"));
            Assert.DoesNotContain(file, labels);
        }

        [Theory]
        [InlineData("<kscript><import src=\"lib/‸\" /></kscript>", "main.ks.xml")]
        [InlineData("import \"lib/‸\" as m", "tour.kscript")]
        public void FolderPathListsThatFolder(string text, string file)
        {
            List<string> labels = Labels(text, file);
            Assert.Contains("strings.ks.xml", labels);
            Assert.Contains("maths.kscript", labels);
            Assert.Contains("deep/", labels);
            Assert.DoesNotContain("notes.txt", labels);
        }

        [Fact]
        public void PathsAreRelativeToTheCurrentFile()
        {
            List<string> labels = Labels("import \"‸\"", "lib/maths.kscript");
            Assert.Contains("strings.ks.xml", labels);
            Assert.Contains("deep/util.ks", labels);
            Assert.Contains("../tour.kscript", labels);
            Assert.Contains("../main.ks.xml", labels);
        }
    }
}
