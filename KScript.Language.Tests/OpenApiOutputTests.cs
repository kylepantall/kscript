using KScript.Language.Outputs;
using KScript.LanguageServer;

namespace KScript.Language.Tests
{
    /// <summary>http.openapi.file: the OpenAPI document kept in the project by kscript watch, kscript serve and the editor.</summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public sealed class OpenApiOutputTests : IDisposable
    {
        private readonly string dir = Directory.CreateTempSubdirectory("kscript-openapi").FullName;
        private readonly OpenApiOutput output = new OpenApiOutput();

        public void Dispose() => Directory.Delete(dir, true);

        private void Write(string name, string text)
        {
            string path = Path.Combine(dir, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text);
        }

        private const string Api = "KScript.Routes {\n  Get(path: \"/hi\") {\n    return 'hi'\n  }\n}\n";

        [Fact]
        public void IsRegisteredAndOffUntilAFileIsSet()
        {
            Assert.Contains(ProjectOutputs.All, o => o is OpenApiOutput);
            Assert.False(output.IsEnabled(null));
            Assert.False(output.IsEnabled(dir));
            Write("kscript.json", "{ \"name\": \"hi\" }");
            Assert.False(output.IsEnabled(dir));
            Assert.Empty(output.Write(dir, check: false));
            Write("kscript.json", "{ nope");
            Assert.False(output.IsEnabled(dir));
            Write("kscript.json", "{ \"http\": { \"openapi\": { \"file\": \"docs/openapi.json\" } } }");
            Assert.True(output.IsEnabled(dir));
            Assert.Contains(ProjectOutputs.Enabled(dir), o => o.Name == "openapi");
        }

        [Fact]
        public void WritesTheDocumentAndKeepsItUpToDate()
        {
            Write("kscript.json", "{ \"name\": \"hi\", \"http\": { \"openapi\": { \"file\": \"docs/openapi.json\" } } }");

            // No routes yet: nothing to write.
            Assert.Empty(output.Write(dir, check: false));

            Write("api.kscript", Api);
            Assert.Equal(new[] { "docs/openapi.json" }, output.Write(dir, check: true));
            Assert.False(File.Exists(Path.Combine(dir, "docs", "openapi.json")));
            Assert.Equal(new[] { "docs/openapi.json" }, output.Write(dir, check: false));
            string json = File.ReadAllText(Path.Combine(dir, "docs", "openapi.json"));
            Assert.Contains("\"/api/hi\"", json);
            Assert.Empty(output.Write(dir, check: false));

            // The watcher leaves its own file alone, and sees the routes change.
            Assert.True(output.Owns("docs/openapi.json"));
            Assert.True(output.Owns("docs\\openapi.json"));
            Assert.False(output.Owns("api.kscript"));
            Assert.False(ProjectOutputs.IsSource("docs/openapi.json"));
            Write("api.kscript", Api.Replace("/hi", "/hello"));
            Assert.Equal(new[] { "docs/openapi.json" }, output.Write(dir, check: false));
            Assert.Contains("\"/api/hello\"", File.ReadAllText(Path.Combine(dir, "docs", "openapi.json")));
        }

        [Fact]
        public void AYamlNameWritesYamlAndWatchBuildsIt()
        {
            Write("kscript.json", "{ \"http\": { \"openapi\": { \"file\": \"openapi.yaml\" } } }");
            Write("api.kscript", Api);
            var o = new StringWriter { NewLine = "\n" };
            Assert.Equal(1, WatchCommand.Run(new[] { dir, "--check" }, o, new CancellationToken(true), TimeSpan.FromMilliseconds(20)));
            Assert.Equal(0, WatchCommand.Run(new[] { dir, "--once" }, o, new CancellationToken(true), TimeSpan.FromMilliseconds(20)));
            Assert.Contains("openapi: updated openapi.yaml", o.ToString());
            Assert.StartsWith("openapi: \"3.1.0\"\n", File.ReadAllText(Path.Combine(dir, "openapi.yaml")));
            Assert.Equal(0, WatchCommand.Run(new[] { dir, "--check" }, o, new CancellationToken(true), TimeSpan.FromMilliseconds(20)));
        }

        [Fact]
        public void ARouteErrorIsReportedNotThrown()
        {
            Write("kscript.json", "{ \"http\": { \"openapi\": { \"file\": \"openapi.yml\" } } }");
            Write("api.kscript", "KScript.Routes {\n  Get(path: \"/hi\", to: @nowhere.fn)\n}\n");
            OutputResult result = Assert.Single(ProjectOutputs.Build(dir, only: new[] { output }));
            Assert.NotNull(result.Error);
        }
    }
}
