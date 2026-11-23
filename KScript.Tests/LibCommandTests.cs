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
        Assert.Equal(new[] { "https://api.github.com/repos/kylepantall/kscript/commits/HEAD",
            $"https://api.github.com/repos/kylepantall/kscript/contents/libraries/isolink-connect?ref={Sha}",
            $"https://codeload.github.com/kylepantall/kscript/zip/{Sha}" }, github.Requests);
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
        Assert.Equal(3, github.Requests.Count);
        Assert.Equal("edited", File.ReadAllText(Path.Combine(installed, "index.kscript")));
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
        var conflict = Add("Slug", subdir);
        Assert.Equal(ExitCodes.Failed, conflict.Code);
        Assert.Contains("already exists", conflict.Error);
        Assert.Equal(3, github.Requests.Count);
        Assert.Equal("custom", File.ReadAllText(script));
    }

    [Fact]
    public void MissingRepositoryOrLibraryReportsTheRequestedSourceAndNeverGuessesRefs()
    {
        github.Status = HttpStatusCode.NotFound;
        var missingRepository = Add("missing");
        Assert.Equal(ExitCodes.Failed, missingRepository.Code);
        Assert.Contains("public GitHub repository kylepantall/kscript", missingRepository.Error);
        Assert.Single(github.Requests);

        github.Status = HttpStatusCode.OK;
        github.ContentsStatus = HttpStatusCode.NotFound;
        var missingLibrary = Add("missing");
        Assert.Equal(ExitCodes.Failed, missingLibrary.Code);
        Assert.Contains("libraries/missing/ was not found in kylepantall/kscript", missingLibrary.Error);
        Assert.Equal(3, github.Requests.Count);
        Assert.False(Directory.Exists(Path.Combine(root, "lib", "missing")));
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
        Assert.Equal(ExitCodes.Failed, Add("slugs").Code);
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
