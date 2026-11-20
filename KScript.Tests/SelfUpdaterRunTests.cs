using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using KScript.Updates;

namespace KScript.Tests;

/// <summary>kscript update end to end, against a fake GitHub and a temp folder instead of the real binary.</summary>
public sealed class SelfUpdaterRunTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("kscript-update-run").FullName;

    public void Dispose() => Directory.Delete(dir, true);

    /// <summary>Answers each URL from a table; anything else is a 404.</summary>
    private sealed class FakeGitHub : HttpMessageHandler
    {
        public Dictionary<string, Func<HttpResponseMessage>> Routes { get; } = new();

        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(Routes.TryGetValue(request.RequestUri!.ToString(), out var answer) ? answer() : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private const string Latest = "https://api.github.com/repos/me/ks/releases/latest";
    private const string Download = "https://example.test/kscript-linux-x64.tar.gz";

    private static HttpResponseMessage Json(string tag, string asset = "kscript-linux-x64.tar.gz") => new(HttpStatusCode.OK)
    {
        Content = new StringContent($"{{ \"tag_name\": \"{tag}\", \"assets\": [ {{ \"name\": \"other.zip\", \"browser_download_url\": \"x\" }}, {{ \"name\": \"{asset}\", \"browser_download_url\": \"{Download}\" }} ] }}"),
    };

    private static byte[] Tarball(string content)
    {
        var archive = new MemoryStream();
        using (var gz = new GZipStream(archive, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gz))
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, "kscript") { DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content)) };
            tar.WriteEntry(entry);
        }
        return archive.ToArray();
    }

    private static byte[] Zip(string content)
    {
        var archive = new MemoryStream();
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
        using (var w = new StreamWriter(zip.CreateEntry("kscript.exe").Open()))
        {
            w.Write(content);
        }
        return archive.ToArray();
    }

    private (FakeGitHub GitHub, UpdateEnvironment Env) Setup(string tag = "v0.2.0")
    {
        var github = new FakeGitHub();
        github.Routes[Latest] = () => Json(tag);
        github.Routes[Download] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Tarball("new binary")) };
        string target = Path.Combine(dir, "kscript");
        File.WriteAllText(target, "old binary");
        return (github, new UpdateEnvironment
        {
            Repository = "me/ks",
            CurrentVersion = "0.1.0",
            RuntimeId = "linux-x64",
            Token = null,
            TargetPath = target,
            Windows = false,
            Handler = github,
            RunVersion = _ => "kscript 0.2.0",
        });
    }

    private static (int Code, string Out, string Err) Update(UpdateEnvironment env, params string[] args)
    {
        var o = new StringWriter { NewLine = "\n" };
        var e = new StringWriter { NewLine = "\n" };
        int code = SelfUpdater.Run(args, o, e, env);
        return (code, o.ToString(), e.ToString());
    }

    [Fact]
    public void Installs_a_newer_release()
    {
        var (github, env) = Setup();
        env.Token = "secret";
        var (code, output, error) = Update(env);
        Assert.True(code == 0, error);
        Assert.Contains("Updated kscript 0.1.0 -> 0.2.0", output);
        Assert.Equal("new binary", File.ReadAllText(env.TargetPath));
        Assert.Equal("Bearer", github.Requests[0].Headers.Authorization!.Scheme);
        Assert.Single(Directory.GetFiles(dir)); // the staged download is gone
    }

    [Fact]
    public void Installs_on_windows_by_moving_the_old_exe_aside()
    {
        var (github, env) = Setup();
        env.Windows = true;
        env.RuntimeId = "win-x64";
        github.Routes[Latest] = () => Json("v0.2.0", "kscript-win-x64.zip");
        github.Routes[Download] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Zip("new exe")) };
        Assert.Equal(0, Update(env).Code);
        Assert.Equal("new exe", File.ReadAllText(env.TargetPath));
        Assert.Equal("old binary", File.ReadAllText(env.TargetPath + ".old"));

        // The next start removes it.
        SelfUpdater.CleanupPreviousBinary(env.TargetPath, windows: true);
        Assert.False(File.Exists(env.TargetPath + ".old"));
    }

    [Fact]
    public void Checks_and_up_to_date()
    {
        var (_, env) = Setup();
        Assert.Contains("kscript 0.2.0 is available (you have 0.1.0)", Update(env, "--check").Out);
        env.CurrentVersion = "0.2.0";
        Assert.Contains("is up to date (latest release: v0.2.0)", Update(env, "--check").Out);
        Assert.Contains("kscript 0.2.0 is up to date.", Update(env).Out);
        Assert.Equal("old binary", File.ReadAllText(env.TargetPath));
    }

    [Fact]
    public void Usage()
    {
        var (_, env) = Setup();
        Assert.Contains("Usage: kscript update", Update(env, "--help").Out);
        Assert.Contains("--version needs a release tag", Update(env, "--version").Err);
    }

    [Fact]
    public void Reports_what_went_wrong()
    {
        var (github, env) = Setup();
        Assert.Contains("Release v9.9.9 was not found in me/ks", Update(env, "--version", "9.9.9").Err);

        github.Routes.Remove(Latest);
        Assert.Contains("me/ks has no published releases yet", Update(env).Err);

        github.Routes[Latest] = () => new HttpResponseMessage(HttpStatusCode.Forbidden);
        Assert.Contains("rate limit", Update(env).Err);
        github.Routes[Latest] = () => new HttpResponseMessage(HttpStatusCode.InternalServerError);
        Assert.Contains("Could not reach GitHub", Update(env).Err);

        github.Routes[Latest] = () => Json("v0.2.0", "kscript-osx-arm64.tar.gz");
        Assert.Contains("has no kscript-linux-x64.tar.gz download", Update(env).Err);

        github.Routes[Latest] = () => Json("v0.2.0");
        env.RunVersion = _ => null;
        Assert.Contains("didn't start", Update(env).Err);
        Assert.Equal("old binary", File.ReadAllText(env.TargetPath));

        env.RunVersion = _ => throw new UnauthorizedAccessException("denied");
        Assert.Contains("Re-run with sudo", Update(env).Err);
        env.Windows = true;
        Assert.Contains("as Administrator", Update(env, "--force").Err);
        env.Windows = false;

        env.TargetPath = null;
        Assert.Contains("running from a .NET build", Update(env, "--force").Err);

        env.RuntimeId = null;
        Assert.Contains("No kscript release is built for", Update(env).Err);
    }

    [Fact]
    public void Runs_the_downloaded_binary_to_check_it()
    {
        string Script(string name, string body)
        {
            string path = Path.Combine(dir, name);
            File.WriteAllText(path, "#!/bin/sh\n" + body + "\n");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return path;
        }
        if (OperatingSystem.IsWindows()) return;

        Assert.Equal("kscript 0.2.0", SelfUpdater.RunVersion(Script("ok", "echo 'kscript 0.2.0'")));
        Assert.Null(SelfUpdater.RunVersion(Script("fails", "exit 3")));
        Assert.Null(SelfUpdater.RunVersion(Path.Combine(dir, "missing")));
        Assert.Null(SelfUpdater.RunVersion(Script("hangs", "exec >&- 2>&-\nsleep 5"), timeoutMilliseconds: 100));
    }

    [Fact]
    public void Finds_the_installed_binary()
    {
        Assert.Null(SelfUpdater.InstalledBinaryPath("/some/KScript.Cli.dll", "/usr/bin/dotnet"));
        Assert.Null(SelfUpdater.InstalledBinaryPath("", null));
        string real = Path.Combine(dir, "kscript");
        File.WriteAllText(real, "binary");
        Assert.Equal(real, SelfUpdater.InstalledBinaryPath("", real));
        string link = Path.Combine(dir, "link");
        File.CreateSymbolicLink(link, real);
        Assert.Equal(real, SelfUpdater.InstalledBinaryPath("", link));
        Assert.Matches(@"^\d+\.\d+\.\d+", SelfUpdater.CurrentVersion);
    }

    [Fact]
    public void Windows_replacement_keeps_the_old_exe_when_something_is_in_the_way()
    {
        string target = Path.Combine(dir, "kscript.exe");
        File.WriteAllText(target, "old");
        Directory.CreateDirectory(target + ".old"); // can't be deleted as a file, so another name is picked
        string staged = Path.Combine(dir, "staged");
        File.WriteAllText(staged, "new");
        SelfUpdater.ReplaceBinary(staged, target, windows: true);
        Assert.Equal("new", File.ReadAllText(target));
        Assert.Single(Directory.GetFiles(dir, "kscript.exe.*.old"));

        // A staged file that has gone puts the old exe back.
        Assert.ThrowsAny<IOException>(() => SelfUpdater.ReplaceBinary(Path.Combine(dir, "gone"), target, windows: true));
        Assert.Equal("new", File.ReadAllText(target));

        // Cleanup is best effort and does nothing off Windows.
        SelfUpdater.CleanupPreviousBinary(target, windows: false);
        Assert.Single(Directory.GetFiles(dir, "kscript.exe.*.old"));
        SelfUpdater.CleanupPreviousBinary(null, windows: true);
        SelfUpdater.CleanupPreviousBinary(Path.Combine(dir, "missing", "kscript.exe"), windows: true);
        SelfUpdater.CleanupPreviousBinary(target, windows: true);
        Assert.Empty(Directory.GetFiles(dir, "kscript.exe.*.old"));
    }

    [Fact]
    public void The_public_entry_points()
    {
        var o = new StringWriter();
        Assert.Equal(0, SelfUpdater.Run(new[] { "-h" }, o, TextWriter.Null));
        Assert.Contains("--check", o.ToString());
        SelfUpdater.CleanupPreviousBinary();
        Assert.False(SelfUpdater.TryParseVersion(" ", out _));

        var archive = new MemoryStream();
        using (var gz = new GZipStream(archive, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gz))
        {
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "README.md") { DataStream = new MemoryStream(new byte[] { 1 }) });
        }
        archive.Position = 0;
        Assert.Contains("has no kscript binary inside", Assert.Throws<InvalidDataException>(() => SelfUpdater.ExtractBinary(archive, "kscript-linux-x64.tar.gz", Path.Combine(dir, "x"))).Message);
    }

    [Fact]
    public void Deleting_is_best_effort()
    {
        // /proc files exist but can't be deleted, even by root.
        if (File.Exists("/proc/self/status")) SelfUpdater.TryDelete("/proc/self/status");
    }
}
