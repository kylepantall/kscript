using System.IO.Compression;
using System.Net;
using System.Text.Json;
using KScript.Cli;

namespace KScript.Tests;

public sealed class PublisherDiscoveryTests : IDisposable
{
    private const string Main = "kylepantall/kscript";
    private const string Sdk = "waggr-gb/isolink-connect";
    private const string Name = "isolink-connect";
    private const string Sha = "1234567890abcdef1234567890abcdef12345678";
    private readonly string project = Directory.CreateTempSubdirectory("kscript-publisher-").FullName;
    private readonly GitHub github = new();

    public void Dispose() => Directory.Delete(project, recursive: true);

    private sealed class GitHub : HttpMessageHandler
    {
        internal Dictionary<string, string[]> Pages { get; } = new();
        internal HashSet<string> Folders { get; } = new();
        internal Dictionary<string, string> Manifests { get; } = new();
        internal List<Uri> Requests { get; } = new();
        internal HttpStatusCode ListingStatus { get; set; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var uri = request.RequestUri;
            Requests.Add(uri);
            var parts = uri.AbsolutePath.Trim('/').Split('/');
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            if (uri.Host == "api.github.com" && parts[0] == "users")
            {
                Assert.Equal("repos", parts[2]);
                response.StatusCode = ListingStatus;
                string[] repos = Pages.GetValueOrDefault(parts[1] + uri.Query) ?? Array.Empty<string>();
                response.Content = new StringContent(JsonSerializer.Serialize(repos.Select(r => new { full_name = r })));
            }
            else if (uri.Host == "api.github.com" && parts[0] == "repos")
            {
                string repository = parts[1] + "/" + parts[2];
                if (parts[3] == "commits")
                {
                    Assert.Equal("HEAD", parts[4]);
                    response.Content = new StringContent(JsonSerializer.Serialize(new { sha = Sha }));
                }
                else
                {
                    Assert.Equal("contents/libraries/" + Name, string.Join('/', parts.Skip(3)));
                    if (Folders.Contains(repository)) response.Content = new StringContent("[]");
                    else response.StatusCode = HttpStatusCode.NotFound;
                }
            }
            else if (uri.Host == "raw.githubusercontent.com")
            {
                Assert.Equal("kscript.json", parts[3]);
                if (Manifests.TryGetValue(parts[0] + "/" + parts[1], out string content))
                    response.Content = new StringContent(content);
                else response.StatusCode = HttpStatusCode.NotFound;
            }
            else if (uri.Host == "codeload.github.com")
            {
                Assert.Equal(Sha, parts[3]);
                string repository = parts[0] + "/" + parts[1];
                using var bytes = new MemoryStream();
                using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, true))
                {
                    string prefix = parts[1] + "-" + Sha + "/" + (Folders.Contains(repository) ? "libraries/" + Name + "/" : "");
                    void Add(string path, string content)
                    {
                        using var writer = new StreamWriter(archive.CreateEntry(prefix + path).Open());
                        writer.Write(content);
                    }
                    Add("index.kscript", "fn installed() => true");
                    if (Manifests.TryGetValue(repository, out string manifest)) Add("kscript.json", manifest);
                }
                response.Content = new ByteArrayContent(bytes.ToArray());
            }
            else throw new InvalidOperationException($"Unexpected request {uri}");
            return Task.FromResult(response);
        }
    }

    private (int Code, string Error) Run(params string[] args)
    {
        using var errors = new StringWriter();
        int code = LibCommand.Run(args, TextWriter.Null, errors, project, github);
        return (code, errors.ToString());
    }

    private void Page(string owner, int number, params string[] repositories) =>
        github.Pages[owner + "?per_page=100&page=" + number] = repositories;

    private const string RootPackage = """
        { "name": "isolink-connect", /* public entry */ "library": { "entry": "index.kscript", "version": "1.0.0" }, }
        """;

    [Theory]
    [InlineData(Sdk, false)]
    [InlineData("orimovo/toolkit", true)]
    [InlineData("orimovo/.github", false)]
    [InlineData("waggr-gb/_tools", true)]
    [InlineData("orimovo/-tools", false)]
    [InlineData("waggr-gb/con", true)]
    [InlineData("orimovo/con.tools", false)]
    public void BareNameFindsFuturePublicationsAndRestoresOnlyTheRecordedCommit(string repository, bool rootPackage)
    {
        string owner = repository.Split('/')[0];
        Page(owner, 1, owner + "/.github", owner + "/application", repository);
        github.Manifests[owner + "/application"] = "{ unfinished application config";
        if (rootPackage) github.Manifests[repository] = RootPackage;
        else github.Folders.Add(repository);

        var result = Run("add", Name);
        Assert.True(result.Code == ExitCodes.Ok, result.Error);
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(project, "kscript.json")));
        var pin = config.RootElement.GetProperty("libraries").GetProperty(Name);
        string url = "https://github.com/" + repository + (rootPackage ? "" : "/tree/HEAD/libraries/" + Name);
        Assert.Equal(url, pin.GetProperty("source").GetString());
        Assert.Equal(Sha, pin.GetProperty("commit").GetString());
        Directory.Delete(Path.Combine(project, "lib"), recursive: true);
        github.Requests.Clear();
        result = Run("restore");
        Assert.True(result.Code == ExitCodes.Ok, result.Error);
        Assert.True(File.Exists(Path.Combine(project, "lib", Name, "index.kscript")));
        Assert.All(github.Requests, uri => Assert.Contains(Sha, uri.AbsoluteUri));
    }

    [Fact]
    public void CollisionListsEveryDistinctQualifiedChoiceAndLetsTheUserInstallEachOne()
    {
        const string registered = "partners/catalog", other = "orimovo/sdk-collection";
        Assert.True(LibraryRepositories.Add(project, registered));
        Page("orimovo", 1, other, other);
        Page("waggr-gb", 1, Sdk);
        github.Folders.UnionWith(new[] { Main, registered, Sdk });
        github.Manifests[other] = RootPackage;
        const string original = "{ \"name\": \"consumer\" }\n";
        string config = Path.Combine(project, "kscript.json");
        File.WriteAllText(config, original);

        var result = Run("add", Name);
        Assert.Equal(ExitCodes.Failed, result.Code);
        Assert.Contains("There are 4 libraries named 'isolink-connect'. Did you mean?", result.Error);
        string[] choices = { Main, other, registered, Sdk };
        Assert.Equal(choices.Select(repo => "  " + repo + "@" + Name),
            result.Error.Split(Environment.NewLine).Where(line => line.StartsWith("  ")));
        Assert.Equal(original, File.ReadAllText(config));
        Assert.False(Directory.Exists(Path.Combine(project, "lib")));
        Assert.DoesNotContain(github.Requests, uri => uri.Host == "codeload.github.com");

        foreach (string repo in choices)
        {
            File.WriteAllText(config, original);
            github.Requests.Clear();
            var selection = Run("add", repo + "@" + Name);
            Assert.True(selection.Code == ExitCodes.Ok, selection.Error);
            Assert.DoesNotContain(github.Requests, uri => uri.AbsolutePath.StartsWith("/users/"));
            Directory.Delete(Path.Combine(project, "lib"), recursive: true);
        }
    }

    [Fact]
    public void MatchOnLaterPublisherPageStillParticipatesInAmbiguity()
    {
        github.Folders.UnionWith(new[] { Main, Sdk });
        Page("waggr-gb", 1, Enumerable.Range(0, 100).Select(i => "waggr-gb/unrelated-" + i).ToArray());
        Page("waggr-gb", 2, Sdk);
        var result = Run("add", Name);
        Assert.Contains("There are 2 libraries", result.Error);
        Assert.Contains(Sdk + "@" + Name, result.Error);
        Assert.Empty(Directory.EnumerateFileSystemEntries(project));
    }

    [Fact]
    public void IncompleteSearchCannotSilentlySelectAnEarlierMatch()
    {
        github.Folders.Add(Main);
        github.ListingStatus = HttpStatusCode.TooManyRequests;
        var result = Run("add", Name);
        Assert.Equal(ExitCodes.Failed, result.Code);
        Assert.Contains("429", result.Error);
        Assert.Empty(Directory.EnumerateFileSystemEntries(project));
        Assert.DoesNotContain(github.Requests, uri => uri.Host == "codeload.github.com");
    }
}
