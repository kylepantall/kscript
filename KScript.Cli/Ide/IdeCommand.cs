using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

using KScript.Updates;

namespace KScript.Ide
{
    /// <summary>An editor that installs VS Code extensions (.vsix) through its command line.</summary>
    public sealed class Editor
    {
        public Editor(string id, string name, string cli, string[] macApps, string[] windowsFolders, string[] linuxPaths, params string[] aliases)
        {
            Id = id;
            Name = name;
            Cli = cli;
            MacApps = macApps;
            WindowsFolders = windowsFolders;
            LinuxPaths = linuxPaths;
            Aliases = aliases;
        }

        public string Id { get; }
        public string Name { get; }

        /// <summary>The command line launcher, such as <c>code</c>.</summary>
        public string Cli { get; }

        /// <summary>macOS app bundle names; the launcher is at Contents/Resources/app/bin/&lt;cli&gt;.</summary>
        public string[] MacApps { get; }

        /// <summary>Install folders on Windows, relative to %LOCALAPPDATA%\Programs and %ProgramFiles%; the launcher is bin\&lt;cli&gt;.cmd.</summary>
        public string[] WindowsFolders { get; }

        public string[] LinuxPaths { get; }
        public string[] Aliases { get; }

        public bool Matches(string name) =>
            string.Equals(name, Id, StringComparison.OrdinalIgnoreCase) || string.Equals(name, Cli, StringComparison.OrdinalIgnoreCase)
            || Aliases.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Where an editor's launcher was found.</summary>
    public sealed record DetectedEditor(Editor Editor, string Launcher);

    /// <summary>What the command touches outside itself, so tests can replace it.</summary>
    public interface IIdeHost
    {
        string GetEnvironmentVariable(string name);
        bool FileExists(string path);
        bool IsWindows { get; }
        bool IsMacOS { get; }

        /// <summary>Runs a program and returns its exit code and combined output.</summary>
        (int ExitCode, string Output) Run(string fileName, IEnumerable<string> arguments);

        /// <summary>Downloads the editor extension for a release (null tag = the release matching this kscript, then the latest) into a file.</summary>
        Task<(string Path, string Tag)> DownloadExtensionAsync(string tag, TextWriter error);
    }

    /// <summary>
    /// <c>kscript ide</c>: installs the KScript editor extension into VS Code and the editors built on it
    /// (Cursor, VSCodium, Windsurf, VS Code Insiders), and prints language server settings for other editors.
    /// The extension is the <c>kscript-vscode.vsix</c> asset of each GitHub release.
    /// </summary>
    public static class IdeCommand
    {
        public const string ExtensionId = "kylepantall.kscript";
        public const string AssetName = "kscript-vscode.vsix";

        public static readonly IReadOnlyList<Editor> Editors = new[]
        {
            new Editor("vscode", "Visual Studio Code", "code",
                new[] { "Visual Studio Code.app" }, new[] { "Microsoft VS Code" }, new[] { "/usr/bin/code", "/usr/share/code/bin/code", "/snap/bin/code" }, "code", "vs-code"),
            new Editor("insiders", "Visual Studio Code - Insiders", "code-insiders",
                new[] { "Visual Studio Code - Insiders.app" }, new[] { "Microsoft VS Code Insiders" }, new[] { "/usr/bin/code-insiders", "/snap/bin/code-insiders" }, "vscode-insiders"),
            new Editor("cursor", "Cursor", "cursor",
                new[] { "Cursor.app" }, new[] { "cursor\\resources\\app" }, new[] { "/usr/bin/cursor", "/opt/Cursor/resources/app/bin/cursor" }),
            new Editor("vscodium", "VSCodium", "codium",
                new[] { "VSCodium.app" }, new[] { "VSCodium" }, new[] { "/usr/bin/codium", "/snap/bin/codium" }, "codium"),
            new Editor("windsurf", "Windsurf", "windsurf",
                new[] { "Windsurf.app" }, new[] { "Windsurf" }, new[] { "/usr/bin/windsurf" }),
        };

        /// <summary>Editors that speak LSP directly; <c>kscript ide config &lt;editor&gt;</c> prints their settings.</summary>
        public static readonly IReadOnlyDictionary<string, string> OtherEditors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["neovim"] = "Neovim 0.11+ (add to init.lua):\n\n" +
                "vim.filetype.add({ pattern = { ['.*%.ks%.xml'] = 'kscript' }, extension = { ks = 'kscript', kscript = 'kscript' } })\n" +
                "vim.lsp.config('kscript', { cmd = { 'kscript', 'lsp' }, filetypes = { 'kscript' }, root_markers = { '.git' } })\n" +
                "vim.lsp.enable('kscript')\n" +
                "-- Optional: XML highlighting for KScript files\n" +
                "vim.treesitter.language.register('xml', 'kscript')",
            ["helix"] = "Helix (add to ~/.config/helix/languages.toml):\n\n" +
                "[language-server.kscript]\ncommand = \"kscript\"\nargs = [\"lsp\"]\n\n" +
                "[[language]]\nname = \"kscript\"\nscope = \"source.kscript\"\nfile-types = [{ glob = \"*.ks.xml\" }, \"ks\", \"kscript\"]\n" +
                "roots = [\".git\"]\nlanguage-servers = [\"kscript\"]\ngrammar = \"xml\"",
            ["sublime"] = "Sublime Text (install the LSP package, then Preferences > Package Settings > LSP > Settings):\n\n" +
                "{\n  \"clients\": {\n    \"kscript\": {\n      \"enabled\": true,\n      \"command\": [\"kscript\", \"lsp\"],\n" +
                "      \"selector\": \"text.xml\"\n    }\n  }\n}",
            ["emacs"] = "Emacs 29+ with Eglot (add to init.el):\n\n" +
                "(define-derived-mode kscript-mode nxml-mode \"KScript\")\n" +
                "(add-to-list 'auto-mode-alist '(\"\\\\.ks\\\\(\\\\.xml\\\\)?\\\\'\" . kscript-mode))\n" +
                "(with-eval-after-load 'eglot\n  (add-to-list 'eglot-server-programs '(kscript-mode \"kscript\" \"lsp\")))\n" +
                "(add-hook 'kscript-mode-hook #'eglot-ensure)",
        };

        public static int Run(string[] args, TextWriter output, TextWriter error, IIdeHost host = null)
        {
            host ??= new SystemIdeHost();
            string verb = args.Length > 0 ? args[0] : "list";
            string[] rest = args.Skip(1).ToArray();

            switch (verb)
            {
                case "list":
                    return List(output, host);
                case "install":
                    return Install(rest, output, error, host).GetAwaiter().GetResult();
                case "uninstall":
                    return Uninstall(rest, output, error, host);
                case "config":
                    return Config(rest, output, error);
                case "--help":
                case "-h":
                case "help":
                    PrintUsage(output);
                    return 0;
                default:
                    error.WriteLine($"Unknown ide command '{verb}'.");
                    PrintUsage(error);
                    return 1;
            }
        }

        static void PrintUsage(TextWriter output)
        {
            output.WriteLine("Usage: kscript ide [list]                                   Show the editors kscript can set up.");
            output.WriteLine("       kscript ide install [editor...] [--vsix <file>] [--version <tag>]");
            output.WriteLine("                                                        Install the KScript extension (all detected editors by default).");
            output.WriteLine("       kscript ide uninstall [editor...]                 Remove the KScript extension.");
            output.WriteLine("       kscript ide config <editor>                       Print language server settings for " + string.Join(", ", OtherEditors.Keys) + ".");
            output.WriteLine("Editors: " + string.Join(", ", Editors.Select(e => e.Id)));
        }

        static int List(TextWriter output, IIdeHost host)
        {
            List<DetectedEditor> found = Detect(host);
            output.WriteLine("Editors for the KScript extension:");
            foreach (Editor e in Editors)
            {
                DetectedEditor d = found.FirstOrDefault(x => x.Editor == e);
                output.WriteLine($"  {e.Id,-10} {e.Name,-31} {(d != null ? d.Launcher : "not found")}");
            }
            output.WriteLine();
            output.WriteLine(found.Count > 0 ? "Run: kscript ide install" : "No supported editor found. Install VS Code, Cursor, VSCodium or Windsurf, or use kscript ide install --vsix with your editor.");
            output.WriteLine("Other editors (" + string.Join(", ", OtherEditors.Keys) + "): kscript ide config <editor>");
            return 0;
        }

        static int Config(string[] args, TextWriter output, TextWriter error)
        {
            string name = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
            if (name == null)
            {
                error.WriteLine("Usage: kscript ide config <" + string.Join("|", OtherEditors.Keys) + ">");
                return 1;
            }
            if (name.Equals("nvim", StringComparison.OrdinalIgnoreCase) || name.Equals("vim", StringComparison.OrdinalIgnoreCase))
            {
                name = "neovim";
            }
            if (OtherEditors.TryGetValue(name, out string text))
            {
                output.WriteLine(text);
                return 0;
            }
            if (Editors.Any(e => e.Matches(name)))
            {
                output.WriteLine($"{name} uses the KScript extension. Run: kscript ide install {name}");
                return 0;
            }
            error.WriteLine($"No settings for '{name}'. Any editor with LSP support works: run \"kscript lsp\" as the language server over stdio for *.ks.xml files.");
            return 1;
        }

        /// <summary>Finds each supported editor's launcher, on the PATH or in its usual install folder.</summary>
        public static List<DetectedEditor> Detect(IIdeHost host)
        {
            List<DetectedEditor> found = new List<DetectedEditor>();
            foreach (Editor e in Editors)
            {
                string launcher = FindLauncher(e, host);
                if (launcher != null)
                {
                    found.Add(new DetectedEditor(e, launcher));
                }
            }
            return found;
        }

        internal static string FindLauncher(Editor e, IIdeHost host)
        {
            char separator = host.IsWindows ? ';' : ':';
            string[] names = host.IsWindows ? new[] { e.Cli + ".cmd", e.Cli + ".exe" } : new[] { e.Cli };
            foreach (string dir in (host.GetEnvironmentVariable("PATH") ?? string.Empty).Split(separator, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (string name in names)
                {
                    string candidate = Path.Combine(dir.Trim('"'), name);
                    if (host.FileExists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            IEnumerable<string> known;
            if (host.IsMacOS)
            {
                string home = host.GetEnvironmentVariable("HOME") ?? string.Empty;
                known = e.MacApps.SelectMany(app => new[] { "/Applications", Path.Combine(home, "Applications") }
                    .Select(root => Path.Combine(root, app, "Contents", "Resources", "app", "bin", e.Cli)));
            }
            else if (host.IsWindows)
            {
                string[] roots = new[] { host.GetEnvironmentVariable("LOCALAPPDATA") is string l ? Path.Combine(l, "Programs") : null,
                        host.GetEnvironmentVariable("ProgramFiles"), host.GetEnvironmentVariable("ProgramFiles(x86)") }
                    .Where(r => !string.IsNullOrEmpty(r)).ToArray();
                known = e.WindowsFolders.SelectMany(folder => roots.Select(root => Path.Combine(root, folder, "bin", e.Cli + ".cmd")));
            }
            else
            {
                known = e.LinuxPaths;
            }
            return known.FirstOrDefault(host.FileExists);
        }

        static (List<DetectedEditor> Targets, int Error) ChooseEditors(IEnumerable<string> names, TextWriter error, IIdeHost host)
        {
            List<DetectedEditor> detected = Detect(host);
            List<string> requested = names.ToList();
            if (requested.Count == 0)
            {
                if (detected.Count == 0)
                {
                    error.WriteLine("No supported editor found (VS Code, Cursor, VSCodium, Windsurf). Install one, or put its command line launcher (such as 'code') on your PATH.");
                    error.WriteLine("Other editors: kscript ide config <" + string.Join("|", OtherEditors.Keys) + ">");
                    return (null, 1);
                }
                return (detected, 0);
            }

            List<DetectedEditor> targets = new List<DetectedEditor>();
            foreach (string name in requested)
            {
                Editor editor = Editors.FirstOrDefault(e => e.Matches(name));
                if (editor == null)
                {
                    error.WriteLine(OtherEditors.ContainsKey(name) || name is "nvim" or "vim"
                        ? $"{name} doesn't use extensions. Run: kscript ide config {name}"
                        : $"Unknown editor '{name}'. Supported: {string.Join(", ", Editors.Select(e => e.Id))}.");
                    return (null, 1);
                }
                DetectedEditor found = detected.FirstOrDefault(d => d.Editor == editor);
                if (found == null)
                {
                    error.WriteLine($"{editor.Name} wasn't found. Put its '{editor.Cli}' command on your PATH (in the editor: Command Palette > Shell Command: Install '{editor.Cli}' command in PATH).");
                    return (null, 1);
                }
                targets.Add(found);
            }
            return (targets, 0);
        }

        static async Task<int> Install(string[] args, TextWriter output, TextWriter error, IIdeHost host)
        {
            string vsix = null;
            string tag = null;
            List<string> names = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--vsix" || args[i] == "--version" || args[i] == "--tag")
                {
                    if (i + 1 >= args.Length)
                    {
                        error.WriteLine($"{args[i]} needs a value.");
                        return 1;
                    }
                    if (args[i] == "--vsix") vsix = args[++i];
                    else tag = args[++i].StartsWith("v", StringComparison.Ordinal) ? args[i] : "v" + args[i];
                }
                else
                {
                    names.Add(args[i]);
                }
            }

            (List<DetectedEditor> targets, int code) = ChooseEditors(names, error, host);
            if (targets == null)
            {
                return code;
            }

            bool downloaded = false;
            if (vsix != null)
            {
                if (!host.FileExists(vsix))
                {
                    error.WriteLine($"{vsix} doesn't exist.");
                    return 1;
                }
                vsix = Path.GetFullPath(vsix);
            }
            else
            {
                (string path, string releaseTag) = await host.DownloadExtensionAsync(tag, error);
                if (path == null)
                {
                    return 1;
                }
                output.WriteLine($"Downloaded the KScript extension from release {releaseTag}.");
                vsix = path;
                downloaded = true;
            }

            int failures = 0;
            try
            {
                foreach (DetectedEditor target in targets)
                {
                    (int exit, string text) = host.Run(target.Launcher, new[] { "--install-extension", vsix, "--force" });
                    if (exit == 0)
                    {
                        output.WriteLine($"Installed the KScript extension in {target.Editor.Name}.");
                    }
                    else
                    {
                        failures++;
                        error.WriteLine($"{target.Editor.Name} couldn't install the extension (exit code {exit}): {text.Trim()}");
                    }
                }
            }
            finally
            {
                if (downloaded)
                {
                    try { File.Delete(vsix); } catch { }
                }
            }

            if (failures == 0)
            {
                output.WriteLine("Reload the editor window to start it. The extension uses this kscript for errors, completion and hover; keep it current with kscript update.");
            }
            return failures == 0 ? 0 : 1;
        }

        static int Uninstall(string[] args, TextWriter output, TextWriter error, IIdeHost host)
        {
            (List<DetectedEditor> targets, int code) = ChooseEditors(args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)), error, host);
            if (targets == null)
            {
                return code;
            }
            int failures = 0;
            foreach (DetectedEditor target in targets)
            {
                (int exit, string text) = host.Run(target.Launcher, new[] { "--uninstall-extension", ExtensionId });
                if (exit == 0)
                {
                    output.WriteLine($"Removed the KScript extension from {target.Editor.Name}.");
                }
                else if (text.Contains("not installed", StringComparison.OrdinalIgnoreCase))
                {
                    output.WriteLine($"The KScript extension isn't installed in {target.Editor.Name}.");
                }
                else
                {
                    failures++;
                    error.WriteLine($"{target.Editor.Name} couldn't remove the extension (exit code {exit}): {text.Trim()}");
                }
            }
            return failures == 0 ? 0 : 1;
        }
    }

    /// <summary>The real machine: environment, files, processes and GitHub releases.</summary>
    public sealed class SystemIdeHost : IIdeHost
    {
        public string GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);
        public bool FileExists(string path) => File.Exists(path);
        public bool IsWindows => OperatingSystem.IsWindows();
        public bool IsMacOS => OperatingSystem.IsMacOS();

        public (int ExitCode, string Output) Run(string fileName, IEnumerable<string> arguments)
        {
            ProcessStartInfo info = new ProcessStartInfo(fileName)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string a in arguments)
            {
                info.ArgumentList.Add(a);
            }
            try
            {
                using Process p = Process.Start(info);
                Task<string> stdout = p.StandardOutput.ReadToEndAsync();
                Task<string> stderr = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(180000))
                {
                    try { p.Kill(true); } catch { }
                    return (-1, "Timed out.");
                }
                return (p.ExitCode, stdout.Result + stderr.Result);
            }
            catch (Exception ex)
            {
                return (-1, ex.Message);
            }
        }

        public async Task<(string Path, string Tag)> DownloadExtensionAsync(string tag, TextWriter error)
        {
            string repo = Environment.GetEnvironmentVariable("KSCRIPT_REPO") ?? SelfUpdater.DefaultRepository;
            using HttpClient http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("kscript-ide", SelfUpdater.CurrentVersion));
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            string token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
            if (!string.IsNullOrEmpty(token))
            {
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            // The extension that matches this kscript first, then the latest release.
            List<string> tags = new List<string>();
            if (tag != null)
            {
                tags.Add(tag);
            }
            else
            {
                if (SelfUpdater.TryParseVersion(SelfUpdater.CurrentVersion, out _) && !SelfUpdater.CurrentVersion.StartsWith("0.0.0", StringComparison.Ordinal))
                {
                    tags.Add("v" + SelfUpdater.CurrentVersion);
                }
                tags.Add(null);
            }

            try
            {
                foreach (string t in tags)
                {
                    string url = t == null
                        ? $"https://api.github.com/repos/{repo}/releases/latest"
                        : $"https://api.github.com/repos/{repo}/releases/tags/{Uri.EscapeDataString(t)}";
                    using HttpResponseMessage response = await http.GetAsync(url);
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        continue;
                    }
                    if ((int)response.StatusCode == 403 || (int)response.StatusCode == 429)
                    {
                        error.WriteLine("GitHub refused the request (rate limit). Try again later, or set GITHUB_TOKEN.");
                        return (null, null);
                    }
                    response.EnsureSuccessStatusCode();

                    using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    string releaseTag = json.RootElement.GetProperty("tag_name").GetString();
                    string download = json.RootElement.GetProperty("assets").EnumerateArray()
                        .Where(a => a.GetProperty("name").GetString() == IdeCommand.AssetName)
                        .Select(a => a.GetProperty("browser_download_url").GetString())
                        .FirstOrDefault();
                    if (download == null)
                    {
                        continue;
                    }

                    string file = Path.Combine(Path.GetTempPath(), $"kscript-{releaseTag}-{Guid.NewGuid():N}.vsix");
                    await using (Stream source = await http.GetStreamAsync(download))
                    await using (FileStream target = File.Create(file))
                    {
                        await source.CopyToAsync(target);
                    }
                    return (file, releaseTag);
                }
            }
            catch (HttpRequestException ex)
            {
                error.WriteLine($"Could not reach GitHub: {ex.Message}");
                return (null, null);
            }

            error.WriteLine(tag != null
                ? $"Release {tag} has no {IdeCommand.AssetName}."
                : $"No release of {repo} has the editor extension ({IdeCommand.AssetName}) yet. It ships with each release after v0.1.0.");
            return (null, null);
        }
    }
}
