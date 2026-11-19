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

            public Task<(string Path, string Tag)> DownloadExtensionAsync(string tag, TextWriter error)
            {
                DownloadedTag = tag;
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
            FakeHost host = LinuxWith("/usr/local/bin/code", "/usr/bin/cursor");
            List<DetectedEditor> found = IdeCommand.Detect(host);
            Assert.Equal(new[] { "vscode", "cursor" }, found.Select(d => d.Editor.Id));
            Assert.Equal("/usr/local/bin/code", found[0].Launcher);
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

        [Fact]
        public void UninstallsByExtensionId()
        {
            FakeHost host = LinuxWith("/usr/bin/code");
            Assert.Equal(0, Run(host, "uninstall").Code);
            Assert.Equal(new[] { "--uninstall-extension", IdeCommand.ExtensionId }, Assert.Single(host.Runs).Args);
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
