using KScript.Language.Analysis;
using KScript.LanguageServer;
using KScript.Projects;
using KScript.Syntax;
using KScript.Syntax.Markup;

using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests
{
    /// <summary>Files kept in memory, in folders, so a whole project can be looked through.</summary>
    internal sealed class MemoryFiles : IFileSystem
    {
        private readonly Dictionary<string, string> files = new Dictionary<string, string>(StringComparer.Ordinal);

        public MemoryFiles Add(string path, string text)
        {
            files[Path.GetFullPath(path)] = text;
            return this;
        }

        public bool FileExists(string path) => path != null && files.ContainsKey(Path.GetFullPath(path));

        public bool DirectoryExists(string path) => files.Keys.Any(f => f.StartsWith(Path.GetFullPath(path) + Path.DirectorySeparatorChar, StringComparison.Ordinal));

        public string ReadText(string path) => files.TryGetValue(Path.GetFullPath(path), out string text) ? text : null;

        public IEnumerable<string> ListFiles(string directory, string pattern) =>
            files.Keys.Where(f => Path.GetDirectoryName(f) == Path.GetFullPath(directory)).ToList();

        public IEnumerable<string> ListDirectories(string directory)
        {
            string full = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
            return files.Keys.Where(f => f.StartsWith(full, StringComparison.Ordinal) && f.IndexOf(Path.DirectorySeparatorChar, full.Length) > 0)
                .Select(f => full + f.Substring(full.Length).Split(Path.DirectorySeparatorChar)[0])
                .Distinct()
                .ToList();
        }
    }

    /// <summary>KS6002-KS6005: functions, variables, parameters and imports that nothing uses.</summary>
    public class UnusedCodeTests
    {
        private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "kscript-unused"));

        private static string At(params string[] parts) => Path.Combine(new[] { Root }.Concat(parts).ToArray());

        private static DocumentAnalysis Check(string text, string file = "test.ks.xml", IFileSystem files = null) =>
            Analyzer.Analyze(text, At(file), files ?? new MemoryFiles());

        /// <summary>"code:marked text" for each unused-code diagnostic, in order.</summary>
        private static List<string> Unused(DocumentAnalysis a) =>
            a.Diagnostics.Where(d => DiagnosticCodes.IsUnnecessary(d.Code)).OrderBy(d => d.Span.Start).Select(d => d.Code + ":" + a.Source[d.Span]).ToList();

        private static Diagnostic Find(DocumentAnalysis a, string code, string marked) =>
            a.Diagnostics.Single(d => d.Code == code && a.Source[d.Span] == marked);

        private static string Fix(DocumentAnalysis a, Diagnostic d, string titleStart) =>
            AnalyzerTests.Apply(a.Source.Text, d.Fixes.Single(f => f.Title.StartsWith(titleStart, StringComparison.Ordinal)).Changes);

        #region Functions

        [Fact]
        public void FunctionsNothingCallsAreReported()
        {
            string text = Script("<fn name=\"used\" value=\"1\" />\n<fn name=\"unused\" value=\"2\" />\n<fn name=\"self\" params=\"n\"><return value=\"@self($n - 1)\" /></fn>\n"
                + "<fn name=\"_ignored\" value=\"3\" />\n<echo>{ @used() }</echo>");
            DocumentAnalysis a = Check(text);
            Assert.Equal(new[] { "KS6002:unused", "KS6002:self" }, Unused(a));

            Diagnostic d = Find(a, DiagnosticCodes.UnusedFunction, "unused");
            Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
            Assert.Equal("The function unused is never called.", d.Message);
            Assert.Equal(text.Replace("<fn name=\"unused\" value=\"2\" />\n", string.Empty), Fix(a, d, "Remove the function unused"));
            Assert.Contains("<fn name=\"_unused\"", Fix(a, d, "Rename to _unused"));
        }

        [Fact]
        public void AComponentsRenderAndStyleAreCalledByThePage()
        {
            const string badge = """
                import KScript.Web

                type Badge extends Component {
                  label: string

                  fn render(): Component => ```html
                  <span class="note-badge">{$label}</span>
                  ```

                  override fn style(): ComponentStyle => ```css
                  .note-badge { padding: 0.25rem; }
                  ```

                  fn helper() => 1
                }
                """;
            DocumentAnalysis a = Analyzer.Analyze(badge, "_components/badge.kscript");
            Assert.DoesNotContain(a.Diagnostics, d => d.Code == DiagnosticCodes.UnusedFunction && (d.Message.Contains("style") || d.Message.Contains("render")));
        }

        [Fact]
        public void RemovingAFunctionTakesItsDocComment()
        {
            const string code = "/// Says hi.\n/// @param who Someone.\nfn hello(who) {\n  echo \"Hi $who\"\n}\nfn main() => 1\necho @main()";
            DocumentAnalysis a = Check(code, "test.kscript");
            Assert.Equal(new[] { "KS6002:hello" }, Unused(a));
            Assert.Equal("fn main() => 1\necho @main()", Fix(a, Find(a, DiagnosticCodes.UnusedFunction, "hello"), "Remove"));

            const string xml = "<kscript>\n  <!--- Doubles a number. -->\n  <fn name=\"double\" params=\"x\" value=\"$x * 2\" />\n  <echo>hi</echo>\n</kscript>";
            DocumentAnalysis b = Check(xml);
            Assert.Equal("<kscript>\n  <echo>hi</echo>\n</kscript>", Fix(b, Find(b, DiagnosticCodes.UnusedFunction, "double"), "Remove"));

            // Something else on the same line stays, and so does the line.
            const string inline = "<kscript><fn name=\"f\" value=\"1\" /><echo>hi</echo></kscript>";
            DocumentAnalysis c = Check(inline);
            Assert.Equal("<kscript><echo>hi</echo></kscript>", Fix(c, Find(c, DiagnosticCodes.UnusedFunction, "f"), "Remove"));
        }

        [Fact]
        public void PublicFunctionsAndConfiguredMiddlewareAreNotReported()
        {
            MemoryFiles files = new MemoryFiles().Add(At("mw", "kscript.json"), "{ \"http\": { \"middleware\": [\"audit:5\", \"cors\"] } }");
            const string text = "<kscript>\n<!--- Kept for other projects.\n@public -->\n<fn name=\"api\" value=\"1\" />\n"
                + "<fn name=\"audit\" params=\"request, next\" value=\"$next($request)\" />\n<fn name=\"other\" value=\"1\" />\n</kscript>";
            DocumentAnalysis a = Check(text, Path.Combine("mw", "lib.ks.xml"), files);
            Assert.Equal(new[] { "KS6002:other" }, Unused(a));
        }

        [Fact]
        public void PagesAndUnsavedScriptsDontReportFunctions()
        {
            Assert.Empty(Unused(Check("<fn name=\"f\" value=\"1\" />", "page.html")));
            Assert.Equal(new[] { "KS6002:f" }, Unused(Analyzer.Analyze(Script("<fn name=\"f\" value=\"1\" />"))));
        }

        #endregion

        #region Project-wide

        private static MemoryFiles Project()
        {
            const string lib = "<kscript>\n  <fn name=\"slugify\" params=\"t\" value=\"$t\" />\n  <fn name=\"shout\" params=\"t\" value=\"$t\" />\n"
                + "  <fn name=\"whisper\" params=\"t\" value=\"$t\" />\n  <fn name=\"orphan\" value=\"1\" />\n"
                + "  <let name=\"PI\" value=\"3.14\" const=\"yes\" />\n  <const name=\"LOST\" value=\"1\" />\n</kscript>";
            return new MemoryFiles()
                .Add(At("p", "kscript.json"), "{}")
                .Add(At("p", "lib", "strings.ks.xml"), lib)
                .Add(At("p", "a.ks.xml"), "<kscript><import path=\"lib/strings.ks.xml\" names=\"slugify\" /><echo>{ @slugify('a') }</echo></kscript>")
                .Add(At("p", "c.ks.xml"), "<kscript><import path=\"lib/strings.ks.xml\" /><echo>{ @whisper('a') }</echo></kscript>")
                .Add(At("p", "sub", "b.ks.xml"), "<kscript><import path=\"../lib/strings.ks.xml\" as=\"str\" /><echo>{ @str.shout('a') } { str.PI }</echo></kscript>")
                .Add(At("p", "other.ks.xml"), "<kscript><echo>strings</echo></kscript>")
                .Add(At("p", "notes.txt"), "strings")
                .Add(At("p", "README.md"), "Import strings with `<import path=\"lib/strings.ks.xml\" />`.")
                .Add(At("p", "bin", "d.ks.xml"), "<kscript><import path=\"../lib/strings.ks.xml\" names=\"orphan\" /><echo>{ @orphan() }</echo></kscript>")
                .Add(At("p", ".hidden", "e.ks.xml"), "<kscript><import path=\"../lib/strings.ks.xml\" names=\"orphan\" /><echo>{ @orphan() }</echo></kscript>");
        }

        [Fact]
        public void FunctionsOtherFilesCallAreUsed()
        {
            MemoryFiles files = Project();
            string path = At("p", "lib", "strings.ks.xml");
            DocumentAnalysis a = Analyzer.Analyze(files.ReadText(path), path, files);
            Assert.Equal(new[] { "KS6002:orphan", "KS6003:LOST" }, Unused(a));
            Assert.Equal("The function orphan is never called. ../a.ks.xml and 2 other files import this script but don't call it.",
                Find(a, DiagnosticCodes.UnusedFunction, "orphan").Message);
            Assert.Equal("The constant $LOST is never used. ../a.ks.xml and 2 other files import this script but don't use it.",
                Find(a, DiagnosticCodes.UnusedVariable, "LOST").Message);

            ProjectUses uses = ProjectUsage.Find(path, files.ReadText(path), files);
            Assert.Equal(new[] { "PI", "shout", "slugify", "whisper" }, uses.Names.OrderBy(n => n, StringComparer.Ordinal));
            Assert.Equal(3, uses.Importers.Count);
            Assert.DoesNotContain(uses.Importers, file => file.EndsWith("README.md", StringComparison.Ordinal));
        }

        [Fact]
        public void AnImportedMethodCallMarksOnlyItsOwnDeclarationAsUsed()
        {
            string path = At("p", "account.kscript");
            var files = new MemoryFiles()
                .Add(At("p", "kscript.json"), "{}")
                .Add(path, "type Account { name: string; fn label() => $this.name }\nfn label() => 'module'\n")
                .Add(At("p", "main.kscript"), "import \"account.kscript\"\naccount = Account(name: 'Ada')\necho $account->label()");
            string importer = At("p", "main.kscript");
            Analyzer.Analyze(files.ReadText(importer), importer, files);
            var analysis = Analyzer.Analyze(files.ReadText(path), path, files);
            var unused = Assert.Single(analysis.Diagnostics, d => d.Code == DiagnosticCodes.UnusedFunction);
            Assert.Equal(files.ReadText(path).LastIndexOf("label", StringComparison.Ordinal), unused.Span.Start);
        }

        [Fact]
        public void OneImporterIsNamed()
        {
            MemoryFiles files = new MemoryFiles()
                .Add(At("one", "kscript.json"), "{}")
                .Add(At("one", "lib.ks.xml"), "<kscript><fn name=\"a\" value=\"1\" /><fn name=\"b\" value=\"1\" /><const name=\"C\" value=\"1\" /></kscript>")
                .Add(At("one", "main.ks.xml"), "<kscript><import path=\"lib.ks.xml\" names=\"a\" /><echo>{ @a() }</echo></kscript>");
            string path = At("one", "lib.ks.xml");
            DocumentAnalysis a = Analyzer.Analyze(files.ReadText(path), path, files);
            Assert.Equal("The function b is never called. main.ks.xml imports this script but doesn't call it.", Find(a, DiagnosticCodes.UnusedFunction, "b").Message);
            Assert.Equal("The constant $C is never used. main.ks.xml imports this script but doesn't use it.", Find(a, DiagnosticCodes.UnusedVariable, "C").Message);
        }

        [Fact]
        public void OutsideAProjectOnlyTheScriptsFolderIsSearched()
        {
            MemoryFiles files = new MemoryFiles()
                .Add(At("loose", "lib.ks.xml"), "<kscript><fn name=\"near\" value=\"1\" /><fn name=\"far\" value=\"1\" /></kscript>")
                .Add(At("loose", "main.ks.xml"), "<kscript><import path=\"lib.ks.xml\" as=\"lib\" /><echo>{ @lib.near() }</echo></kscript>")
                .Add(At("loose", "deeper", "x.ks.xml"), "<kscript><import path=\"../lib.ks.xml\" as=\"lib\" /><echo>{ @lib.far() }</echo></kscript>");
            string path = At("loose", "lib.ks.xml");
            Assert.Equal(new[] { "KS6002:far" }, Unused(Analyzer.Analyze(files.ReadText(path), path, files)));
        }

        [Fact]
        public void RouteControllersAreUsed()
        {
            const string people = "<kscript><fn name=\"index\" value=\"1\" /><fn name=\"store\" value=\"1\" /><fn name=\"show\" params=\"id\" value=\"$id\" />"
                + "<fn name=\"update\" value=\"1\" /><fn name=\"destroy\" value=\"1\" /><fn name=\"helper\" value=\"1\" /></kscript>";
            List<string> UnusedWith(string routes)
            {
                MemoryFiles files = new MemoryFiles()
                    .Add(At("h", "kscript.json"), "{}")
                    .Add(At("h", "controllers", "people.ks.xml"), people)
                    .Add(At("h", "routes.ks.xml"), "<KScript.Routes>\n  <import path=\"controllers/people.ks.xml\" as=\"people\" />\n" + routes + "\n</KScript.Routes>");
                string path = At("h", "controllers", "people.ks.xml");
                return Unused(Analyzer.Analyze(people, path, files));
            }

            Assert.Equal(new[] { "KS6002:destroy" }, UnusedWith("<Resource path=\"/people\" to=\"people\" except=\"destroy\" />\n<Get path=\"/x\" to=\"people.helper\" />"));
            Assert.Equal(new[] { "KS6002:store", "KS6002:update", "KS6002:destroy", "KS6002:helper" }, UnusedWith("<Resource path=\"/people\" to=\"people\" only=\"index, show\" />"));
            Assert.Equal(6, UnusedWith("<Resource path=\"/people\" to=\"nobody\" />\n<Resource path=\"/x\" />").Count);

            // The route file's import counts as used by the Resource.
            MemoryFiles routesOnly = new MemoryFiles().Add(At("h2", "controllers", "people.ks.xml"), people);
            DocumentAnalysis routesFile = Check("<KScript.Routes>\n  <import path=\"controllers/people.ks.xml\" as=\"people\" />\n  <Resource path=\"/people\" to=\"people\" />\n</KScript.Routes>",
                Path.Combine("h2", "routes.ks.xml"), routesOnly);
            Assert.Empty(Unused(routesFile));
        }

        [Fact]
        public void ProjectUsageEdgeCases()
        {
            Assert.Empty(ProjectUsage.Find(null, "", null).Names);
            Assert.Empty(ProjectUsage.Find(At("nowhere", "x.ks.xml"), "", new MemoryFiles()).Importers);
            Assert.Empty(((IFileSystem)EmptyFileSystem.Instance).ListDirectories(Root));

            Assert.Empty(DiskFileSystem.Instance.ListDirectories(At("missing-folder")));
            string temp = Directory.CreateTempSubdirectory("kscript-unused-").FullName;
            try
            {
                Directory.CreateDirectory(Path.Combine(temp, "inner"));
                Assert.Equal(new[] { Path.Combine(temp, "inner") }, DiskFileSystem.Instance.ListDirectories(temp));
            }
            finally
            {
                Directory.Delete(temp, recursive: true);
            }
            if (Directory.Exists("/proc/1/map_files"))
            {
                Assert.Empty(DiskFileSystem.Instance.ListDirectories("/proc/1/map_files"));
            }

            MemoryFiles memory = new MemoryFiles().Add(At("ws", "sub", "a.ks.xml"), "<kscript />");
            Assert.Equal(new[] { At("ws", "sub") }, new Workspace(memory).ListDirectories(At("ws")));
        }

        #endregion

        #region Variables

        [Fact]
        public void VariablesNothingReadsAreReported()
        {
            string text = Script("<let name=\"a\" value=\"1\" />\n<let name=\"b\" value=\"1\" />\n<set name=\"b\" value=\"2\" />\n"
                + "<for each=\"i\" in=\"0..3\"><echo>x</echo></for>\n<try><echo>x</echo><catch as=\"err\"><echo>failed</echo></catch></try>\n"
                + "<let>c = 1; d = 2</let><echo>$d</echo>\n<let name=\"_skip\" value=\"1\" />\n<enum name=\"Size\" values=\"s, m\" />\n"
                + "<type name=\"Point\" fields=\"x: int\" />\n<const name=\"TOP\" value=\"1\" />");
            DocumentAnalysis a = Check(text);
            Assert.Equal(new[] { "KS6003:a", "KS6003:b", "KS6003:i", "KS6003:err", "KS6003:c", "KS6003:TOP" }, Unused(a));

            Diagnostic unused = Find(a, DiagnosticCodes.UnusedVariable, "a");
            Assert.Equal("The variable $a is never used.", unused.Message);
            Assert.Equal(text.Replace("<let name=\"a\" value=\"1\" />\n", string.Empty), Fix(a, unused, "Remove $a"));

            Diagnostic written = Find(a, DiagnosticCodes.UnusedVariable, "b");
            Assert.Equal("The variable $b is set but its value is never read.", written.Message);
            Assert.Single(written.Fixes);
            string renamed = Fix(a, written, "Rename to _b");
            Assert.Contains("<let name=\"_b\"", renamed);
            Assert.Contains("<set name=\"_b\"", renamed);

            Assert.Equal("The loop variable $i is never used.", Find(a, DiagnosticCodes.UnusedVariable, "i").Message);
            Assert.Single(Find(a, DiagnosticCodes.UnusedVariable, "c").Fixes);
            Assert.Equal("The constant $TOP is never used.", Find(a, DiagnosticCodes.UnusedVariable, "TOP").Message);
        }

        [Fact]
        public void CodeFormatVariables()
        {
            const string code = "x = 1\ny = 1\ny += 1\nw = 3\necho $w\nfn f() {\n  const k = 1\n  return 2\n}\necho @f()";
            DocumentAnalysis a = Check(code, "test.kscript");
            Assert.Equal(new[] { "KS6003:x", "KS6003:y", "KS6003:k" }, Unused(a));
            Assert.Equal(code.Substring("x = 1\n".Length), Fix(a, Find(a, DiagnosticCodes.UnusedVariable, "x"), "Remove"));
            Assert.Equal(code.Replace("  const k = 1\n", string.Empty), Fix(a, Find(a, DiagnosticCodes.UnusedVariable, "k"), "Remove"));
            Assert.Contains("set but its value is never read", Find(a, DiagnosticCodes.UnusedVariable, "y").Message);
        }

        [Fact]
        public void PagesOnlyCheckFunctionLocals()
        {
            const string page = "<let name=\"title\" value=\"'x'\" />\n<fn name=\"f\" params=\"p\"><let name=\"local\" value=\"1\" /><return value=\"$p\" /></fn>\n"
                + "<live id=\"c\"><state name=\"q\" value=\"''\" /><input bind=\"q\" /></live>";
            Assert.Equal(new[] { "KS6003:local" }, Unused(Check(page, "page.html")));
        }

        [Fact]
        public void BoundVariablesAreRead()
        {
            const string window = "<KScript.Window title=\"T\">\n  <let name=\"draft\" value=\"''\" />\n  <TextBox bind=\"draft\" />\n</KScript.Window>";
            Assert.Empty(Unused(Check(window)));
        }

        #endregion

        #region Parameters

        [Fact]
        public void ParametersNothingReadsAreReported()
        {
            string text = Script("<fn name=\"f\" params=\"a, b, c\"><return value=\"$b\" /></fn>\n<fn name=\"g\" params=\"only\"><return value=\"1\" /></fn>\n"
                + "<fn name=\"dead\" params=\"z\" value=\"1\" />\n<fn name=\"skip\" params=\"_q\" value=\"1\" />\n"
                + "<let name=\"h\" value=\"fn(x, y) => $x\" />\n<let name=\"k\" value=\"fn(x, y) => $y\" />\n<let name=\"m\" value=\"fn(_x) => 1\" />\n"
                + "<echo>{ @f(1, 2) } { @g() } { @skip(1) } { $h(1, 2) } { $k(1, 2) } { $m(1) }</echo>");
            DocumentAnalysis a = Check(text);
            Assert.Equal(new[] { "KS6004:a", "KS6004:c", "KS6004:only", "KS6002:dead", "KS6004:y" }, Unused(a));

            Diagnostic first = Find(a, DiagnosticCodes.UnusedParameter, "a");
            Assert.Equal("The parameter $a is never used.", first.Message);
            Assert.Single(first.Fixes);
            Assert.Contains("params=\"a, b\"", Fix(a, Find(a, DiagnosticCodes.UnusedParameter, "c"), "Remove the parameter $c"));
            Assert.Contains("<fn name=\"g\">", Fix(a, Find(a, DiagnosticCodes.UnusedParameter, "only"), "Remove"));
            Diagnostic lambda = Find(a, DiagnosticCodes.UnusedParameter, "y");
            Assert.Equal("The lambda parameter $y is never used.", lambda.Message);
            Assert.Contains("fn(x, _y) => $x", Fix(a, lambda, "Rename"));
        }

        [Fact]
        public void CodeFormatParameters()
        {
            // No call passes x or b, so removing them leaves every call working.
            const string code = "fn one(x = 0) => 1\nfn two(a, b = 0) => $a\necho @one() + @two(1)";
            DocumentAnalysis a = Check(code, "test.kscript");
            Assert.Equal(new[] { "KS6004:x", "KS6004:b" }, Unused(a));
            Assert.StartsWith("fn one() => 1\n", Fix(a, Find(a, DiagnosticCodes.UnusedParameter, "x"), "Remove"));
            Assert.Contains("fn two(a) => $a", Fix(a, Find(a, DiagnosticCodes.UnusedParameter, "b"), "Remove"));

            // A call that passes the parameter would stop working, so there is only the rename.
            DocumentAnalysis passed = Check("fn two(a, b) => $a\necho @two(1, 2)", "test.kscript");
            Assert.Equal(new[] { "Rename to _b (unused on purpose)" }, Find(passed, DiagnosticCodes.UnusedParameter, "b").Fixes.Select(f => f.Title));
        }

        #endregion

        #region Imports

        private static MemoryFiles Lib() => new MemoryFiles().Add(At("i", "lib.ks.xml"),
            "<kscript><fn name=\"one\" value=\"1\" /><fn name=\"two\" value=\"2\" /><fn name=\"three\" value=\"3\" /></kscript>");

        private static DocumentAnalysis Imports(string body) => Check(Script(body), Path.Combine("i", "main.ks.xml"), Lib());

        [Fact]
        public void UnusedScriptImports()
        {
            DocumentAnalysis alias = Imports("<import path=\"lib.ks.xml\" as=\"lib\" />");
            Diagnostic d = Find(alias, DiagnosticCodes.UnusedImport, "lib");
            Assert.Equal("Nothing imported as lib is used.", d.Message);
            Assert.Equal(Script(string.Empty).Replace("\n\n", "\n"), Fix(alias, d, "Remove the import"));

            DocumentAnalysis aliasAndNames = Imports("<import path=\"lib.ks.xml\" as=\"lib\" names=\"one\" /><echo>{ @one() }</echo>");
            Assert.Contains("<import path=\"lib.ks.xml\" names=\"one\" />", Fix(aliasAndNames, Find(aliasAndNames, DiagnosticCodes.UnusedImport, "lib"), "Remove as"));

            DocumentAnalysis names = Imports("<import path=\"lib.ks.xml\" names=\"one, two, three\" /><echo>{ @two() }</echo>");
            Assert.Equal(new[] { "KS6005:one", "KS6005:three" }, Unused(names));
            Assert.Equal("one is imported but never used.", Find(names, DiagnosticCodes.UnusedImport, "one").Message);
            Assert.Contains("names=\"two, three\"", Fix(names, Find(names, DiagnosticCodes.UnusedImport, "one"), "Remove one"));
            Assert.Contains("names=\"one, two\"", Fix(names, Find(names, DiagnosticCodes.UnusedImport, "three"), "Remove three"));

            DocumentAnalysis spaced = Imports("<import path=\"lib.ks.xml\" names=\"one , two\" /><echo>{ @two() }</echo>");
            Assert.Contains("names=\"two\"", Fix(spaced, Find(spaced, DiagnosticCodes.UnusedImport, "one"), "Remove one"));

            // Windows line endings and trailing spaces go with the removed line.
            DocumentAnalysis crlf = Check("<kscript>\r\n  <import path=\"lib.ks.xml\" as=\"lib\" />  \r\n</kscript>", Path.Combine("i", "main.ks.xml"), Lib());
            Assert.Equal("<kscript>\r\n</kscript>", Fix(crlf, Find(crlf, DiagnosticCodes.UnusedImport, "lib"), "Remove the import"));

            DocumentAnalysis lone = Imports("<import path=\"lib.ks.xml\" names=\"three\" />");
            Assert.DoesNotContain("<import", Fix(lone, Find(lone, DiagnosticCodes.UnusedImport, "three"), "Remove the import"));

            DocumentAnalysis loneWithAlias = Imports("<import path=\"lib.ks.xml\" as=\"lib\" names=\"two\" /><echo>{ @lib.one() }</echo>");
            Assert.Contains("<import path=\"lib.ks.xml\" as=\"lib\" />", Fix(loneWithAlias, Find(loneWithAlias, DiagnosticCodes.UnusedImport, "two"), "Remove names"));

            DocumentAnalysis all = Imports("<import path=\"lib.ks.xml\" />");
            Assert.Equal("Nothing from lib.ks.xml is used.", Find(all, DiagnosticCodes.UnusedImport, "lib.ks.xml").Message);

            Assert.Empty(Unused(Imports("<import path=\"lib.ks.xml\" names=\"nope\" />")));
            Assert.Empty(Unused(Imports("<import path=\"missing.ks.xml\" />")));
            Assert.Empty(Unused(Imports("<import path=\"lib.ks.xml\" /><echo>{ @three() }</echo>")));
            Assert.Empty(Unused(Imports("<import path=\"lib.ks.xml\" as=\"lib\" /><call fn=\"lib.two\" />")));
        }

        [Fact]
        public void UnusedPackageImports()
        {
            Assert.Equal("Nothing from KScript.Math is used.", Find(Imports("<import package=\"KScript.Math\" />"), DiagnosticCodes.UnusedImport, "KScript.Math").Message);
            Assert.Equal(new[] { "KS6005:KScript.Math" }, Unused(Imports("<import package=\"KScript.Math\" as=\"m\" />")));
            Assert.Empty(Unused(Imports("<import package=\"KScript.Math\" /><echo>{ @sqrt(4) }</echo>")));
            Assert.Empty(Unused(Imports("<import package=\"KScript.Math\" /><echo>{ 4->sqrt() }</echo>")));
            Assert.Empty(Unused(Imports("<import package=\"KScript.Math\" /><echo>{ $pi }</echo>")));
            Assert.Empty(Unused(Imports("<import package=\"KScript.Math\" as=\"m\" /><echo>{ @m.hypot(3, 4) }</echo>")));
            Assert.Empty(Unused(Imports("<import package=\"KScript.Math\" /><call fn=\"hypot\" args=\"3, 4\" />")));
            Assert.Empty(Unused(Imports("<import package=\"KScript.Terminal\" /><input into=\"name\">Name? </input><echo>$name</echo>")));
            Assert.Empty(Unused(Imports("<import package=\"KScript.Core\" /><import package=\"KScript.Nope\" />")));
            Assert.Empty(Unused(Check("<import package=\"KScript.Math\" />", "page.html")));
        }

        [Fact]
        public void CodeFormatImports()
        {
            MemoryFiles files = new MemoryFiles().Add(At("ci", "lib.kscript"), "fn one() => 1\nfn two() => 2");
            DocumentAnalysis a = Check("import one, two from \"lib.kscript\"\nimport KScript.Math\necho @two()", Path.Combine("ci", "main.kscript"), files);
            Assert.Equal(new[] { "KS6005:one", "KS6005:KScript.Math" }, Unused(a));
            Assert.StartsWith("import two from", Fix(a, Find(a, DiagnosticCodes.UnusedImport, "one"), "Remove one"));
            Assert.Equal("import one, two from \"lib.kscript\"\necho @two()", Fix(a, Find(a, DiagnosticCodes.UnusedImport, "KScript.Math"), "Remove the import"));
        }

        #endregion

        #region Settings and editors

        [Fact]
        public void StandardsTurnTheRulesOnAndOff()
        {
            const string text = "<kscript><fn name=\"f\" value=\"1\" /></kscript>";
            List<Diagnostic> byDefault = LintRules.Apply(Analyzer.Analyze(text).Diagnostics, null, null);
            Assert.Equal(DiagnosticSeverity.Warning, Assert.Single(byDefault).Severity);

            ProjectConfig off = ProjectConfig.Parse("{ \"lint\": { \"rules\": { \"KS6002\": \"off\" } } }", At("cfg", "kscript.json"));
            Assert.Empty(LintRules.Apply(Analyzer.Analyze(text).Diagnostics, off, At("cfg", "a.ks.xml")));

            ProjectConfig strict = ProjectConfig.Parse("{ \"standard\": \"kscript:strict\" }", At("cfg", "kscript.json"));
            Assert.Equal(DiagnosticSeverity.Error, Assert.Single(LintRules.Apply(Analyzer.Analyze(text).Diagnostics, strict, At("cfg", "a.ks.xml"))).Severity);
        }

        [Fact]
        public void OnlyUnusedCodeIsUnnecessary()
        {
            Assert.True(DiagnosticCodes.IsUnnecessary(DiagnosticCodes.UnusedFunction));
            Assert.True(DiagnosticCodes.IsUnnecessary(DiagnosticCodes.UnusedVariable));
            Assert.True(DiagnosticCodes.IsUnnecessary(DiagnosticCodes.UnusedParameter));
            Assert.True(DiagnosticCodes.IsUnnecessary(DiagnosticCodes.UnusedImport));
            Assert.False(DiagnosticCodes.IsUnnecessary(DiagnosticCodes.PreferEarlyExit));
        }

        [Fact]
        public void PublicDocTag()
        {
            DocComment doc = DocComment.Parse("Kept for others.\n@public");
            Assert.True(doc.IsPublic);
            Assert.Equal("Kept for others.", doc.Summary);
            Assert.True(DocComment.Parse("@public").IsPublic);
            Assert.False(DocComment.Parse("Plain.").IsPublic);
        }

        #endregion
    }

    /// <summary>Tests that change ProjectUsage's limits, so they don't run alongside the others.</summary>
    [CollectionDefinition(nameof(ProjectUsageLimitTests), DisableParallelization = true)]
    public sealed class ProjectUsageLimitCollection
    {
    }

    [Collection(nameof(ProjectUsageLimitTests))]
    public class ProjectUsageLimitTests
    {
        private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "kscript-unused-limits"));

        [Fact]
        public void BigProjectsAreCutShortAndTheCacheIsReused()
        {
            MemoryFiles files = new MemoryFiles().Add(Path.Combine(Root, "kscript.json"), "{}");
            string lib = Path.Combine(Root, "lib.ks.xml");
            files.Add(lib, "<kscript><fn name=\"a\" value=\"1\" /></kscript>");
            for (int i = 0; i < 5; i++)
            {
                files.Add(Path.Combine(Root, $"use{i}.ks.xml"), "<kscript><import path=\"lib.ks.xml\" names=\"a\" /><echo>{ @a() }</echo></kscript>");
            }

            int maxFiles = ProjectUsage.MaxFiles;
            int cacheLimit = ProjectUsage.CacheLimit;
            try
            {
                ProjectUsage.ClearCache();
                Assert.Equal(5, ProjectUsage.Find(lib, files.ReadText(lib), files).Importers.Count);
                // The same texts again come from the cache.
                Assert.Equal(5, ProjectUsage.Find(lib, files.ReadText(lib), files).Importers.Count);

                ProjectUsage.CacheLimit = 2;
                Assert.Equal(5, ProjectUsage.Find(lib, files.ReadText(lib) + " ", files).Importers.Count);

                ProjectUsage.MaxFiles = 3;
                Assert.Equal(2, ProjectUsage.Find(lib, files.ReadText(lib), files).Importers.Count);
            }
            finally
            {
                ProjectUsage.MaxFiles = maxFiles;
                ProjectUsage.CacheLimit = cacheLimit;
                ProjectUsage.ClearCache();
            }
        }
    }
}
