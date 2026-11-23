using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KScript.Cli;
using KScript.Compile;
using KScript.Runtime;

namespace KScript.Tests;

public sealed class LibraryRestoreTests : IDisposable
{
    private const string Pin = "1234567890abcdef1234567890abcdef12345678";
    private const string Upstream = "abcdef1234567890abcdef1234567890abcdef12";
    private const string MovedHead = "5555555555555555555555555555555555555555";
    private const string Direct = "https://github.com/acme/roots";
    private const string Catalog = "https://github.com/acme/catalog/tree/HEAD/libraries/roots";
    private readonly string root = Directory.CreateTempSubdirectory("kscript-restore-").FullName;
    private readonly GitHub github = new();
    public void Dispose() => Directory.Delete(root, recursive: true);

    private sealed class GitHub : HttpMessageHandler
    {
        internal readonly Dictionary<string, byte[]> Routes = new();
        internal readonly List<string> Requests = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri.AbsoluteUri;
            Requests.Add(url);
            var response = Routes.TryGetValue(url, out byte[] body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound);
            return Task.FromResult(response);
        }
    }

    private static byte[] Archive(params (string Path, string Text)[] files)
    {
        using var result = new MemoryStream();
        using (var zip = new ZipArchive(result, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                using var writer = new StreamWriter(zip.CreateEntry("github-root/" + file.Path).Open());
                writer.Write(file.Text);
            }
        }
        return result.ToArray();
    }

    private (int Code, string Error) Run(string cwd, params string[] args)
    {
        using var error = new StringWriter();
        return (LibCommand.Run(args, TextWriter.Null, error, cwd, github), error.ToString());
    }

    private static string Config(string dir) => Path.Combine(dir, "kscript.json");

    [Theory]
    [InlineData("orimovo/client-collection", true)]
    [InlineData("waggr-gb/libraries", false)]
    public void AnOfficialPinAndItsReceiptResolveBareNamesWithoutDiscovery(string repository, bool rootPackage)
    {
        string prefix = rootPackage ? "" : "libraries/roots/";
        string source = "https://github.com/" + repository + (rootPackage ? "" : "/tree/HEAD/libraries/roots");
        var pins = new { libraries = new { roots = new { source, commit = Pin } } };
        File.WriteAllText(Config(root), JsonSerializer.Serialize(pins));
        string contents = $"https://api.github.com/repos/{repository}/contents/libraries/roots?ref={Pin}";
        if (!rootPackage) github.Routes[contents] = JsonSerializer.SerializeToUtf8Bytes(Array.Empty<string>());
        string archive = $"https://codeload.github.com/{repository}/zip/{Pin}";
        github.Routes[archive] = Archive(
            (prefix + "kscript.json", """{"name":"roots","library":{"version":"1.0.0","entry":"client.kscript"}}"""),
            (prefix + "client.kscript", "fn root() => 7"));
        var restored = Run(root, "restore");
        Assert.True(restored.Code == 0, restored.Error);
        Assert.Equal(rootPackage ? new[] { archive } : new[] { contents, archive }, github.Requests);
        string installed = Path.Combine(root, "lib", "roots", "client.kscript");
        File.WriteAllText(installed, "fn root() => 8");
        github.Routes.Clear();
        github.Requests.Clear();
        Assert.Equal(0, Run(root, "add", "roots").Code);
        File.WriteAllText(Config(root), "{}");
        var receiptOnly = Run(root, "add", "roots");
        Assert.True(receiptOnly.Code == 0, receiptOnly.Error);
        Assert.Empty(github.Requests);
        Assert.Equal("fn root() => 8", File.ReadAllText(installed));
        var pin = JsonNode.Parse(File.ReadAllText(Config(root)))["libraries"]["roots"];
        Assert.Equal(source, (string)pin["source"]);
        Assert.Equal(Pin, (string)pin["commit"]);
    }

    [Fact]
    public void AliasOfAPublisherRootRequiresItsManifestToDeclareThatLibrary()
    {
        const string source = "https://github.com/orimovo/client-suite";
        File.WriteAllText(Config(root), JsonSerializer.Serialize(new { libraries = new { roots = new { source, commit = Pin } } }));
        string archive = $"https://codeload.github.com/orimovo/client-suite/zip/{Pin}";
        github.Routes[archive] = Archive(("kscript.json", """{"name":"different","library":{"entry":"index.kscript","version":"2.0.0"}}"""), ("index.kscript", "echo false"));
        var result = Run(root, "restore");
        Assert.NotEqual(0, result.Code);
        Assert.Contains("must declare the library name 'roots'", result.Error);
        Assert.False(Directory.Exists(Path.Combine(root, "lib", "roots")));
        Assert.Equal(new[] { archive }, github.Requests);
    }

    [Theory]
    [InlineData("orimovo/publisher-tools", false)]
    [InlineData("another-owner/publisher-tools", true)]
    public void TheQualifiedNameOfAManifestBackedRootCanBeInstalledAndReused(string repository, bool register)
    {
        if (register) LibraryRepositories.Add(root, repository);
        string api = "https://api.github.com/repos/" + repository;
        string raw = $"https://raw.githubusercontent.com/{repository}/{Pin}/kscript.json";
        const string manifest = """{"name":"roots","library":{"entry":"client.kscript","version":"1.2.3"}}""";
        string archive = $"https://codeload.github.com/{repository}/zip/{Pin}";
        github.Routes[api + "/commits/HEAD"] = JsonSerializer.SerializeToUtf8Bytes(new { sha = Pin });
        github.Routes[raw] = Encoding.UTF8.GetBytes(manifest);
        github.Routes[archive] = Archive(("kscript.json", manifest), ("client.kscript", "fn version() => 'verified'"));
        var installed = Run(root, "add", repository + "@roots");
        Assert.True(installed.Code == 0, installed.Error);
        Assert.Equal(new[] { api + "/commits/HEAD", api + "/contents/libraries/roots?ref=" + Pin, raw, archive }, github.Requests);
        string recorded = File.ReadAllText(Config(root));
        string entry = Path.Combine(root, "lib", "roots", "client.kscript");
        Assert.Equal("fn version() => 'verified'", File.ReadAllText(entry));
        github.Requests.Clear();
        Assert.Equal(0, Run(root, "add", repository + "@roots").Code);
        File.WriteAllText(Config(root), "{}");
        Assert.Equal(0, Run(root, "add", "roots").Code);
        Assert.Empty(github.Requests);
        string clone = Directory.CreateDirectory(Path.Combine(root, "fresh")).FullName;
        File.WriteAllText(Config(clone), recorded);
        Assert.Equal(0, Run(clone, "restore").Code);
        Assert.Equal(new[] { archive }, github.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FreshProjectRestoresTrackedSourceAtTheOriginalCommit(bool submodule)
    {
        const string entry = "import KScript.Math\nfn calculate(n: number) => @sqrt($n)";
        string repo = submodule ? "acme/catalog" : "acme/roots";
        string archive = $"https://codeload.github.com/{(submodule ? "acme/all-sdks" : repo)}/zip/{(submodule ? Upstream : Pin)}";
        string head = $"https://api.github.com/repos/{repo}/commits/HEAD";
        string catalogEntry = $"https://api.github.com/repos/acme/catalog/contents/libraries/roots?ref={Pin}";
        github.Routes[head] = JsonSerializer.SerializeToUtf8Bytes(new { sha = Pin });
        github.Routes[catalogEntry] = JsonSerializer.SerializeToUtf8Bytes(new { type = "submodule", sha = Upstream, submodule_git_url = "https://github.com/acme/all-sdks.git" });
        string prefix = submodule ? "libraries/roots/" : "";
        github.Routes[archive] = Archive(
            (prefix + "kscript.json", """{"name":"roots","library":{"version":"1.2.3","entry":"src/roots.kscript"},"packages":["KScript.Math"]}"""),
            (prefix + "src/roots.kscript", entry),
            (prefix + "index.kscript", "fn wrong_entry() => false"));

        const string outerProject = """{"name":"outer-project"}""";
        File.WriteAllText(Config(root), outerProject);
        string original = Directory.CreateDirectory(Path.Combine(root, "original")).FullName;
        string nested = Directory.CreateDirectory(Path.Combine(original, "site", "scripts")).FullName;
        File.WriteAllText(Config(original), """{"name":"shop","serve":{"root":"site"},"paths":{"lib":"deps"}}""");
        if (submodule)
            Assert.Equal(0, RepoCommand.Run(new[] { "add", repo }, TextWriter.Null, TextWriter.Null, nested));
        string selector = submodule ? "acme/catalog@roots" : "https://github.com/Acme/Roots.git";
        var added = Run(nested, "add", selector);
        Assert.True(added.Code == 0, added.Error);
        string config = File.ReadAllText(Config(original));
        var manifest = JsonNode.Parse(config);
        Assert.Equal("shop", (string)manifest["name"]);
        var locked = manifest["libraries"]["roots"];
        Assert.Equal(submodule ? Catalog : Direct, (string)locked["source"]);
        Assert.Equal(Pin, (string)locked["commit"]);
        github.Requests.Clear();
        var existing = Run(nested, "add", selector);
        Assert.True(existing.Code == 0, existing.Error);
        Assert.Empty(github.Requests);
        Assert.Equal(config, File.ReadAllText(Config(original)));

        // A fresh checkout has no local receipts, repository registrations or vendored source.
        string clone = Directory.CreateDirectory(Path.Combine(root, "clone")).FullName;
        nested = Directory.CreateDirectory(Path.Combine(clone, "site", "scripts")).FullName;
        File.WriteAllText(Config(clone), config);
        github.Routes[head] = JsonSerializer.SerializeToUtf8Bytes(new { sha = MovedHead });
        github.Requests.Clear();
        var restored = Run(nested, "restore");
        Assert.True(restored.Code == 0, restored.Error);
        Assert.Equal(submodule ? new[] { catalogEntry, archive } : new[] { archive }, github.Requests);

        string library = Path.Combine(clone, "site", "deps", "roots");
        string source = Path.Combine(library, "src", "roots.kscript");
        Assert.Equal(entry, File.ReadAllText(source));
        var receipt = JsonNode.Parse(File.ReadAllText(Path.Combine(library, ".kscript-lib.json")));
        Assert.Equal("src/roots.kscript", (string)receipt["entry"]);
        Assert.Equal("1.2.3", (string)receipt["version"]);
        Assert.Equal(Pin, (string)receipt["commit"]);
        if (submodule) Assert.Equal(Upstream, (string)receipt["submodule"]["commit"]);

        string main = Path.Combine(nested, "main.kscript");
        File.WriteAllText(main, "import \"../deps/roots/src/roots.kscript\" as roots\necho @roots.calculate(81)");
        using var output = new StringWriter();
        new Interpreter(KScriptHost.ForText(output)).RunFile(main);
        Assert.Equal("9", output.ToString().Trim());
        var plan = CompileCommand.Plan(main, null, null, TextWriter.Null, out _);
        Assert.Equal(entry, Encoding.UTF8.GetString(plan.Files["site/deps/roots/src/roots.kscript"]));
        Assert.Contains("KScript.Math", plan.Packages);

        File.WriteAllText(source, "fn locally_edited() => true");
        github.Requests.Clear();
        github.Routes.Clear();
        var repeated = Run(nested, "restore");
        Assert.True(repeated.Code == 0, repeated.Error);
        Assert.Empty(github.Requests);
        Assert.Equal("fn locally_edited() => true", File.ReadAllText(source));
        Assert.Equal(config, File.ReadAllText(Config(clone)));
        Assert.Equal(outerProject, File.ReadAllText(Config(root)));
    }

    [Theory]
    [InlineData("roots", Direct, "main")]
    [InlineData("../escape", Direct, Pin)]
    [InlineData("roots", "https://example.org/acme/roots", Pin)]
    public void InvalidPinsFailWithoutFetchingOrInstalling(string name, string source, string commit)
    {
        var pins = new Dictionary<string, object> { [name] = new { source, commit } };
        string config = JsonSerializer.Serialize(new { libraries = pins });
        File.WriteAllText(Config(root), config);
        Assert.NotEqual(0, Run(root, "restore").Code);
        Assert.Empty(github.Requests);
        Assert.False(Directory.Exists(Path.Combine(root, "lib", "roots")));
        Assert.Equal(config, File.ReadAllText(Config(root)));
    }

    [Fact]
    public void FreshAddMatchesPinnedSlashBranchAndRejectsConflictingSourceOffline()
    {
        const string source = "https://github.com/acme/tools/tree/feature%2Fshared/src/roots";
        string config = JsonSerializer.Serialize(new { libraries = new { roots = new { source, commit = Pin } } });
        File.WriteAllText(Config(root), config);
        var conflict = Run(root, "add", "another/roots");
        Assert.NotEqual(0, conflict.Code);
        Assert.Empty(github.Requests);
        Assert.False(Directory.Exists(Path.Combine(root, "lib", "roots")));

        string archive = $"https://codeload.github.com/acme/tools/zip/{Pin}";
        github.Routes[archive] = Archive(("src/roots/index.kscript", "fn result() => 42"));
        var added = Run(root, "add", "https://github.com/acme/tools/tree/feature/shared/src/roots");
        Assert.True(added.Code == 0, added.Error);
        Assert.Equal(new[] { archive }, github.Requests);
        Assert.Equal("fn result() => 42", File.ReadAllText(Path.Combine(root, "lib", "roots", "index.kscript")));
        Assert.Equal(config, File.ReadAllText(Config(root)));
    }

    [Theory]
    [InlineData(Direct, MovedHead)]
    [InlineData("https://github.com/another/roots", Pin)]
    public void ConflictingInstalledSourceOrRevisionPreservesLocalEdits(string source, string commit)
    {
        File.WriteAllText(Config(root), "{}");
        github.Routes["https://api.github.com/repos/acme/roots/commits/HEAD"] = JsonSerializer.SerializeToUtf8Bytes(new { sha = Pin });
        github.Routes[$"https://codeload.github.com/acme/roots/zip/{Pin}"] = Archive(("index.kscript", "fn value() => 1"));
        var added = Run(root, "add", "acme/roots");
        Assert.True(added.Code == 0, added.Error);
        string installed = Path.Combine(root, "lib", "roots", "index.kscript");
        File.WriteAllText(installed, "local edit");
        string config = JsonSerializer.Serialize(new { libraries = new { roots = new { source, commit } } });
        File.WriteAllText(Config(root), config);
        github.Requests.Clear();
        var restored = Run(root, "restore");
        Assert.NotEqual(0, restored.Code);
        Assert.NotEmpty(restored.Error);
        Assert.Empty(github.Requests);
        Assert.Equal(config, File.ReadAllText(Config(root)));
        Assert.Equal("local edit", File.ReadAllText(installed));
    }

    [Fact]
    public void RetryingPartialRestoreKeepsCompletedLibrariesAndFillsTheMissingOne()
    {
        string config = JsonSerializer.Serialize(new
        {
            libraries = new
            {
                alpha = new { source = "https://github.com/acme/alpha", commit = Pin },
                beta = new { source = "https://github.com/acme/beta", commit = Upstream }
            }
        });
        string alphaZip = $"https://codeload.github.com/acme/alpha/zip/{Pin}";
        string betaZip = $"https://codeload.github.com/acme/beta/zip/{Upstream}";
        File.WriteAllText(Config(root), config);
        github.Routes[alphaZip] = Archive(("index.kscript", "fn one() => 1"));
        Assert.NotEqual(0, Run(root, "restore").Code);
        string alpha = Path.Combine(root, "lib", "alpha", "index.kscript");
        Assert.True(File.Exists(alpha));
        Assert.False(Directory.Exists(Path.Combine(root, "lib", "beta")));
        File.WriteAllText(alpha, "fn one() => 10");
        github.Routes.Remove(alphaZip);
        github.Routes[betaZip] = Archive(("index.kscript", "fn two() => 2"));
        github.Requests.Clear();
        var retry = Run(root, "restore");
        Assert.True(retry.Code == 0, retry.Error);
        Assert.Equal(new[] { betaZip }, github.Requests);
        Assert.Equal("fn one() => 10", File.ReadAllText(alpha));
        Assert.Equal("fn two() => 2", File.ReadAllText(Path.Combine(root, "lib", "beta", "index.kscript")));
        Assert.Equal(config, File.ReadAllText(Config(root)));
    }
}
