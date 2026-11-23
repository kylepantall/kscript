using System.IO.Compression;
using System.Net;
using System.Text.Json;
using KScript.Cli;
using KScript.Runtime;

namespace KScript.Tests;

public sealed class LibCommandTests : IDisposable
{
    private const string Sha = "1234567890abcdef1234567890abcdef12345678";
    private const string UpstreamSha = "abcdef1234567890abcdef1234567890abcdef12";
    private readonly string root = Directory.CreateTempSubdirectory("kscript-lib-tests-").FullName;
    private readonly GitHub github = new();

    public void Dispose() => Directory.Delete(root, recursive: true);

    private sealed class GitHub : HttpMessageHandler
    {
        internal byte[] Archive { get; set; }
        internal string Contents { get; set; } = "[]";
        internal HttpStatusCode? ContentsStatus { get; set; }
        internal HttpStatusCode? ArchiveStatus { get; set; }
        internal List<string> Requests { get; } = new();
        internal HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        internal Dictionary<string, HttpStatusCode> RefErrors { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri.AbsoluteUri);
            HttpStatusCode status = Status;
            HttpContent body;
            if (request.RequestUri.Host == "codeload.github.com")
            {
                status = ArchiveStatus ?? status;
                body = new ByteArrayContent(Archive ?? Array.Empty<byte>());
            }
            else if (request.RequestUri.Host == "api.github.com" && request.RequestUri.AbsolutePath.Contains("/commits/"))
            {
                string revision = Uri.UnescapeDataString(request.RequestUri.AbsolutePath.Split("/commits/")[1]);
                status = RefErrors.GetValueOrDefault(revision, Status);
                body = new StringContent("{\"sha\":\"" + Sha + "\"}");
            }
            else if (request.RequestUri.Host == "api.github.com" && request.RequestUri.AbsolutePath.Contains("/contents/"))
            {
                status = ContentsStatus ?? status;
                body = new StringContent(Contents);
            }
            else if (request.RequestUri.Host == "api.github.com" && request.RequestUri.AbsolutePath.StartsWith("/users/"))
                body = new StringContent("[]");
            else if (request.RequestUri.Host == "raw.githubusercontent.com")
            {
                status = HttpStatusCode.NotFound;
                body = new StringContent("");
            }
            else throw new InvalidOperationException($"Unexpected request {request.RequestUri}");
            return Task.FromResult(new HttpResponseMessage(status) { Content = body });
        }
    }

    private static byte[] Archive(params (string File, string Text, int Attributes)[] files)
    {
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                var entry = archive.CreateEntry("repo-" + Sha + "/" + file.File);
                entry.ExternalAttributes = file.Attributes;
                using var writer = new StreamWriter(entry.Open());
                writer.Write(file.Text);
            }
        }
        return bytes.ToArray();
    }

    private (int Code, string Output, string Error) Add(string source, string directory = null)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        int code = LibCommand.Run(new[] { "add", source }, output, error, directory ?? root, github);
        return (code, output.ToString(), error.ToString());
    }

    [Theory]
    [InlineData("Org/Library")]
    [InlineData("https://github.com/Org/Library.git")]
    public void InstallsRegularFilesAtOneCommitAndReportsAnImport(string source)
    {
        github.Archive = Archive(("index.kscript", "fn hello() => 'hi'", 0), ("data/labels.json", "{}", 0));
        var result = Add(source);
        Assert.True(result.Code == 0, result.Error);
        Assert.Contains("import \"lib/library/index.kscript\"", result.Output);
        Assert.Equal(new[] { "https://api.github.com/repos/org/library/commits/HEAD", $"https://codeload.github.com/org/library/zip/{Sha}" }, github.Requests);
        string folder = Path.Combine(root, "lib", "library");
        Assert.Equal("{}", File.ReadAllText(Path.Combine(folder, "data", "labels.json")));
        using var receipt = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, ".kscript-lib.json")));
        Assert.Equal(Sha, receipt.RootElement.GetProperty("commit").GetString());
        Assert.Equal("https://github.com/org/library", receipt.RootElement.GetProperty("source").GetString());

        File.WriteAllText(Path.Combine(folder, "index.kscript"), "edited");
        Assert.Contains("Already installed", Add("org/library").Output);
        Assert.Equal(2, github.Requests.Count);
        Assert.Equal("edited", File.ReadAllText(Path.Combine(folder, "index.kscript")));
        Assert.Equal(ExitCodes.Failed, Add("another/library").Code);
        Assert.Equal(2, github.Requests.Count);
    }

    [Fact]
    public void BareAndQualifiedNamesShareAnOfflinePinnedInstallFromTheDefaultRepository()
    {
        github.Archive = Archive(("index.kscript", "whole repo", 0),
            ("libraries/isolink-connect/index.kscript", "library", 0), ("libraries/other/index.kscript", "other", 0));
        var result = Add("isolink-connect");
        Assert.True(result.Code == ExitCodes.Ok, result.Error);
        Assert.Contains("https://api.github.com/repos/kylepantall/kscript/commits/HEAD", github.Requests);
        Assert.Contains($"https://api.github.com/repos/kylepantall/kscript/contents/libraries/isolink-connect?ref={Sha}", github.Requests);
        Assert.Equal($"https://codeload.github.com/kylepantall/kscript/zip/{Sha}", github.Requests.Last());
        int requests = github.Requests.Count;
        Assert.Contains("import \"lib/isolink-connect/index.kscript\"", result.Output);
        string installed = Path.Combine(root, "lib", "isolink-connect");
        using var receipt = JsonDocument.Parse(File.ReadAllText(Path.Combine(installed, ".kscript-lib.json")));
        Assert.Equal("https://github.com/kylepantall/kscript/tree/HEAD/libraries/isolink-connect", receipt.RootElement.GetProperty("source").GetString());
        Assert.Equal(Sha, receipt.RootElement.GetProperty("commit").GetString());
        Assert.False(receipt.RootElement.TryGetProperty("submodule", out _));
        Assert.Equal("library", File.ReadAllText(Path.Combine(installed, "index.kscript")));
        Assert.False(Directory.Exists(Path.Combine(installed, "libraries")));
        File.WriteAllText(Path.Combine(installed, "index.kscript"), "edited");

        github.Status = HttpStatusCode.ServiceUnavailable;
        Assert.Contains("Already installed", Add("KylePantall/KScript@isolink-connect").Output);
        Assert.Contains("Already installed", Add("https://github.com/kylepantall/kscript/tree/HEAD/libraries/isolink-connect").Output);
        Assert.Equal(requests, github.Requests.Count);
        Assert.Equal("edited", File.ReadAllText(Path.Combine(installed, "index.kscript")));
    }

    [Theory]
    [InlineData("org/toolkit", "", false)]
    [InlineData("toolkit", "libraries/toolkit/", false)]
    [InlineData("toolkit", "libraries/toolkit/", true)]
    public void ManifestSelectsThePublicEntryAndRecordsTheDeclaredMetadata(string source, string prefix, bool submodule)
    {
        if (submodule) github.Contents = Gitlink("submodule", "https://github.com/org/sdks", UpstreamSha);
        github.Archive = Archive(
            (prefix + "kscript.json", """
                {
                  // The catalog also publishes this library as toolkit.
                  "name": "example-sdk",
                  "library": { "entry": "src/public.kscript", "version": "0.2.0-beta.1" },
                  "packages": ["KScript.Net"],
                }
                """, 0),
            (prefix + "index.kscript", "fn answer() => 0", 0),
            (prefix + "src/public.kscript", "fn answer() => 42", 0));
        var result = Add(source);
        Assert.True(result.Code == ExitCodes.Ok, result.Error);
        string installedSha = submodule ? UpstreamSha : Sha;
        Assert.Contains($"Installed toolkit 0.2.0-beta.1 at {installedSha}", result.Output);
        Assert.Contains("import \"lib/toolkit/src/public.kscript\"", result.Output);
        string installed = Path.Combine(root, "lib", "toolkit");
        using var receipt = JsonDocument.Parse(File.ReadAllText(Path.Combine(installed, ".kscript-lib.json")));
        var metadata = receipt.RootElement;
        Assert.Equal("example-sdk", metadata.GetProperty("name").GetString());
        Assert.Equal("0.2.0-beta.1", metadata.GetProperty("version").GetString());
        Assert.Equal("src/public.kscript", metadata.GetProperty("entry").GetString());
        Assert.Equal(new[] { "KScript.Net" }, metadata.GetProperty("packages").EnumerateArray().Select(p => p.GetString()));
        using var output = new StringWriter();
        new Interpreter(KScriptHost.ForText(output)).RunText("import \"lib/toolkit/src/public.kscript\" as sdk\necho @sdk.answer()", Path.Combine(root, "main.kscript"));
        Assert.Equal("42", output.ToString().Trim());

        int requests = github.Requests.Count;
        github.Status = HttpStatusCode.ServiceUnavailable;
        File.WriteAllText(Path.Combine(installed, "kscript.json"), "locally edited manifest");
        Assert.Contains($"Already installed toolkit 0.2.0-beta.1 at {installedSha}", Add(source).Output);
        Assert.Equal(requests, github.Requests.Count);
        Assert.Equal("locally edited manifest", File.ReadAllText(Path.Combine(installed, "kscript.json")));
    }

    [Theory]
    [InlineData("""{"name":"legacy","packages":["App.Custom"],"standard":"./not-in-the-archive.md"}""")]
    [InlineData("""{"name":"legacy","app":{"main":"demo.kscript"}}""")]
    public void OrdinaryProjectSettingsKeepTheLegacyLibraryEntrySelection(string config)
    {
        github.Archive = Archive(("kscript.json", config, 0), ("demo.kscript", "echo 'demo'", 0), ("index.ks.xml", "<kscript/>", 0));
        var result = Add("org/toolkit");
        Assert.True(result.Code == ExitCodes.Ok, result.Error);
        Assert.Contains("import \"lib/toolkit/index.ks.xml\"", result.Output);
        using var receipt = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "lib", "toolkit", ".kscript-lib.json")));
        Assert.False(receipt.RootElement.TryGetProperty("version", out _));
    }

    [Theory]
    [InlineData("src/other.kscript", "0.2.0", "toolkit", "KScript.Net", "entry")]
    [InlineData("src/REAL.kscript", "0.2.0", "toolkit", "KScript.Net", "entry")]
    [InlineData("../outside.kscript", "0.2.0", "toolkit", "KScript.Net", "entry")]
    [InlineData("/outside.kscript", "0.2.0", "toolkit", "KScript.Net", "entry")]
    [InlineData("C:\\outside.kscript", "0.2.0", "toolkit", "KScript.Net", "entry")]
    [InlineData("README.md", "0.2.0", "toolkit", "KScript.Net", "entry")]
    [InlineData("src/real.kscript", "latest", "toolkit", "KScript.Net", "version")]
    [InlineData("src/real.kscript", "0.2.0", "../other", "KScript.Net", "name")]
    [InlineData("src/real.kscript", "0.2.0", "toolkit", "KScript.Missing", "package")]
    public void BadManifestLeavesTheInstallDirectoryFreeForRetry(string entry, string version, string name, string package, string field)
    {
        string config = JsonSerializer.Serialize(new { name, library = new { entry, version }, packages = new[] { package } });
        github.Archive = Archive(("kscript.json", config, 0), ("src/real.kscript", "echo 'ok'", 0), ("README.md", "SDK", 0));
        var result = Add("org/toolkit");
        Assert.Equal(ExitCodes.Failed, result.Code);
        Assert.Contains(field, result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(root, "lib")));
        github.Archive = Archive(("index.kscript", "echo 'fixed'", 0));
        Assert.Equal(ExitCodes.Ok, Add("org/toolkit").Code);
    }

    [Theory]
    [InlineData("../sdk", "kscript.json", "{}")]
    [InlineData("/sdk", "kscript.json", "{}")]
    [InlineData("sdk", null, null)]
    [InlineData("sdk", "kscript.json", "{}")]
    [InlineData("sdk", "kscript.json", """{"library":"nested"}""")]
    [InlineData("sdk", "KScript.json", """{"name":"library","library":{"version":"1.0.0","entry":"index.kscript"}}""")]
    public void InvalidLibraryReferencesCannotInstallAFallbackEntry(string folder, string childFile, string childConfig)
    {
        string workspace = JsonSerializer.Serialize(new { library = folder });
        var files = new List<(string, string, int)>
        {
            ("kscript.json", workspace, 0),
            ("index.kscript", "echo 'project'", 0),
            ("sdk/index.kscript", "echo 'fallback'", 0),
            ("sdk/nested/kscript.json", """{"name":"library","library":{"version":"1.0.0","entry":"index.kscript"}}""", 0),
            ("sdk/nested/index.kscript", "echo 'nested'", 0)
        };
        if (childFile != null) files.Add(("sdk/" + childFile, childConfig, 0));
        github.Archive = Archive(files.ToArray());

        var result = Add("org/library");
        Assert.Equal(ExitCodes.Failed, result.Code);
        Assert.NotEmpty(result.Error);
        Assert.False(Directory.Exists(Path.Combine(root, "lib", "library")));
        Assert.False(File.Exists(Path.Combine(root, "kscript.json")));
    }

    [Fact]
    public void TheTemplateLibrariesCanBeInstalledAndUsedWithoutGeneratingAProject()
    {
        string[] libraries = { "app-cache", "request-id" };
        string repository = Conventions.CliConventionTests.RepoRoot();
        github.Archive = Archive(libraries.Select(name =>
        {
            string path = $"libraries/{name}/index.kscript";
            return (path, File.ReadAllText(Path.Combine(repository, path)), 0);
        }).ToArray());
        foreach (string name in libraries)
        {
            var result = Add(name);
            Assert.True(result.Code == 0, result.Error);
        }
        string script = Path.Combine(root, "app.kscript");
        File.WriteAllText(script, """
            import KScript.Http
            import "lib/app-cache/index.kscript" as cache
            import "lib/request-id/index.kscript"

            key = @uuid()
            echo @cache.cached($key, fn() => 'first')
            echo @cache.cached($key, fn() => 'second')
            @cache.forget_cached($key)
            echo @cache.cached($key, fn() => 'third')
            next = fn(r) => @response($r.path, status: 202)
            existing = @request_id({ headers: { 'x-request-id': 'from-caller' }, path: '/test' }, $next)
            echo $existing->header('X-Request-Id')
            echo $existing.status
            echo $existing.body
            generated = @request_id({ headers: {}, path: '/' }, $next)
            echo $generated->header('X-Request-Id')
            """);
        using var output = new StringWriter();
        new Interpreter(KScriptHost.ForText(output)).RunFile(script);
        string[] lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(new[] { "first", "first", "third", "from-caller", "202", "/test" }, lines[..^1]);
        Assert.True(Guid.TryParseExact(lines[^1], "D", out _));
    }

    [Fact]
    public void QualifiedNameRequiresRegistrationInTheProjectAndKeepsNameConflictsIntact()
    {
        File.WriteAllText(Path.Combine(root, "kscript.json"), """{"paths":{"lib":"dependencies"}}""");
        string subdir = Directory.CreateDirectory(Path.Combine(root, "src")).FullName;
        var unregistered = Add("Org/Tools@Slug", subdir);
        Assert.Equal(ExitCodes.Usage, unregistered.Code);
        Assert.Contains("kscript repo add org/tools", unregistered.Error);
        Assert.Empty(github.Requests);
        LibraryRepositories.Add(root, "org/tools");
        github.Archive = Archive(("libraries/Slug/index.kscript", "custom", 0), ("libraries/slug/index.kscript", "unrelated", 0));
        var installed = Add("Org/Tools@Slug", subdir);
        Assert.True(installed.Code == ExitCodes.Ok, installed.Error);
        Assert.Contains("https://api.github.com/repos/org/tools/commits/HEAD", github.Requests);
        Assert.Contains("import \"dependencies/Slug/index.kscript\"", installed.Output);
        string script = Path.Combine(root, "dependencies", "Slug", "index.kscript");
        Assert.Equal("custom", File.ReadAllText(script));
        Assert.Equal(ExitCodes.Ok, Add("Slug", subdir).Code);
        var conflict = Add("kylepantall/kscript@Slug", subdir);
        Assert.Equal(ExitCodes.Failed, conflict.Code);
        Assert.Contains("already exists", conflict.Error);
        Assert.Equal(3, github.Requests.Count);
        Assert.Equal("custom", File.ReadAllText(script));
    }

    [Fact]
    public void MissingRepositoryOrLibraryReportsTheRequestedSourceAndNeverGuessesRefs()
    {
        github.Status = HttpStatusCode.NotFound;
        var missingRepository = Add("kylepantall/kscript@missing");
        Assert.Equal(ExitCodes.Failed, missingRepository.Code);
        Assert.Contains("public GitHub repository kylepantall/kscript", missingRepository.Error);
        Assert.Single(github.Requests);

        github.Status = HttpStatusCode.OK;
        github.ContentsStatus = HttpStatusCode.NotFound;
        var missingLibrary = Add("kylepantall/kscript@missing");
        Assert.Equal(ExitCodes.Failed, missingLibrary.Code);
        Assert.Contains("libraries/missing/ was not found in kylepantall/kscript", missingLibrary.Error);
        Assert.Equal(4, github.Requests.Count);
        Assert.Equal($"https://raw.githubusercontent.com/kylepantall/kscript/{Sha}/kscript.json", github.Requests.Last());
        Assert.False(Directory.Exists(Path.Combine(root, "lib", "missing")));
    }

    [Theory]
    [InlineData("orimovo/another-sdk")]
    [InlineData("waggr-gb/isolink-connect")]
    [InlineData("waggr-gb/.github")]
    [InlineData("orimovo/_tools")]
    [InlineData("orimovo/-tools")]
    [InlineData("waggr-gb/con")]
    public void QualifiedOfficialLibraryUsesOnlyItsPublisher(string repository)
    {
        github.Archive = Archive(("libraries/demo/index.kscript", "fn build() => 3", 0));
        var installed = Add(repository + "@demo");
        Assert.True(installed.Code == ExitCodes.Ok, installed.Error);
        Assert.Equal(new[] { $"https://api.github.com/repos/{repository}/commits/HEAD",
            $"https://api.github.com/repos/{repository}/contents/libraries/demo?ref={Sha}",
            $"https://codeload.github.com/{repository}/zip/{Sha}" }, github.Requests);
        Assert.False(Directory.Exists(Path.Combine(root, ".kscript", "repos")));
    }

    [Theory]
    [InlineData("waggr-gb/.git")]
    [InlineData("waggr-gb/CON")]
    [InlineData("orimovo/.github")]
    [InlineData("https://github.com/orimovo/.github/tree/main")]
    [InlineData("orimovo/_tools")]
    [InlineData("orimovo/-tools")]
    [InlineData("orimovo/..@demo")]
    [InlineData("https://github.com/orimovo/.github/tree/%2E%2E/tools")]
    [InlineData("https://github.com/orimovo/.github/tree/main/CON")]
    [InlineData("orimovo/.github@_tools")]
    public void UnsafeInstallNamesAndPathsAreRejectedBeforeFetchingAnything(string selector)
    {
        Assert.Equal(ExitCodes.Usage, Add(selector).Code);
        Assert.Empty(github.Requests);
        Assert.Empty(Directory.GetFileSystemEntries(root));
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(500)]
    public void BareNameStopsOnCatalogErrorsBesidesAnAbsentEntry(int status)
    {
        github.ContentsStatus = (HttpStatusCode)status;
        Assert.Equal(ExitCodes.Failed, Add("demo").Code);
        Assert.Equal(2, github.Requests.Count);
        Assert.All(github.Requests, url => Assert.Contains("/kylepantall/kscript/", url));
    }

    [Fact]
    public void BareNameStopsOnAnUnavailableArchiveOrInvalidPackageManifest()
    {
        github.ArchiveStatus = HttpStatusCode.NotFound;
        Assert.Equal(ExitCodes.Failed, Add("demo").Code);
        Assert.Equal($"https://codeload.github.com/kylepantall/kscript/zip/{Sha}", github.Requests.Last());
        github.Requests.Clear();
        github.ArchiveStatus = null;
        github.Archive = Archive(("libraries/demo/kscript.json", """{"name":"demo","library":{"entry":"missing.kscript"}}""", 0),
            ("libraries/demo/index.kscript", "echo true", 0));
        Assert.Equal(ExitCodes.Failed, Add("demo").Code);
        Assert.Equal($"https://codeload.github.com/kylepantall/kscript/zip/{Sha}", github.Requests.Last());
        Assert.Single(github.Requests, url => url.StartsWith("https://codeload.github.com/"));
    }

    private static string Gitlink(string type, string url, string commit) =>
        JsonSerializer.Serialize(new { type, name = "slugs", path = "libraries/slugs", submodule_git_url = url, sha = commit });

    [Theory]
    [InlineData("org/catalog@slugs", "HEAD", "submodule", "https://github.com/Upstream/Slug-Tools.git", "upstream/slug-tools")]
    [InlineData("org/catalog@slugs", "HEAD", "file", "git@github.com:other/slug-tools.git", "other/slug-tools")]
    [InlineData("org/catalog@slugs", "HEAD", "submodule", "../slug-tools.git", "org/slug-tools")]
    [InlineData("https://github.com/org/catalog/tree/v2/libraries/slugs", "v2", "submodule", "ssh://git@github.com/other/slug-tools.git", "other/slug-tools")]
    public void SubmoduleDownloadsTheCatalogPinAndPreservesCatalogIdentity(string selector, string reference, string type,
        string gitUrl, string repository)
    {
        if (selector.Contains('@')) LibraryRepositories.Add(root, "org/catalog");
        github.Contents = Gitlink(type, gitUrl, UpstreamSha);
        github.Archive = Archive(("index.kscript", "fn slug() => 'ready'", 0), ("data/names.json", "[]", 0));
        var result = Add(selector);
        Assert.True(result.Code == ExitCodes.Ok, result.Error);
        Assert.Contains("import \"lib/slugs/index.kscript\"", result.Output);
        var expected = new[] {
            $"https://api.github.com/repos/org/catalog/commits/{reference}",
            $"https://api.github.com/repos/org/catalog/contents/libraries/slugs?ref={Sha}",
            $"https://codeload.github.com/{repository}/zip/{UpstreamSha}"
        };
        Assert.Equal(expected, github.Requests);
        string installed = Path.Combine(root, "lib", "slugs");
        string script = Path.Combine(installed, "index.kscript");
        Assert.Equal("fn slug() => 'ready'", File.ReadAllText(script));
        Assert.Equal("[]", File.ReadAllText(Path.Combine(installed, "data", "names.json")));
        using var receipt = JsonDocument.Parse(File.ReadAllText(Path.Combine(installed, ".kscript-lib.json")));
        Assert.Equal($"https://github.com/org/catalog/tree/{reference}/libraries/slugs", receipt.RootElement.GetProperty("source").GetString());
        Assert.Equal(Sha, receipt.RootElement.GetProperty("commit").GetString());
        var submodule = receipt.RootElement.GetProperty("submodule");
        Assert.Equal(repository, submodule.GetProperty("repository").GetString());
        Assert.Equal(UpstreamSha, submodule.GetProperty("commit").GetString());

        File.WriteAllText(script, "local changes");
        github.Status = HttpStatusCode.ServiceUnavailable;
        Assert.Contains("Already installed", Add(selector).Output);
        Assert.Equal(ExitCodes.Ok, Add("slugs").Code);
        Assert.Equal(expected, github.Requests);
        Assert.Equal("local changes", File.ReadAllText(script));
    }

    [Theory]
    [InlineData("submodule", "https://git.example.org/tools/slugs.git", UpstreamSha)]
    [InlineData("submodule", "git@example.org:tools/slugs.git", UpstreamSha)]
    [InlineData("submodule", "https://github.com/tools/slugs/tree/main", UpstreamSha)]
    [InlineData("submodule", "/tmp/slugs", UpstreamSha)]
    [InlineData("submodule", null, UpstreamSha)]
    [InlineData("submodule", "../slugs", "main")]
    [InlineData("submodule", "../slugs", null)]
    [InlineData("file", null, UpstreamSha)]
    [InlineData("symlink", "https://github.com/tools/slugs.git", UpstreamSha)]
    public void InvalidCatalogEntriesFailBeforeFetchingAnArchive(string type, string gitUrl, string commit)
    {
        github.Contents = Gitlink(type, gitUrl, commit);
        var result = Add("slugs");
        Assert.Equal(ExitCodes.Failed, result.Code);
        Assert.NotEmpty(result.Error);
        Assert.Equal(new[] { "https://api.github.com/repos/kylepantall/kscript/commits/HEAD",
            $"https://api.github.com/repos/kylepantall/kscript/contents/libraries/slugs?ref={Sha}" }, github.Requests);
        Assert.False(Directory.Exists(Path.Combine(root, "lib", "slugs")));
    }

    [Fact]
    public void MissingPinnedCommitLeavesTheDestinationFreeForARetry()
    {
        github.Contents = Gitlink("submodule", "https://github.com/other/repo.git", UpstreamSha);
        github.ArchiveStatus = HttpStatusCode.NotFound;
        Assert.Equal(ExitCodes.Failed, Add("slugs").Code);
        Assert.Equal($"https://codeload.github.com/other/repo/zip/{UpstreamSha}", github.Requests.Last());
        Assert.False(Directory.Exists(Path.Combine(root, "lib", "slugs")));

        github.ArchiveStatus = HttpStatusCode.OK;
        github.Archive = Archive(("index.kscript", "fn answer() => 42", 0));
        var retry = Add("slugs");
        Assert.True(retry.Code == ExitCodes.Ok, retry.Error);
        Assert.Equal("fn answer() => 42", File.ReadAllText(Path.Combine(root, "lib", "slugs", "index.kscript")));
    }

    [Theory]
    [InlineData("Org/Repo", "org/repo")]
    [InlineData("https://github.com/Org/Repo.git/", "org/repo")]
    [InlineData("https://github.com/Org/Repo/tree/main", null)]
    [InlineData("https://github.com/Org/Repo.git/tree/main", null)]
    [InlineData("org/repo@slug", null)]
    [InlineData("slug", null)]
    [InlineData("http://github.com/org/repo", null)]
    [InlineData("https://example.org/org/repo", null)]
    public void RepositoryRegistrationsAcceptOnlyCanonicalizableGitHubRoots(string input, string expected)
    {
        Assert.Equal(expected != null, LibCommand.TryParseRepository(input, out string repository));
        Assert.Equal(expected, repository);
    }

    [Fact]
    public void TreeUrlInstallsOnlyTheSelectedFolderUnderConfiguredSiteAndLibPaths()
    {
        File.WriteAllText(Path.Combine(root, "kscript.json"), """{"serve":{"root":"site"},"paths":{"lib":"vendor/libraries"}}""");
        string cwd = Directory.CreateDirectory(Path.Combine(root, "site", "pages")).FullName;
        github.Archive = Archive(("libraries/hello/index.ks.xml", "<kscript />", 0), ("libraries/other/index.kscript", "other", 0));
        var result = Add("https://github.com/org/repo/tree/v1.0.0/libraries/hello", cwd);
        Assert.True(result.Code == 0, result.Error);
        Assert.Contains("import \"vendor/libraries/hello/index.ks.xml\"", result.Output);
        Assert.Contains("https://api.github.com/repos/org/repo/commits/v1.0.0", github.Requests);
        string libraries = Path.Combine(root, "site", "vendor", "libraries");
        Assert.Equal(new[] { "hello" }, Directory.GetDirectories(libraries).Select(Path.GetFileName));
        Assert.Equal("<kscript />", File.ReadAllText(Path.Combine(libraries, "hello", "index.ks.xml")));
    }

    [Theory]
    [InlineData("examples/hello")]
    [InlineData("libraries/sdk/hello")]
    public void DirectTreeOutsideANamedLibraryStillInstallsFromTheCatalogArchive(string path)
    {
        github.Archive = Archive(($"{path}/index.kscript", "fn hello() => 'hi'", 0));
        var result = Add($"https://github.com/org/tools/tree/v1/{path}");
        Assert.True(result.Code == ExitCodes.Ok, result.Error);
        Assert.Equal(new[] { "https://api.github.com/repos/org/tools/commits/v1",
            $"https://codeload.github.com/org/tools/zip/{Sha}" }, github.Requests);
        Assert.Equal("fn hello() => 'hi'", File.ReadAllText(Path.Combine(root, "lib", "hello", "index.kscript")));
    }

    [Theory]
    [InlineData("", "repo", HttpStatusCode.UnprocessableEntity)]
    [InlineData("/libraries/hello", "hello", HttpStatusCode.NotFound)]
    public void SlashBranchesInstallTheRepoOrItsSubfolderAndRepeatOffline(string tail, string name, HttpStatusCode unknown)
    {
        const string branch = "codex/libraries-and-type-methods";
        string url = $"https://github.com/org/repo/tree/{branch}{tail}";
        github.RefErrors["codex"] = unknown;
        github.Archive = Archive(("index.kscript", "repo", 0), ("libraries/hello/index.kscript", "subfolder", 0));
        // An unrelated directory whose name matches the other interpretation stays intact.
        string unrelated = tail == "" ? "libraries-and-type-methods" : "repo";
        string other = Directory.CreateDirectory(Path.Combine(root, "lib", unrelated)).FullName;
        File.WriteAllText(Path.Combine(other, ".kscript-lib.json"), "not a receipt");

        var result = Add(url);
        Assert.True(result.Code == 0, result.Error);
        var expected = new List<string> { "https://api.github.com/repos/org/repo/commits/codex",
            "https://api.github.com/repos/org/repo/commits/codex%2Flibraries-and-type-methods" };
        if (tail.Length > 0) expected.Add($"https://api.github.com/repos/org/repo/contents/libraries/hello?ref={Sha}");
        expected.Add($"https://codeload.github.com/org/repo/zip/{Sha}");
        Assert.Equal(expected, github.Requests);
        Assert.Contains($"import \"lib/{name}/index.kscript\"", result.Output);
        string file = Path.Combine(root, "lib", name, "index.kscript");
        Assert.Equal(tail == "" ? "repo" : "subfolder", File.ReadAllText(file));
        Assert.Equal("not a receipt", File.ReadAllText(Path.Combine(other, ".kscript-lib.json")));
        File.WriteAllText(file, "local edit");
        github.Status = HttpStatusCode.ServiceUnavailable;
        Assert.Contains($"Already installed {name}", Add(url).Output);
        Assert.Contains($"Already installed {name}", Add(url.Replace(branch, Uri.EscapeDataString(branch))).Output);
        Assert.Equal("local edit", File.ReadAllText(file));
        Assert.Equal(expected, github.Requests);
    }

    [Fact]
    public void GitHubErrorsOtherThanUnknownRefsDoNotRetryOrInstall()
    {
        github.RefErrors["codex"] = HttpStatusCode.Forbidden;
        Assert.Equal(ExitCodes.Failed, Add("https://github.com/org/repo/tree/codex/branch/lib").Code);
        Assert.Single(github.Requests);
        Assert.Empty(Directory.GetFileSystemEntries(root));
    }

    [Theory]
    [InlineData("https://example.org/a/b")]
    [InlineData("https://github.com.evil.test/a/b")]
    [InlineData("https://github.com/a/b/tree/main/%2e%2e")]
    [InlineData("https://github.com/a/b/tree/main/%2fetc")]
    [InlineData("https://github.com/a/b?foo=bar")]
    [InlineData("org/CON")]
    [InlineData("org/repo@../escape")]
    [InlineData("org/repo@%2fetc")]
    [InlineData("org/repo@.hidden")]
    [InlineData("org/repo@a@b")]
    [InlineData("org/repo@")]
    [InlineData("org/repo@NUL")]
    [InlineData("https://example.org/org/repo@slug")]
    public void InvalidSourcesDoNotMakeNetworkRequests(string source)
    {
        Assert.Equal(ExitCodes.Usage, Add(source).Code);
        Assert.Empty(github.Requests);
    }

    [Theory]
    [InlineData("../outside.kscript", 0)]
    [InlineData("sub/../../outside.kscript", 0)]
    [InlineData("C:/outside.kscript", 0)]
    [InlineData("..\\outside.kscript", 0)]
    [InlineData(".git/config", 0)]
    [InlineData(".kscript-lib.json", 0)]
    [InlineData("link", unchecked((int)0xa1ff0000))]
    public void FailedExtractionLeavesNoPartialInstallation(string path, int attributes)
    {
        github.Archive = Archive(("index.kscript", "first", 0), (path, "untrusted", attributes));
        Assert.Equal(ExitCodes.Failed, Add("org/library").Code);
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(root, "lib")));
        Assert.False(File.Exists(Path.Combine(root, "outside.kscript")));
    }

    [Fact]
    public void RejectsLibraryWithoutScriptsAndPreservesExistingUserDirectory()
    {
        github.Archive = Archive(("notes.md", "hello", 0));
        Assert.Equal(ExitCodes.Failed, Add("org/library").Code);
        string folder = Directory.CreateDirectory(Path.Combine(root, "lib", "library")).FullName;
        File.WriteAllText(Path.Combine(folder, "notes.md"), "mine");
        Assert.Equal(ExitCodes.Failed, Add("org/library").Code);
        Assert.Equal("mine", File.ReadAllText(Path.Combine(folder, "notes.md")));
        Assert.Equal(2, github.Requests.Count);
    }

    [Fact]
    public void LibraryFolderCannotRedirectWritesOutsideTheProject()
    {
        if (OperatingSystem.IsWindows()) return; // Creating symbolic links on Windows can require Developer Mode.
        string outside = Directory.CreateDirectory(Path.Combine(root, "elsewhere")).FullName;
        Directory.CreateSymbolicLink(Path.Combine(root, "lib"), outside);
        Assert.Equal(ExitCodes.Failed, Add("org/library").Code);
        Assert.Empty(github.Requests);
        Assert.Empty(Directory.GetFileSystemEntries(outside));
    }

    [Fact]
    public void FailedDownloadDoesNotInstallAnything()
    {
        github.Status = HttpStatusCode.NotFound;
        Assert.Equal(ExitCodes.Failed, Add("org/library").Code);
        Assert.Empty(Directory.GetFileSystemEntries(root));
    }
}
