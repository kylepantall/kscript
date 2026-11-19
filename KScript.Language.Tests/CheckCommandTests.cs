using KScript.LanguageServer;

namespace KScript.Language.Tests
{
    public sealed class CheckCommandTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "kscript-check-" + Guid.NewGuid().ToString("N"));

        public CheckCommandTests() => Directory.CreateDirectory(dir);

        public void Dispose() => Directory.Delete(dir, recursive: true);

        private string Write(string name, string text)
        {
            string path = Path.Combine(dir, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text);
            return path;
        }

        private static (int Code, string Output) Run(bool warningsAsErrors, params string[] paths)
        {
            StringWriter output = new StringWriter();
            int code = CheckCommand.Run(paths, output, warningsAsErrors);
            return (code, output.ToString());
        }

        [Fact]
        public void CleanFilesPass()
        {
            Write("ok.ks.xml", "<kscript><echo>hi</echo></kscript>");
            (int code, string output) = Run(false, dir);
            Assert.Equal(0, code);
            Assert.Contains("1 file checked: 0 errors, 0 warnings.", output);
        }

        [Fact]
        public void FolderChecksSkipMakeTemplates()
        {
            Write("ok.ks.xml", "<kscript><echo>hi</echo></kscript>");
            Write(Path.Combine("_templates", "script.ks.xml"), "<kscript><echo>{{ name }} $nope</echo></kscript>");
            (int code, string output) = Run(false, dir);
            Assert.Equal(0, code);
            Assert.Contains("1 file checked: 0 errors, 0 warnings.", output);
        }

        [Fact]
        public void ErrorsFailWithCompilerStyleLines()
        {
            string path = Write("bad.ks.xml", "<kscript>\n  <echo>$nope</echo>\n</kscript>");
            (int code, string output) = Run(false, path);
            Assert.Equal(1, code);
            Assert.Contains($"{path}(2,10): error KS4001: $nope is not defined.", output);
        }

        [Fact]
        public void WarningsFailOnlyWhenAskedTo()
        {
            Write("warn.ks.xml", "<kscript><echo colour=\"red\">x</echo></kscript>");
            Assert.Equal(0, Run(false, dir).Code);
            Assert.Equal(1, Run(true, dir).Code);
        }

        [Fact]
        public void FoldersFindScriptsAndSkipOtherXml()
        {
            Write("a.ks.xml", "<kscript></kscript>");
            Write("sub/b.ks", "<kscript></kscript>");
            Write("legacy.xml", "<kscript></kscript>");
            Write("config.xml", "<configuration><ehco /></configuration>");
            (int code, string output) = Run(false, dir);
            Assert.Equal(0, code);
            Assert.Contains("3 files checked", output);
        }

        [Fact]
        public void SiteFoldersCheckTheirPages()
        {
            Write("_layout.html", "<!doctype html>\n<html><body><main><slot /></main></body></html>");
            Write("index.html", "<layout src=\"_layout.html\" title=\"Home\">\n  <p class=\"lead\">Hi<br>there</p>\n</layout>");
            string bad = Write("about.md", "---\ntitle: About\n---\n\n# {$titel}\n");
            Write(Path.Combine("_templates", "page.html"), "<p>{{ title }} {$nope}</p>");
            (int code, string output) = Run(false, dir);
            Assert.Equal(1, code);
            Assert.Contains("3 files checked: 1 error", output);
            Assert.Contains($"{bad}(5,", output);
        }

        [Fact]
        public void NestedPagesFindTheLayoutAtTheSiteRoot()
        {
            Write("_layout.html", "<html><body><slot /></body></html>");
            string page = Write(Path.Combine("people", "[id].html"), "<layout src=\"_layout.html\" title=\"Person\"><p>hi</p></layout>");
            (int code, string output) = Run(true, page);
            Assert.True(code == 0, output);
        }

        [Fact]
        public void ComponentPropsAreKnownWithARelativePath()
        {
            Write("_layout.html", "<slot />");
            Write(Path.Combine("_components", "card.html"), "<div class=\"card\">{$title}</div>");
            string previous = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(dir);
            try
            {
                (int code, string output) = Run(true, Path.Combine("_components", "card.html"));
                Assert.True(code == 0, output);
            }
            finally
            {
                Directory.SetCurrentDirectory(previous);
            }
        }

        [Fact]
        public void HtmlOutsideASiteIsLeftAlone()
        {
            Write("notes.html", "<p>{$nope}</p>");
            (int code, string output) = Run(false, dir);
            Assert.Equal(2, code);
            Assert.Contains("No KScript files found", output);
        }

        [Fact]
        public void MissingPathsAreUsageErrors()
        {
            Assert.Equal(2, Run(false, Path.Combine(dir, "missing.ks.xml")).Code);
            Assert.Equal(2, Run(false).Code);
        }

        [Fact]
        public void TheSamplesAreClean()
        {
            (int code, string output) = Run(true, Path.Combine(AppContext.BaseDirectory, "samples"));
            Assert.True(code == 0, output);
        }
    }
}
