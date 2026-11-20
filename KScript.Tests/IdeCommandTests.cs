using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using KScript.Ide;

using Xunit;

namespace KScript.Tests
{
    public class IdeCommandTests
    {
        private sealed class FakeHost : IIdeHost
        {
            public Dictionary<string, string> Env { get; } = new Dictionary<string, string>();
            public HashSet<string> Files { get; } = new HashSet<string>();
            public List<(string File, string[] Args)> Runs { get; } = new List<(string, string[])>();
            public Func<string, string[], (int, string)> OnRun { get; set; } = (f, a) => (0, "ok");
            public string DownloadedTag { get; private set; } = "unset";
            public bool IsWindows { get; set; }
            public bool IsMacOS { get; set; }

            public string GetEnvironmentVariable(string name) => Env.TryGetValue(name, out string v) ? v : null;
            public bool FileExists(string path) => Files.Contains(path);

            public (int ExitCode, string Output) Run(string fileName, IEnumerable<string> arguments)
            {
                string[] args = arguments.ToArray();
                Runs.Add((fileName, args));
                return OnRun(fileName, args);
            }

            public bool DownloadFails { get; set; }

            public Task<(string Path, string Tag)> DownloadExtensionAsync(string tag, TextWriter error)
            {
                DownloadedTag = tag;
                if (DownloadFails)
                {
                    return Task.FromResult<(string, string)>((null, null));
                }
                string file = Path.GetTempFileName();
                return Task.FromResult((file, tag ?? "v9.9.9"));
            }
        }

        private static (int Code, string Out, string Err) Run(FakeHost host, params string[] args)
        {
            StringWriter output = new StringWriter();
            StringWriter error = new StringWriter();
            int code = IdeCommand.Run(args, output, error, host);
            return (code, output.ToString(), error.ToString());
        }

        private static FakeHost LinuxWith(params string[] files)
        {
            FakeHost host = new FakeHost();
            host.Env["PATH"] = "/usr/local/bin:/usr/bin";
            foreach (string f in files)
            {
                host.Files.Add(f);
            }
            return host;
        }

        [Fact]
        public void DetectsEditorsOnThePath()
        {
            // Path.Combine, because the test also runs on Windows, where it joins with a backslash.
            string code = Path.Combine("/usr/local/bin", "code");
            FakeHost host = LinuxWith(code, Path.Combine("/usr/bin", "cursor"));
            List<DetectedEditor> found = IdeCommand.Detect(host);
            Assert.Equal(new[] { "vscode", "cursor" }, found.Select(d => d.Editor.Id));
            Assert.Equal(code, found[0].Launcher);
        }

        [Fact]
        public void DetectsMacAppsThatAreNotOnThePath()
        {
            FakeHost host = new FakeHost { IsMacOS = true };
            host.Env["HOME"] = "/Users/k";
            host.Env["PATH"] = "/usr/bin";
            string cursor = Path.Combine("/Applications", "Cursor.app", "Contents", "Resources", "app", "bin", "cursor");
            host.Files.Add(cursor);
            Assert.Equal(cursor, Assert.Single(IdeCommand.Detect(host)).Launcher);
        }

        [Fact]
        public void DetectsWindowsUserInstalls()
        {
            FakeHost host = new FakeHost { IsWindows = true };
            host.Env["LOCALAPPDATA"] = "C:/Users/k/AppData/Local";
            host.Env["PATH"] = "";
            string code = Path.Combine("C:/Users/k/AppData/Local", "Programs", "Microsoft VS Code", "bin", "code.cmd");
            host.Files.Add(code);
            Assert.Equal(code, Assert.Single(IdeCommand.Detect(host)).Launcher);
        }

        [Fact]
        public void InstallsIntoEveryDetectedEditor()
        {
            FakeHost host = LinuxWith("/usr/bin/code", "/usr/bin/codium");
            (int code, string output, _) = Run(host, "install");
            Assert.Equal(0, code);
            Assert.Equal(2, host.Runs.Count);
            Assert.All(host.Runs, r => Assert.Equal("--install-extension", r.Args[0]));
            Assert.All(host.Runs, r => Assert.Equal("--force", r.Args[2]));
            Assert.Contains("Installed the KScript extension in Visual Studio Code.", output);
            Assert.Contains("Installed the KScript extension in VSCodium.", output);
            Assert.Null(host.DownloadedTag);
            Assert.False(File.Exists(host.Runs[0].Args[1]), "the downloaded .vsix is cleaned up");
        }

        [Fact]
        public void InstallsIntoNamedEditorsWithALocalVsix()
        {
            string vsix = Path.GetTempFileName();
            try
            {
                FakeHost host = LinuxWith("/usr/bin/code", "/usr/bin/cursor", vsix);
                (int code, _, _) = Run(host, "install", "cursor", "--vsix", vsix);
                Assert.Equal(0, code);
                (string file, string[] args) = Assert.Single(host.Runs);
                Assert.Equal("/usr/bin/cursor", file);
                Assert.Equal(Path.GetFullPath(vsix), args[1]);
                Assert.Equal("unset", host.DownloadedTag);
                Assert.True(File.Exists(vsix), "a local .vsix is left alone");
            }
            finally
            {
                File.Delete(vsix);
            }
        }

        [Fact]
        public void InstallsASpecificRelease()
        {
            FakeHost host = LinuxWith("/usr/bin/code");
            Assert.Equal(0, Run(host, "install", "code", "--version", "0.2.0").Code);
            Assert.Equal("v0.2.0", host.DownloadedTag);
        }

        [Theory]
        [InlineData("install", "nano", "Unknown editor 'nano'")]
        [InlineData("install", "cursor", "Cursor wasn't found")]
        [InlineData("install", "neovim", "kscript ide config neovim")]
        [InlineData("uninstall", "windsurf", "Windsurf wasn't found")]
        public void ExplainsWhatIsMissing(string verb, string editor, string message)
        {
            FakeHost host = LinuxWith("/usr/bin/code");
            (int code, _, string error) = Run(host, verb, editor);
            Assert.Equal(1, code);
            Assert.Contains(message, error);
            Assert.Empty(host.Runs);
        }

        [Fact]
        public void NoEditorsIsAnError()
        {
            (int code, _, string error) = Run(LinuxWith(), "install");
            Assert.Equal(1, code);
            Assert.Contains("No supported editor found", error);
        }

        [Fact]
        public void ReportsEditorFailures()
        {
            FakeHost host = LinuxWith("/usr/bin/code");
            host.OnRun = (f, a) => (1, "Extension is not compatible");
            (int code, _, string error) = Run(host, "install");
            Assert.Equal(1, code);
            Assert.Contains("not compatible", error);
        }

        private static (int Code, string Out, string Err) UpdateInstalled(FakeHost host, string tag = "v0.3.0", bool force = false, bool checkOnly = false)
        {
            StringWriter output = new StringWriter();
            StringWriter error = new StringWriter();
            int code = IdeCommand.UpdateInstalledAsync(tag, force, checkOnly, output, error, host).GetAwaiter().GetResult();
            return (code, output.ToString(), error.ToString());
        }

        /// <summary>VS Code has 0.2.0, Cursor has 0.3.0, VSCodium doesn't have the extension.</summary>
        private static FakeHost ThreeEditors()
        {
            FakeHost host = LinuxWith("/usr/bin/code", "/usr/bin/cursor", "/usr/bin/codium");
            host.OnRun = (file, args) => args[0] != "--list-extensions" ? (0, "ok") : file switch
            {
                "/usr/bin/code" => (0, "ms-python.python@2024.1.0\nKylePantall.KScript@0.2.0\n"),
                "/usr/bin/cursor" => (0, "kylepantall.kscript@0.3.0\n"),
                _ => (0, "ms-python.python@2024.1.0\n"),
            };
            return host;
        }

        [Fact]
        public void UpdatesTheExtensionOnlyWhereItIsInstalledAndStale()
        {
            FakeHost host = ThreeEditors();
            (int code, string output, string error) = UpdateInstalled(host);
            Assert.True(code == 0, error);
            Assert.Equal("v0.3.0", host.DownloadedTag);
            (string file, string[] args) = Assert.Single(host.Runs, r => r.Args[0] == "--install-extension");
            Assert.Equal("/usr/bin/code", file);
            Assert.Equal("--force", args[2]);
            Assert.False(File.Exists(args[1]), "the downloaded .vsix is cleaned up");
            Assert.Contains("Updated the KScript extension in Visual Studio Code: 0.2.0 -> 0.3.0", output);
            Assert.Contains("The KScript extension in Cursor is up to date (0.3.0).", output);
            Assert.DoesNotContain("VSCodium", output);
            Assert.Contains("Reload the editor window", output);
        }

        [Fact]
        public void ChecksWithoutInstalling()
        {
            FakeHost host = ThreeEditors();
            (int code, string output, _) = UpdateInstalled(host, checkOnly: true);
            Assert.Equal(0, code);
            Assert.Contains("The KScript extension in Visual Studio Code is 0.2.0; kscript update will install 0.3.0.", output);
            Assert.Equal("unset", host.DownloadedTag);
            Assert.DoesNotContain(host.Runs, r => r.Args[0] == "--install-extension");
        }

        [Fact]
        public void ForceReinstallsAnExtensionThatIsAlreadyCurrent()
        {
            FakeHost host = ThreeEditors();
            (_, string output, _) = UpdateInstalled(host, force: true);
            Assert.Contains("Reinstalled the KScript extension 0.3.0 in Cursor.", output);
            Assert.Contains("Updated the KScript extension in Visual Studio Code", output);
            Assert.Equal(2, host.Runs.Count(r => r.Args[0] == "--install-extension"));
        }

        [Fact]
        public void NothingToUpdateIsQuiet()
        {
            FakeHost none = LinuxWith();
            Assert.Equal((0, "", ""), UpdateInstalled(none));

            FakeHost current = LinuxWith("/usr/bin/cursor");
            current.OnRun = (f, a) => (0, "kylepantall.kscript@0.3.0");
            (int code, string output, _) = UpdateInstalled(current);
            Assert.Equal(0, code);
            Assert.Equal("unset", current.DownloadedTag);
            Assert.DoesNotContain("Reload", output);
        }

        [Fact]
        public void ReportsExtensionUpdateFailures()
        {
            // An editor that can't list its extensions is an error, but the others are still checked.
            FakeHost broken = ThreeEditors();
            Func<string, string[], (int, string)> inner = broken.OnRun;
            broken.OnRun = (file, args) => file == "/usr/bin/cursor" ? (2, "crashed ") : inner(file, args);
            (int code, string output, string error) = UpdateInstalled(broken);
            Assert.Equal(1, code);
            Assert.Contains("Cursor couldn't list its extensions (exit code 2): crashed", error);
            Assert.Contains("Updated the KScript extension in Visual Studio Code", output);

            FakeHost onlyBroken = LinuxWith("/usr/bin/cursor");
            onlyBroken.OnRun = (f, a) => (2, "crashed");
            Assert.Equal(1, UpdateInstalled(onlyBroken).Code);

            FakeHost failsInstall = ThreeEditors();
            inner = failsInstall.OnRun;
            failsInstall.OnRun = (file, args) => args[0] == "--install-extension" ? (1, "not compatible") : inner(file, args);
            (code, output, error) = UpdateInstalled(failsInstall);
            Assert.Equal(1, code);
            Assert.Contains("Visual Studio Code couldn't update the extension (exit code 1): not compatible", error);
            Assert.DoesNotContain("Reload", output);

            FakeHost noDownload = ThreeEditors();
            noDownload.DownloadFails = true;
            Assert.Equal(1, UpdateInstalled(noDownload).Code);
            Assert.DoesNotContain(noDownload.Runs, r => r.Args[0] == "--install-extension");
        }

        [Fact]
        public void UninstallsByExtensionId()
        {
            FakeHost host = LinuxWith("/usr/bin/code");
            Assert.Equal(0, Run(host, "uninstall").Code);
            Assert.Equal(new[] { "--uninstall-extension", IdeCommand.ExtensionId }, Assert.Single(host.Runs).Args);
        }

        [Fact]
        public void UsageAndBadArguments()
        {
            FakeHost host = LinuxWith("/usr/bin/code");
            Assert.Contains("kscript ide install", Run(host, "--help").Out);
            (int code, _, string error) = Run(host, "frobnicate");
            Assert.Equal(1, code);
            Assert.Contains("Unknown ide command 'frobnicate'", error);
            Assert.Contains("Usage: kscript ide", error);

            Assert.Contains("Usage: kscript ide config", Run(host, "config").Err);
            Assert.Contains("No settings for 'nano'", Run(host, "config", "nano").Err);
            Assert.Contains("--vsix needs a value", Run(host, "install", "--vsix").Err);
            Assert.Contains("nope.vsix doesn't exist", Run(host, "install", "--vsix", "nope.vsix").Err);

            host.DownloadFails = true;
            Assert.Equal(1, Run(host, "install").Code);
            Assert.Empty(host.Runs);
        }

        [Fact]
        public void UninstallExplainsWhatHappened()
        {
            FakeHost host = LinuxWith("/usr/bin/code");
            host.OnRun = (f, a) => (1, "Extension 'kylepantall.kscript' is not installed.");
            (int code, string output, _) = Run(host, "uninstall");
            Assert.Equal(0, code);
            Assert.Contains("isn't installed in Visual Studio Code", output);

            host.OnRun = (f, a) => (2, "disk full");
            (code, _, string error) = Run(host, "uninstall");
            Assert.Equal(1, code);
            Assert.Contains("couldn't remove the extension (exit code 2): disk full", error);
        }

        [Fact]
        public void ListsEditors()
        {
            (int code, string output, _) = Run(LinuxWith("/usr/bin/code"));
            Assert.Equal(0, code);
            Assert.Contains("/usr/bin/code", output);
            Assert.Contains("not found", output);
            Assert.Contains("kscript ide config", output);
        }

        [Theory]
        [InlineData("neovim", "vim.lsp.enable('kscript')")]
        [InlineData("nvim", "vim.lsp.config")]
        [InlineData("helix", "[language-server.kscript]")]
        [InlineData("sublime", "\"command\": [\"kscript\", \"lsp\"]")]
        [InlineData("emacs", "eglot-server-programs")]
        [InlineData("cursor", "kscript ide install cursor")]
        public void PrintsSettingsForOtherEditors(string editor, string expected)
        {
            (int code, string output, _) = Run(new FakeHost(), "config", editor);
            Assert.Equal(0, code);
            Assert.Contains(expected, output);
        }

        [Fact]
        public void TheExtensionIdMatchesTheManifest()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "KScript.sln")))
            {
                dir = Path.GetDirectoryName(dir);
            }
            if (dir == null)
            {
                return;
            }
            using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "editors", "vscode", "package.json")));
            string id = manifest.RootElement.GetProperty("publisher").GetString() + "." + manifest.RootElement.GetProperty("name").GetString();
            Assert.Equal(IdeCommand.ExtensionId, id);
            Assert.Contains("kscript-vscode.vsix", manifest.RootElement.GetProperty("scripts").GetProperty("package").GetString());
        }
    }
}
