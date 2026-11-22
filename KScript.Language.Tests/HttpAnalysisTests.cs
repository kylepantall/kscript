using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Syntax;

namespace KScript.Language.Tests
{
    /// <summary>The editor and kscript check on KScript.Http route files: controller names, middleware and route paths.</summary>
    public class HttpAnalysisTests
    {
        private sealed class FakeFiles : IFileSystem
        {
            private readonly Dictionary<string, string> files;
            public FakeFiles(Dictionary<string, string> files) => this.files = files.ToDictionary(k => Path.GetFullPath(k.Key), k => k.Value);
            public bool FileExists(string path) => files.ContainsKey(Path.GetFullPath(path));
            public bool DirectoryExists(string path) => files.Keys.Any(f => f.StartsWith(Path.GetFullPath(path) + Path.DirectorySeparatorChar, StringComparison.Ordinal));
            public string ReadText(string path) => files.TryGetValue(Path.GetFullPath(path), out string t) ? t : null;
            public IEnumerable<string> ListFiles(string directory, string pattern) =>
                files.Keys.Where(f => Path.GetDirectoryName(f) == Path.GetFullPath(directory) && f.EndsWith(pattern.TrimStart('*'), StringComparison.Ordinal));
        }

        private static readonly string Site = Path.Combine(Path.GetTempPath(), "kscript-http-analysis");

        private static readonly FakeFiles Files = new FakeFiles(new Dictionary<string, string>
        {
            [Path.Combine(Site, "kscript.json")] = "{ \"http\": { \"groups\": { \"admin\": [\"auth\", \"log\"] } } }",
            [Path.Combine(Site, "controllers", "people.ks.xml")] = "<kscript>\n<import package=\"KScript.Http\" />\n<fn name=\"index\" value=\"[]\" />\n<fn name=\"show\" params=\"id\" value=\"{ id: $id }\" />\n</kscript>",
        });

        private static readonly LanguageService Service = new LanguageService(files: Files);

        private static DocumentAnalysis Analyze(string body, string file = "routes.ks.xml") =>
            TestHelpers.WithoutUnused(Analyzer.Analyze("<KScript.Routes>\n  <import src=\"controllers/people.ks.xml\" as=\"people\" />\n" + body + "\n</KScript.Routes>", Path.Combine(Site, file), Files));

        [Theory]
        [InlineData("<Get path=\"/people/{id}\" to=\"people.show\" where=\"id: int\" name=\"people.show\" />")]
        [InlineData("<Get path=\"/files/{path*}\" redirect=\"/\" />")]
        [InlineData("<Middleware use=\"log, cors, throttle:30, auth:ADMIN_TOKEN, admin\" />")]
        [InlineData("<Group prefix=\"/a\" middleware=\"auth\"><Get path=\"/\" to=\"people.index\" /></Group>")]
        [InlineData("<if test=\"true\"><Get path=\"/x\" to=\"people.index\" /></if>")]
        [InlineData("<Get path=\"/ping\"><return value=\"'pong'\" /></Get>")]
        [InlineData("<Resource path=\"/people\" to=\"people\" only=\"index, show\" />")]
        [InlineData("<fn name=\"check\" params=\"request, next\" value=\"$next($request)\" /><Get path=\"/\" to=\"people.index\" middleware=\"check\" />")]
        public void ValidRoutesAreClean(string body) => TestHelpers.AssertClean(Analyze(body));

        [Theory]
        [InlineData("<Get path=\"/\" to=\"people.shwo\" />", "The import 'people' has no 'shwo'.")]
        [InlineData("<Get path=\"/\" to=\"staff.show\" />", "Nothing is imported as 'staff'.")]
        [InlineData("<Get path=\"/\" to=\"missing\" />", "No function named 'missing'.")]
        [InlineData("<Middleware use=\"lgo\" />", "No function named 'lgo'.")]
        public void MistakesAreReported(string body, string message)
        {
            Diagnostic d = TestHelpers.AssertSingle(Analyze(body), DiagnosticCodes.UndefinedFunction);
            Assert.StartsWith(message, d.Message);
        }

        [Fact]
        public void GroupsComeFromTheNearestKscriptJson()
        {
            string routes = "<KScript.Routes>\n  <Middleware use=\"admin\" />\n  <Middleware use=\"admin\" />\n</KScript.Routes>";
            // kscript.json is two folders up, and the group is used twice (read once).
            TestHelpers.AssertClean(Analyzer.Analyze(routes, Path.Combine(Site, "api", "v1", "routes.ks.xml"), Files));

            // A kscript.json that can't be read means no groups, so admin is an unknown function.
            var broken = new FakeFiles(new Dictionary<string, string> { [Path.Combine(Site, "kscript.json")] = "{ nope" });
            DocumentAnalysis a = Analyzer.Analyze(routes, Path.Combine(Site, "routes.ks.xml"), broken);
            Assert.Contains(a.Diagnostics, d => d.Code == DiagnosticCodes.UndefinedFunction && d.Message.Contains("'admin'"));
        }

        [Fact]
        public void ListsCanBeArrays()
        {
            DocumentAnalysis a = Analyzer.Analyze(
                "import KScript.Http\nimport \"controllers/people.ks.xml\" as people\n\nRoutes {\n  fn stamp(request: map, next: fn) => $next($request)\n  Middleware(use: [stamp, cors, throttle:30, admin])\n  Resource(path: \"/people\", to: people, only: [index, show])\n  Route(methods: [get, post], path: \"/both\", to: people.index)\n  Group(middleware: [auth]) {\n    Get(path: \"/\", to: people.show)\n  }\n}\n",
                Path.Combine(Site, "routes.kscript"), Files);
            TestHelpers.AssertClean(a);
        }

        [Fact]
        public void NamesInAnArrayAreChecked()
        {
            DocumentAnalysis a = Analyzer.Analyze("import KScript.Http\n\nRoutes {\n  Middleware(use: [cors, stmap])\n}\n", Path.Combine(Site, "routes.kscript"), Files);
            Diagnostic d = TestHelpers.AssertSingle(a, DiagnosticCodes.UndefinedFunction);
            Assert.StartsWith("No function named 'stmap'", d.Message);
            Assert.Equal("stmap", a.Markup.Source.Text.Substring(d.Span.Start, d.Span.Length));
        }

        [Fact]
        public void AStringListSuggestsAnArray()
        {
            string text = "import KScript.Http\n\nRoutes {\n  fn stamp(request: map, next: fn) => $next($request)\n  Middleware(use: \"stamp, cors\")\n}\n";
            DocumentAnalysis a = Analyzer.Analyze(text, Path.Combine(Site, "routes.kscript"), Files);
            Diagnostic d = Assert.Single(a.Diagnostics, x => x.Code == DiagnosticCodes.PreferArrayList);
            Assert.Contains("use: [stamp, cors]", d.Message);
            Assert.Equal("import KScript.Http\n\nRoutes {\n  fn stamp(request: map, next: fn) => $next($request)\n  Middleware(use: [stamp, cors])\n}\n", AnalyzerTests.Apply(text, d.Fixes[0].Changes));

            // XML attributes are always text, and one name needs no list.
            Assert.DoesNotContain(Analyze("<fn name=\"stamp\" params=\"request, next\" value=\"$next($request)\" /><Middleware use=\"stamp, cors\" />").Diagnostics, x => x.Code == DiagnosticCodes.PreferArrayList);
            Assert.DoesNotContain(Analyzer.Analyze("import KScript.Http\n\nRoutes {\n  Middleware(use: \"cors\")\n}\n", Path.Combine(Site, "routes.kscript"), Files).Diagnostics, x => x.Code == DiagnosticCodes.PreferArrayList);
            // Text that isn't a list of names stays a string.
            Assert.DoesNotContain(Analyzer.Analyze("import KScript.Http\n\nRoutes {\n  Middleware(use: \"cors, rate(10)\")\n}\n", Path.Combine(Site, "routes.kscript"), Files).Diagnostics, x => x.Code == DiagnosticCodes.PreferArrayList);
        }

        [Fact]
        public void NamesInAnArrayComplete()
        {
            string text = "import KScript.Http\nimport \"controllers/people.ks.xml\" as people\n\nRoutes {\n  Get(path: \"/\", to: people.index, middleware: [cors, ‸])\n}\n";
            int offset = text.IndexOf('‸');
            text = text.Remove(offset, 1);
            DocumentAnalysis analysis = Service.Analyze(text, Path.Combine(Site, "routes.kscript"));
            List<string> labels = Service.GetCompletions(analysis, offset).Select(i => i.Label).ToList();
            Assert.Contains("throttle", labels);
            Assert.Contains("people.show", labels);
        }

        [Fact]
        public void RoutesBelongInsideRoutes()
        {
            DocumentAnalysis a = Analyzer.Analyze("<kscript>\n<import package=\"KScript.Http\" />\n<Get path=\"/\" redirect=\"/\" />\n</kscript>", Path.Combine(Site, "x.ks.xml"), Files);
            Assert.Contains(a.Diagnostics, d => d.Code == DiagnosticCodes.MisplacedElement && d.Message.Contains("<Routes>"));
        }

        [Fact]
        public void TheCodeFormatWorksToo()
        {
            DocumentAnalysis a = Analyzer.Analyze(
                "import KScript.Http\nimport \"controllers/people.ks.xml\" as people\n\nRoutes(middleware: admin) {\n  Get(path: \"/people/{id}\", to: people.show)\n  Group(prefix: \"/v2\", middleware: [cors, throttle:5]) {\n    Get(path: \"/\", to: people.index)\n  }\n}\n",
                Path.Combine(Site, "api.kscript"), Files);
            TestHelpers.AssertClean(a);
        }

        [Fact]
        public void ViewsMayUseTheControllersVariables()
        {
            DocumentAnalysis a = Analyzer.Analyze("<h1>{$person.name}</h1>", Path.Combine(Site, "views", "people", "show.html"), Files);
            TestHelpers.AssertClean(a);
        }

        [Fact]
        public void ControllerNamesAndMiddlewareComplete()
        {
            string text = "<KScript.Routes>\n  <import src=\"controllers/people.ks.xml\" as=\"people\" />\n  <Get path=\"/\" to=\"‸\" middleware=\"\" />\n</KScript.Routes>";
            int offset = text.IndexOf('‸');
            text = text.Remove(offset, 1);
            DocumentAnalysis analysis = Service.Analyze(text, Path.Combine(Site, "routes.ks.xml"));
            List<string> labels = Service.GetCompletions(analysis, offset).Select(i => i.Label).ToList();
            Assert.Contains("people.show", labels);
            Assert.Contains("people.index", labels);

            int mw = text.IndexOf("middleware=\"", StringComparison.Ordinal) + "middleware=\"".Length;
            List<string> middleware = Service.GetCompletions(analysis, mw).Select(i => i.Label).ToList();
            Assert.Contains("cors", middleware);
            Assert.Contains("throttle", middleware);
        }

        [Fact]
        public void RouteElementsComplete()
        {
            string text = "<KScript.Routes>\n  <‸\n</KScript.Routes>";
            int offset = text.IndexOf('‸');
            text = text.Remove(offset, 1);
            DocumentAnalysis analysis = Service.Analyze(text, Path.Combine(Site, "routes.ks.xml"));
            List<string> labels = Service.GetCompletions(analysis, offset).Select(i => i.Label).ToList();
            Assert.Contains("Get", labels);
            Assert.Contains("Resource", labels);
            Assert.Contains("Group", labels);
        }
    }
}
