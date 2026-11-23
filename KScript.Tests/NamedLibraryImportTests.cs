using System.IO.Compression;
using System.Net;
using System.Text;
using KScript.Cli;
using KScript.Compile;
using KScript.Runtime;

namespace KScript.Tests;

public sealed class NamedLibraryImportTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RegisteredLibraryAndItsDependenciesWorkInScriptsAndCompilePlans(bool submodule)
    {
        string project = Directory.CreateTempSubdirectory("kscript-named-import-").FullName;
        try
        {
            string scripts = Directory.CreateDirectory(Path.Combine(project, "site", "scripts")).FullName;
            File.WriteAllText(Path.Combine(project, "kscript.json"), """{"serve":{"root":"site"},"paths":{"lib":"vendor"}}""");
            const string entry = """
                import "src/math.kscript" as impl
                fn calculate(n: number) => @impl.square_root($n)
                """;
            const string dependency = """
                // import KScript.AI
                import KScript.Math
                fn square_root(n: number) => @sqrt($n)
                """;
            using var archive = new MemoryStream();
            using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (path, content) in new[] { ("index.kscript", entry), ("src/math.kscript", dependency), ("label.txt", "roots") })
                {
                    using var writer = new StreamWriter(zip.CreateEntry("upstream/libraries/roots/" + path).Open());
                    writer.Write(content);
                }
                using var other = new StreamWriter(zip.CreateEntry("upstream/other-sdk/index.kscript").Open());
                other.Write("fn unrelated() => 42");
            }

            using var error = new StringWriter();
            Assert.True(RepoCommand.Run(new[] { "add", "acme/catalog" }, TextWriter.Null, error, scripts) == 0, error.ToString());
            Assert.True(LibCommand.Run(new[] { "add", "acme/catalog@roots" }, TextWriter.Null, error, scripts, new GitHub(archive.ToArray(), submodule)) == 0, error.ToString());
            Assert.False(Directory.Exists(Path.Combine(project, "site", "vendor", "roots", "other-sdk")));
            string main = Path.Combine(scripts, "app.kscript");
            File.WriteAllText(main, """
                import "../vendor/roots/index.kscript" as roots
                echo @roots.calculate(81)
                """);
            using var output = new StringWriter();
            new Interpreter(KScriptHost.ForText(output)).RunFile(main);
            Assert.Equal("9", output.ToString().TrimEnd());

            var plan = CompileCommand.Plan(main, null, null, TextWriter.Null, out _);
            Assert.Equal("site/scripts/app.kscript", plan.Main);
            Assert.Equal(new[] { "KScript.Core", "KScript.Math" }, plan.Packages.ToArray());
            Assert.Equal(entry, Encoding.UTF8.GetString(plan.Files["site/vendor/roots/index.kscript"]));
            Assert.Equal(dependency, Encoding.UTF8.GetString(plan.Files["site/vendor/roots/src/math.kscript"]));
            Assert.Equal("roots", Encoding.UTF8.GetString(plan.Files["site/vendor/roots/label.txt"]));

            File.WriteAllText(Path.Combine(project, "site", "index.html"), "<h1>Roots</h1>");
            var site = CompileCommand.Plan(project, null, null, TextWriter.Null, out _);
            Assert.Contains("KScript.Math", site.Packages);
            Assert.DoesNotContain("KScript.AI", site.Packages);
        }
        finally { Directory.Delete(project, recursive: true); }
    }

    private sealed class GitHub(byte[] archive, bool submodule) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            const string sha = "1234567890abcdef1234567890abcdef12345678";
            const string pinned = "abcdef1234567890abcdef1234567890abcdef12";
            HttpContent body = request.RequestUri.AbsoluteUri switch
            {
                "https://api.github.com/repos/acme/catalog/commits/HEAD" => new StringContent($"{{\"sha\":\"{sha}\"}}"),
                "https://api.github.com/repos/acme/catalog/contents/libraries/roots?ref=" + sha =>
                    new StringContent(submodule ? $$"""{"type":"submodule","path":"libraries/roots","sha":"{{pinned}}","submodule_git_url":"https://github.com/acme/all-sdks.git"}""" : "[]"),
                "https://codeload.github.com/acme/catalog/zip/" + sha when !submodule => new ByteArrayContent(archive),
                "https://codeload.github.com/acme/all-sdks/zip/" + pinned when submodule => new ByteArrayContent(archive),
                _ => throw new InvalidOperationException($"Unexpected repository request {request.RequestUri}"),
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = body });
        }
    }
}
