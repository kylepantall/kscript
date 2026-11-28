using KScript.Language.Analysis;
using KScript.LanguageServer;

namespace KScript.Language.Tests
{
    /// <summary>kscript check analyses again only the files that changed, or whose analysis read something that did.</summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public sealed class CheckCacheTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "kscript-check-cache-" + Guid.NewGuid().ToString("N"));

        public CheckCacheTests()
        {
            Directory.CreateDirectory(dir);
            Write("kscript.json", "{ \"name\": \"demo\" }");
            Write("lib/strings.kscript", "/// Shouts.\nfn shout(text: string) => $text->upper()\n");
            Write("main.kscript", "import \"lib/strings.kscript\"\n\necho @shout('hi')\n");
            Write("other.kscript", "echo 'other'\n");
        }

        public void Dispose() => Directory.Delete(dir, recursive: true);

        private void Write(string name, string text)
        {
            string path = Path.Combine(dir, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text);
        }

        private (int Code, string Output) Check(params string[] options)
        {
            var output = new StringWriter();
            bool json = options.Contains("json");
            int code = CheckCommand.Run(new[] { dir }, output, json: json, all: options.Contains("all"));
            return (code, output.ToString());
        }

        [Fact]
        public void An_unchanged_project_is_not_analysed_again()
        {
            Assert.Contains("3 files checked: 0 errors", Check().Output);
            Assert.Contains("3 files checked (3 unchanged): 0 errors", Check().Output);
            Assert.Contains("3 files checked: 0 errors", Check("all").Output);
            Assert.True(File.Exists(CheckCache.PathFor(dir)));
        }

        [Fact]
        public void A_change_reaches_the_files_that_read_it()
        {
            Check();
            // main imports strings, so it is analysed again with it; other isn't.
            Write("lib/strings.kscript", "/// Shouts.\nfn shout(text: string, times: int) => $text->upper()\n");
            var (code, output) = Check();
            Assert.Equal(1, code);
            Assert.Contains("(1 unchanged)", output);
            Assert.Contains("main.kscript", output);

            // The same problem comes back from the cache, in text and in JSON, with its fixes.
            var (again, cached) = Check();
            Assert.Equal(1, again);
            Assert.Contains("(3 unchanged)", cached);
            Assert.Equal(output.Replace("(1 unchanged)", "(3 unchanged)"), cached);
            Assert.Contains("\"problems\"", Check("json").Output);
        }

        [Fact]
        public void A_file_that_starts_using_a_function_is_noticed_without_reading_every_file_again()
        {
            Write("lib/tools.kscript", "fn helper() => 1\n\nfn used() => @helper()\n");
            Write("main.kscript", "import \"lib/strings.kscript\"\nimport used from \"lib/tools.kscript\"\n\necho @shout('hi')\necho @used()\n");
            Check();
            Assert.Contains("(4 unchanged)", Check().Output);

            // other.kscript now mentions strings' function: strings.kscript is analysed again, as other is.
            Write("other.kscript", "import \"lib/strings.kscript\"\n\necho @shout('other')\n");
            string output = Check().Output;
            Assert.Contains("(2 unchanged)", output);
        }

        [Fact]
        public void Settings_and_damaged_caches_mean_analysing_again()
        {
            Check();
            Write("kscript.json", "{ \"name\": \"demo\", \"lint\": { \"warnings_as_errors\": true } }");
            Assert.DoesNotContain("unchanged", Check().Output);

            File.WriteAllText(CheckCache.PathFor(dir), "{ not json");
            Assert.DoesNotContain("unchanged", Check().Output);
            File.WriteAllText(CheckCache.PathFor(dir), "{ \"version\": 1, \"build\": \"0.0.1\", \"files\": {} }");
            Assert.DoesNotContain("unchanged", Check().Output);
            Assert.Contains("unchanged", Check().Output);
        }
    }
}
