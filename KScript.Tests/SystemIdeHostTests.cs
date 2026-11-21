using System.Net;
using System.Net.Http;
using KScript.Ide;

namespace KScript.Tests;

/// <summary>The real IDE host: processes, and the extension download against a fake GitHub.</summary>
public class SystemIdeHostTests
{
    private const string Repo = "https://api.github.com/repos/me/ks/releases/";

    private sealed class FakeGitHub : HttpMessageHandler
    {
        public Dictionary<string, Func<HttpResponseMessage>> Routes { get; } = new();

        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (request.RequestUri!.Host == "unreachable.test") throw new HttpRequestException("no route");
            return Task.FromResult(Routes.TryGetValue(request.RequestUri.ToString(), out var answer) ? answer() : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static Func<HttpResponseMessage> Release(string tag, string asset = IdeCommand.AssetName, string url = "https://example.test/x.vsix") => () => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent($"{{ \"tag_name\": \"{tag}\", \"assets\": [ {{ \"name\": \"{asset}\", \"browser_download_url\": \"{url}\" }} ] }}"),
    };

    private static (string Path, string Tag, string Error) Download(FakeGitHub github, string tag, string version = "0.3.0", string token = null)
    {
        var host = new SystemIdeHost(github, "me/ks", version, token);
        var error = new StringWriter();
        var (path, releaseTag) = host.DownloadExtensionAsync(tag, error).GetAwaiter().GetResult();
        return (path, releaseTag, error.ToString());
    }

    [Fact]
    public void Downloads_the_matching_release_then_the_latest()
    {
        var github = new FakeGitHub();
        github.Routes["https://example.test/x.vsix"] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("vsix") };
        github.Routes[Repo + "latest"] = Release("v0.4.0");

        // v0.3.0 has no release, so the latest is used.
        var (path, tag, _) = Download(github, null, token: "secret");
        try
        {
            Assert.Equal("v0.4.0", tag);
            Assert.Equal("vsix", File.ReadAllText(path));
            Assert.Equal(Repo + "tags/v0.3.0", github.Requests[0].RequestUri!.ToString());
            Assert.Equal("Bearer", github.Requests[0].Headers.Authorization!.Scheme);
        }
        finally
        {
            File.Delete(path);
        }

        // A development build goes straight to the latest release.
        github.Requests.Clear();
        File.Delete(Download(github, null, version: "0.0.0-dev").Path);
        Assert.Equal(Repo + "latest", github.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public void Explains_why_there_is_nothing_to_install()
    {
        var github = new FakeGitHub();
        github.Routes[Repo + "tags/v0.1.0"] = Release("v0.1.0", "kscript-linux-x64.tar.gz");
        Assert.Contains("Release v0.1.0 has no kscript-vscode.vsix", Download(github, "v0.1.0").Error);
        Assert.Contains("No release of me/ks has the editor extension", Download(github, null).Error);

        github.Routes[Repo + "latest"] = () => new HttpResponseMessage((HttpStatusCode)429);
        Assert.Contains("rate limit", Download(github, null).Error);

        github.Routes[Repo + "latest"] = Release("v0.4.0", url: "https://unreachable.test/x.vsix");
        var (path, _, error) = Download(github, null);
        Assert.Null(path);
        Assert.Contains("ERROR  Could not reach GitHub.", error);
        Assert.Contains(": no route", error);
    }

    [Fact]
    public void Runs_editor_command_lines()
    {
        var host = new SystemIdeHost();
        Assert.Equal(OperatingSystem.IsWindows(), host.IsWindows);
        Assert.Equal(OperatingSystem.IsMacOS(), host.IsMacOS);
        Assert.Equal(Environment.GetEnvironmentVariable("PATH"), host.GetEnvironmentVariable("PATH"));
        Assert.False(host.FileExists(Path.Combine(Path.GetTempPath(), "no-such-file-" + Guid.NewGuid())));
        if (OperatingSystem.IsWindows()) return;

        var (exit, output) = host.Run("/bin/sh", new[] { "-c", "echo out; echo err >&2; exit 3" });
        Assert.Equal(3, exit);
        Assert.Contains("out", output);
        Assert.Contains("err", output);

        Assert.Equal(-1, host.Run("/no/such/editor", Array.Empty<string>()).ExitCode);

        var slow = new SystemIdeHost { RunTimeoutMilliseconds = 100 };
        Assert.Equal((-1, "Timed out."), slow.Run("/bin/sh", new[] { "-c", "sleep 5" }));
    }
}
