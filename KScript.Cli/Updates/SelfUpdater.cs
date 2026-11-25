using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;

using KScript.Ide;

namespace KScript.Updates
{
    /// <summary>
    /// <c>kscript update</c>: replaces the running single-file binary with the newest GitHub release.
    /// Release assets are named <c>kscript-&lt;rid&gt;.tar.gz</c> (<c>.zip</c> on Windows), as built by
    /// .github/workflows/release.yml.
    /// </summary>
    /// <summary>What <c>kscript update</c> needs from the machine; tests swap parts of it.</summary>
    internal sealed class UpdateEnvironment
    {
        public string Repository { get; set; } = Environment.GetEnvironmentVariable("KSCRIPT_REPO") ?? SelfUpdater.DefaultRepository;
        public string CurrentVersion { get; set; } = SelfUpdater.CurrentVersion;
        public string RuntimeId { get; set; } = SelfUpdater.RuntimeId();
        public string Token { get; set; } = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        /// <summary>The installed binary to replace, or null when running from a .NET build.</summary>
        public string TargetPath { get; set; } = SelfUpdater.InstalledBinaryPath(SelfUpdater.AssemblyLocation(), Environment.ProcessPath);
        public bool Windows { get; set; } = OperatingSystem.IsWindows();
        public HttpMessageHandler Handler { get; set; } = new HttpClientHandler();
        /// <summary>Runs a binary with --version: its output, or null when it doesn't start.</summary>
        public Func<string, string> RunVersion { get; set; } = binary => SelfUpdater.RunVersion(binary);

        private IIdeHost ideHost;

        /// <summary>The editors whose KScript extension is updated too; by default the real machine, using the same GitHub settings.</summary>
        public IIdeHost IdeHost
        {
            get => ideHost ??= new SystemIdeHost(Handler, Repository, CurrentVersion, Token);
            set => ideHost = value;
        }
    }

    public static class SelfUpdater
    {
        public const string DefaultRepository = "kylepantall/kscript";

        /// <summary>The version this binary was built as, e.g. "0.1.0" (release builds pass it from the tag).</summary>
        public static string CurrentVersion
        {
            get
            {
                var assembly = Assembly.GetEntryAssembly() ?? typeof(SelfUpdater).Assembly;
                string informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                    ?? assembly.GetName().Version?.ToString(3) ?? "0.0.0";
                int plus = informational.IndexOf('+');
                return plus >= 0 ? informational.Substring(0, plus) : informational;
            }
        }

        /// <summary>The release runtime id for this machine, or null when no release is built for it.</summary>
        public static string RuntimeId(OSPlatform? os = null, Architecture? arch = null)
        {
            Architecture a = arch ?? RuntimeInformation.OSArchitecture;
            bool Is(OSPlatform p) => os.HasValue ? os.Value == p : RuntimeInformation.IsOSPlatform(p);

            if (Is(OSPlatform.Windows))
            {
                // Windows on Arm runs the x64 build under emulation.
                return a == Architecture.X64 || a == Architecture.Arm64 ? "win-x64" : null;
            }

            string cpu = a switch { Architecture.X64 => "x64", Architecture.Arm64 => "arm64", _ => null };
            if (cpu == null)
            {
                return null;
            }

            if (Is(OSPlatform.OSX))
            {
                return "osx-" + cpu;
            }

            return Is(OSPlatform.Linux) ? "linux-" + cpu : null;
        }

        public static string AssetName(string rid) => rid.StartsWith("win-", StringComparison.Ordinal) ? $"kscript-{rid}.zip" : $"kscript-{rid}.tar.gz";

        /// <summary>The release archive holding the single-file kscript that kscript compile builds programs from.</summary>
        public static string RuntimeAssetName(string rid) => AssetName(rid).Replace("kscript-", "kscript-runtime-", StringComparison.Ordinal);

        /// <summary>Parses "v1.2.3", "1.2", "1.2.3-beta.1" (the prerelease part is ignored).</summary>
        public static bool TryParseVersion(string text, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string s = text.Trim();
            if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring(1);
            }

            int cut = s.IndexOfAny(new[] { '-', '+' });
            if (cut >= 0)
            {
                s = s.Substring(0, cut);
            }

            if (!Version.TryParse(s.Contains('.') ? s : s + ".0", out var v))
            {
                return false;
            }

            version = new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
            return true;
        }

        /// <summary>True when <paramref name="latest"/> is a higher version than <paramref name="current"/>.</summary>
        public static bool IsNewer(string latest, string current) =>
            TryParseVersion(latest, out var l) && (!TryParseVersion(current, out var c) || l > c);

        public static int Run(string[] args, TextWriter output, TextWriter error) => Run(args, output, error, null);

        internal static int Run(string[] args, TextWriter output, TextWriter error, UpdateEnvironment environment)
        {
            bool checkOnly = args.Contains("--check");
            bool force = args.Contains("--force");
            bool ide = !args.Contains("--no-ide");
            string tag = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--version" || args[i] == "--tag")
                {
                    if (i + 1 >= args.Length)
                    {
                        error.WriteLine($"{args[i]} needs a release tag, e.g. {args[i]} v0.1.0");
                        return ExitCodes.Usage;
                    }
                    tag = args[++i];
                    if (!tag.StartsWith("v", StringComparison.Ordinal))
                    {
                        tag = "v" + tag;
                    }
                }
                else if (args[i] == "--help" || args[i] == "-h")
                {
                    output.WriteLine("Usage: kscript update [--check] [--force] [--version <tag>] [--no-ide]");
                    output.WriteLine("  --check           Only report whether a newer release exists.");
                    output.WriteLine("  --force           Reinstall even when already up to date.");
                    output.WriteLine("  --version <tag>   Install a specific release, e.g. v0.1.0.");
                    output.WriteLine("  --no-ide          Leave the KScript extension in your editors as it is.");
                    output.WriteLine("The KScript extension is updated to the same release in every editor that has it (VS Code, Cursor, VSCodium, Windsurf).");
                    return 0;
                }
                else if (args[i] is not ("--check" or "--force" or "--no-ide"))
                {
                    // A mistyped --check must never go on to install a release.
                    error.WriteLine($"Unknown option '{args[i]}' for kscript update. Use --check, --force, --version <tag> or --no-ide.");
                    return ExitCodes.Usage;
                }
            }

            try
            {
                return RunAsync(checkOnly, force, ide, tag, output, error, environment ?? new UpdateEnvironment()).GetAwaiter().GetResult();
            }
            catch (HttpRequestException ex)
            {
                KScript.Runtime.ErrorReport.Write(error, "Could not reach GitHub.", ex);
                return 1;
            }
            catch (UnauthorizedAccessException ex)
            {
                KScript.Runtime.ErrorReport.Write(error, "No permission to replace the kscript binary.", ex);
                error.WriteLine((environment?.Windows ?? OperatingSystem.IsWindows())
                    ? "Re-run from a terminal opened as Administrator, or reinstall with install.ps1."
                    : "Re-run with sudo, or reinstall into a folder you own with install.sh.");
                return 1;
            }
        }

        static async Task<int> RunAsync(bool checkOnly, bool force, bool ide, string tag, TextWriter output, TextWriter error, UpdateEnvironment env)
        {
            string repo = env.Repository;
            string current = env.CurrentVersion;

            string rid = env.RuntimeId;
            if (rid == null)
            {
                error.WriteLine($"No kscript release is built for {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}).");
                return 1;
            }

            using var http = new HttpClient(env.Handler) { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("kscript-updater", current));
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            string token = env.Token;
            if (!string.IsNullOrEmpty(token))
            {
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            string url = tag == null
                ? $"https://api.github.com/repos/{repo}/releases/latest"
                : $"https://api.github.com/repos/{repo}/releases/tags/{Uri.EscapeDataString(tag)}";

            using var response = await http.GetAsync(url);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                error.WriteLine(tag == null ? $"{repo} has no published releases yet." : $"Release {tag} was not found in {repo}.");
                return 1;
            }
            if ((int)response.StatusCode == 403 || (int)response.StatusCode == 429)
            {
                error.WriteLine("GitHub refused the request (rate limit). Try again later, or set GITHUB_TOKEN.");
                return 1;
            }
            response.EnsureSuccessStatusCode();

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            string latestTag = json.RootElement.GetProperty("tag_name").GetString();
            string assetName = AssetName(rid);
            string downloadUrl = json.RootElement.GetProperty("assets").EnumerateArray()
                .Where(a => a.GetProperty("name").GetString() == assetName)
                .Select(a => a.GetProperty("browser_download_url").GetString())
                .FirstOrDefault();

            bool newer = IsNewer(latestTag, current);
            if (checkOnly)
            {
                var check = new KScript.Output.CliOutput(output);
                if (newer) check.Info($"kscript {latestTag.TrimStart('v')} is available (you have {current}). Run: kscript update");
                else check.Success($"kscript {current} is up to date (latest release: {latestTag}).");
                return ide ? await IdeCommand.UpdateInstalledAsync(latestTag, force, checkOnly: true, output, error, env.IdeHost) : 0;
            }

            if (!newer && !force && tag == null)
            {
                // The CLI is current, but an editor may still have an older extension.
                new KScript.Output.CliOutput(output).Success($"kscript {current} is up to date.");
                return ide ? await IdeCommand.UpdateInstalledAsync(latestTag, force, checkOnly: false, output, error, env.IdeHost) : 0;
            }

            if (downloadUrl == null)
            {
                error.WriteLine($"Release {latestTag} has no {assetName} download.");
                return 1;
            }

            string target = env.TargetPath;
            if (target == null)
            {
                error.WriteLine("This kscript is running from a .NET build, not an installed binary, so it can't update itself.");
                error.WriteLine("Use git pull to update the source, or install a release with install.sh / install.ps1.");
                return 1;
            }

            new KScript.Output.CliOutput(output).Info($"Downloading kscript {latestTag} ({assetName})...");
            string dir = Path.GetDirectoryName(target);
            string staged = Path.Combine(dir, $".kscript-update-{Guid.NewGuid():N}" + (env.Windows ? ".exe" : ""));
            await using var archive = new MemoryStream();

            try
            {
                await using (var download = await http.GetStreamAsync(downloadUrl))
                {
                    await download.CopyToAsync(archive);
                }
                archive.Position = 0;
                ExtractBinary(archive, assetName, staged);

                if (!env.Windows && !OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(staged, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                }

                string stagedVersion = env.RunVersion(staged);
                if (stagedVersion == null)
                {
                    error.WriteLine("The downloaded binary didn't start, so the installed kscript was left as it was.");
                    return 1;
                }

                ReplaceBinary(staged, target, env.Windows);

                // The native libraries the desktop UI needs ship beside kscript (Native AOT can't embed them).
                archive.Position = 0;
                try
                {
                    ExtractCompanions(archive, assetName, dir);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    KScript.Runtime.ErrorReport.Write(error, "kscript was updated, but its UI libraries couldn't be replaced. Close any running kscript apps and run: kscript update --force", ex);
                }
            }
            finally
            {
                TryDelete(staged);
            }

            new KScript.Output.CliOutput(output).Success($"Updated kscript {current} -> {latestTag.TrimStart('v')} ([{target}])");
            return ide ? await IdeCommand.UpdateInstalledAsync(latestTag, force, checkOnly: false, output, error, env.IdeHost) : 0;
        }

        /// <summary>Where kscript's own assembly is on disk: empty in a single-file or Native AOT build, which is how those are told apart.</summary>
        [UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "An empty location is the signal: it means kscript is one binary.")]
        internal static string AssemblyLocation() => typeof(SelfUpdater).Assembly.Location;

        /// <summary>The path of the running single-file binary, or null when running as a framework-dependent dll.</summary>
        internal static string InstalledBinaryPath(string assemblyLocation, string path)
        {
            // A single-file app's assemblies have no location on disk.
            if (!string.IsNullOrEmpty(assemblyLocation) || path == null)
            {
                return null;
            }

            // Follow a symlink (e.g. /usr/local/bin/kscript -> ~/.kscript/bin/kscript) so the real file is replaced.
            var info = new FileInfo(path);
            if (info.LinkTarget != null)
            {
                var resolved = info.ResolveLinkTarget(returnFinalTarget: true);
                if (resolved != null)
                {
                    path = resolved.FullName;
                }
            }

            return path;
        }

        /// <summary>
        /// The file name of an archive entry at the top of the archive (<c>kscript</c>, <c>./libSkiaSharp.dylib</c>),
        /// or null for a folder or anything inside one.
        /// </summary>
        internal static string TopLevelName(string entryName)
        {
            string name = entryName.Replace('\\', '/');
            while (name.StartsWith("./", StringComparison.Ordinal)) name = name.Substring(2);
            return name.Length == 0 || name.Contains('/') ? null : name;
        }

        /// <summary>
        /// Writes the other files of a release archive (the desktop UI's native libraries) into <paramref name="dir"/>,
        /// each staged and then moved into place. Returns their names; older releases have none.
        /// </summary>
        internal static List<string> ExtractCompanions(Stream archive, string assetName, string dir)
        {
            var written = new List<string>();
            void Write(string name, Action<string> extract)
            {
                // Only files at the top of the archive sit beside kscript. Anything in a folder (such as a macOS
                // KScript.dsym bundle, whose DWARF/KScript would land on kscript on a case-insensitive disk) is skipped,
                // and so is any name that differs from kscript only in case.
                name = TopLevelName(name);
                if (name == null || string.Equals(name, "kscript", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "kscript.exe", StringComparison.OrdinalIgnoreCase)) return;
                string staged = Path.Combine(dir, $".kscript-update-{Guid.NewGuid():N}-{name}");
                try
                {
                    extract(staged);
                    File.Move(staged, Path.Combine(dir, name), overwrite: true);
                    written.Add(name);
                }
                finally
                {
                    TryDelete(staged);
                }
            }

            if (assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true);
                foreach (ZipArchiveEntry entry in zip.Entries) Write(entry.FullName, path => entry.ExtractToFile(path, overwrite: true));
                return written;
            }

            using var gzip = new GZipStream(archive, CompressionMode.Decompress, leaveOpen: true);
            using var tar = new TarReader(gzip);
            TarEntry e;
            while ((e = tar.GetNextEntry()) != null)
            {
                if (e.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile)
                {
                    TarEntry current = e;
                    Write(current.Name, path => current.ExtractToFile(path, overwrite: true));
                }
            }
            return written;
        }

        internal static void ExtractBinary(Stream archive, string assetName, string destination)
        {
            if (assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true);
                var entry = zip.Entries.FirstOrDefault(e => string.Equals(TopLevelName(e.FullName), "kscript.exe", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException($"{assetName} has no kscript.exe inside.");
                entry.ExtractToFile(destination, overwrite: true);
                return;
            }

            using var gzip = new GZipStream(archive, CompressionMode.Decompress, leaveOpen: true);
            using var tar = new TarReader(gzip);
            TarEntry e;
            while ((e = tar.GetNextEntry()) != null)
            {
                if ((e.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile) && TopLevelName(e.Name) == "kscript")
                {
                    e.ExtractToFile(destination, overwrite: true);
                    return;
                }
            }

            throw new InvalidDataException($"{assetName} has no kscript binary inside.");
        }

        /// <summary>Runs <c>binary --version</c> and returns its output, or null if it fails to start.</summary>
        internal static string RunVersion(string binary, int timeoutMilliseconds = 30000)
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo(binary, "--version")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                });
                string text = p.StandardOutput.ReadToEnd();
                if (!p.WaitForExit(timeoutMilliseconds))
                {
                    try { p.Kill(); } catch { }
                    return null;
                }
                return p.ExitCode == 0 ? text.Trim() : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Moves <paramref name="staged"/> over <paramref name="target"/>. On Unix a rename replaces the file even while it
        /// runs. Windows locks a running exe against writes but allows renaming it, so the old exe is moved aside to
        /// <c>kscript.exe.old</c> and removed by <see cref="CleanupPreviousBinary"/> on the next start.
        /// </summary>
        internal static void ReplaceBinary(string staged, string target, bool windows)
        {
            if (!windows)
            {
                File.Move(staged, target, overwrite: true);
                return;
            }

            string old = target + ".old";
            TryDelete(old);
            if (Path.Exists(old))
            {
                // A previous update's exe is still running; pick another name for this one.
                old = target + $".{Guid.NewGuid():N}.old";
            }

            File.Move(target, old);
            try
            {
                File.Move(staged, target);
            }
            catch
            {
                File.Move(old, target);
                throw;
            }
        }

        /// <summary>Deletes exes left behind by an earlier update on Windows. Safe to call on every start.</summary>
        public static void CleanupPreviousBinary() => CleanupPreviousBinary(Environment.ProcessPath, OperatingSystem.IsWindows());

        internal static void CleanupPreviousBinary(string path, bool windows)
        {
            if (!windows)
            {
                return;
            }

            try
            {
                string dir = Path.GetDirectoryName(path);
                if (dir == null)
                {
                    return;
                }

                foreach (var file in Directory.EnumerateFiles(dir, Path.GetFileName(path) + ".*old"))
                {
                    TryDelete(file);
                }
            }
            catch (Exception)
            {
                // Best effort only.
            }
        }

        internal static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
                // Still locked or already gone.
            }
        }
    }
}
