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

        [Fact]
        public void ProjectRulesChangeSeveritiesAndExcludePaths()
        {
            Write("kscript.json", "{ \"lint\": { \"exclude\": [\"drafts/**\"], \"rules\": { \"KS4001\": \"warning\", \"KS3003\": \"off\" } } }");
            Write("a.ks.xml", "<kscript>\n  <echo colour=\"red\">$nope</echo>\n</kscript>");
            Write("drafts/b.ks.xml", "<kscript><ehco /></kscript>");
            (int code, string output) = Run(false, dir);
            Assert.True(code == 0, output);
            Assert.Contains("(2,23): warning KS4001", output);
            Assert.DoesNotContain("KS3003", output);
            Assert.DoesNotContain("drafts", output);
            Assert.Contains("1 file checked: 0 errors, 1 warning.", output);

            // A file named on the command line is checked even when excluded, still with the project's rules.
            (code, output) = Run(false, Path.Combine(dir, "drafts", "b.ks.xml"));
            Assert.Equal(1, code);
            Assert.Contains("error KS3001", output);
        }

        [Fact]
        public void ProjectsCheckPagesAndCanFailOnWarnings()
        {
            Write("kscript.json", "{ \"lint\": { \"warningsAsErrors\": true } }");
            Write("index.html", "<p>{$nope}</p>");
            (int code, string output) = Run(false, dir);
            Assert.Equal(1, code);
            Assert.Contains("index.html(1,6): error KS4001", output);

            Write("index.html", "<layout src=\"missing.html\" title=\"x\"></layout>");
            (code, output) = Run(false, dir);
            Assert.True(code == 1, output);
            Assert.Contains("0 errors, 1 warning", output);
        }

        [Fact]
        public void StandardsSetRulesAndMissingOnesFailTheCheck()
        {
            Write("standards/team.kscript-standard.json", "{ \"extends\": \"kscript:strict\", \"lint\": { \"rules\": { \"KS3003\": \"off\" } } }");
            Write("kscript.json", "{ \"standard\": \"standards/team.kscript-standard.json\" }");
            Write("a.ks.xml", "<kscript>\n  <fn name=\"f\" params=\"n\">\n    <if test=\"$n\"><echo colour=\"red\">a</echo></if>\n    <else><echo>b</echo></else>\n  </fn>\n</kscript>");
            (int code, string output) = Run(false, dir);
            Assert.True(code == 1, output);
            Assert.Contains("error KS6001", output);
            Assert.DoesNotContain("KS3003", output);

            Write("kscript.json", "{ \"standard\": \"standards/nope.json\" }");
            (code, output) = Run(false, dir);
            Assert.Equal(2, code);
            Assert.Contains("can't find the standard 'standards/nope.json'", output);
        }

        [Fact]
        public void PrintConfigShowsWhereEachSettingCameFrom()
        {
            Write("shared/acme/kscript-standard.json", "{ \"name\": \"acme\", \"description\": \"Acme house style.\", \"extends\": \"kscript:recommended\", \"format\": { \"indent\": 4 }, \"lint\": { \"rules\": { \"KS6001\": \"error\" } } }");
            Write("app/kscript.json", "{ \"standard\": \"../shared/acme\", \"format\": { \"printWidth\": 100 } }");
            Write("app/a.ks.xml", "<kscript></kscript>");
            var output = new StringWriter();
            Assert.Equal(0, CheckCommand.PrintConfig(Path.Combine(dir, "app", "a.ks.xml"), output));
            string text = output.ToString();
            string Row(string key) => text.Split('\n').Single(l => l.TrimStart().StartsWith(key + " ", StringComparison.Ordinal));

            Assert.Contains("Project: " + Path.Combine(dir, "app", "kscript.json"), text);
            Assert.True(text.IndexOf("kscript:recommended", StringComparison.Ordinal) < text.IndexOf("../shared/acme/kscript-standard.json  \"acme\"", StringComparison.Ordinal), text);
            Assert.Contains("Acme house style.", text);
            Assert.EndsWith("../shared/acme/kscript-standard.json", Row("lint.rules.KS6001").TrimEnd());
            Assert.EndsWith("kscript:recommended", Row("lint.rules.KS6*").TrimEnd());
            Assert.Contains(" 4 ", Row("format.indent"));
            Assert.EndsWith("kscript.json", Row("format.printWidth").TrimEnd());
            Assert.EndsWith("default", Row("lint.warningsAsErrors").TrimEnd());

            Write("app/kscript.json", "{ \"standard\": \"kscript:nope\" }");
            output = new StringWriter();
            Assert.Equal(2, CheckCommand.PrintConfig(Path.Combine(dir, "app"), output));
            Assert.Contains("there is no built-in standard 'kscript:nope'", output.ToString());

            // Outside a project, the defaults with kscript:recommended.
            output = new StringWriter();
            Assert.Equal(0, CheckCommand.PrintConfig(Path.Combine(dir, "shared"), output));
            Assert.Contains("No kscript.json above", output.ToString());
            Assert.Contains("kscript:recommended", output.ToString());
        }

        [Fact]
        public void ProjectWarningsAndBadJsonAreReported()
        {
            Write("kscript.json", "{ \"lnit\": {} }");
            Write("a.ks.xml", "<kscript></kscript>");
            (int code, string output) = Run(false, dir);
            Assert.Equal(0, code);
            Assert.Contains("unknown setting 'lnit'", output);

            Write("kscript.json", "{ nope");
            (code, output) = Run(false, dir);
            Assert.Equal(2, code);
            Assert.Contains("not valid JSON", output);
        }

        [Fact]
        public void NoPathChecksTheProjectTheCurrentFolderIsIn()
        {
            Write("kscript.json", "{}");
            Write("sub/a.ks.xml", "<kscript><ehco /></kscript>");
            string previous = Directory.GetCurrentDirectory();
            try
            {
                Directory.SetCurrentDirectory(Path.Combine(dir, "sub"));
                (int code, string output) = Run(false);
                Assert.Equal(1, code);
                Assert.Contains("sub/a.ks.xml(1,11): error KS3001".Replace('/', Path.DirectorySeparatorChar), output.Replace("../", "").Replace("..\\", ""));
            }
            finally
            {
                Directory.SetCurrentDirectory(previous);
            }
        }

        [Fact]
        public void ANewProjectIsClean()
        {
            KScript.Scaffolding.ProjectScaffolder.Create(dir, "site");
            (int code, string output) = Run(true, Path.Combine(dir, "site"));
            Assert.True(code == 0, output);
            Assert.Contains("13 files checked: 0 errors, 0 warnings.", output);
        }
    }
}
