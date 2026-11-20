using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Text.Json;

using KScript.Projects;

namespace KScript.Apps
{
    /// <summary>
    /// <c>kscript app build [script] [--name N] [--out folder]</c>: packages a script and the folder it lives in as a
    /// desktop app for this computer: <c>Name.app</c> on macOS, a folder with <c>Name.exe</c> on Windows, and a folder
    /// with a launcher and a .desktop file on Linux. The app carries its own copy of kscript, so it runs on computers
    /// without kscript or .NET (when built with a release kscript) and needs no Swift, C# or shell scripts.
    /// </summary>
    public static class AppCommand
    {
        /// <summary>The file inside a packaged app that says which script to run.</summary>
        public const string ManifestName = "kscript-app.json";

        private static readonly string[] SkippedFolders = { ".git", "node_modules", "bin", "obj", "dist", ".vscode", ".idea" };

        public static int Run(string[] args, TextWriter output, TextWriter error) => Run(args, output, error, null, CurrentPlatform);

        /// <summary>For tests: a stand-in kscript (null finds the running one) and the platform to build for.</summary>
        internal static int Run(string[] args, TextWriter output, TextWriter error, KScriptRuntime runtime, Platform platform)
        {
            if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
            {
                output.WriteLine("Usage: kscript app build [script or project folder] [--name \"My App\"] [--out folder]");
                output.WriteLine();
                output.WriteLine("Packages a script as a desktop app for this computer: Name.app on macOS, Name/Name.exe on Windows,");
                output.WriteLine("and Name/ with a launcher and a .desktop file on Linux. The script's folder (or its kscript.json");
                output.WriteLine("project) goes inside the app, so imports, pages and data files come along.");
                output.WriteLine();
                output.WriteLine("With no script, the project's kscript.json names it: { \"app\": { \"main\": \"app.ks.xml\", \"name\": \"My App\" } }.");
                return args.Length == 0 ? 1 : 0;
            }
            if (args[0] != "build")
            {
                error.WriteLine($"Unknown app command '{args[0]}'. Try: kscript app build [script]");
                return 1;
            }

            string script = null, name = null, outDir = null;
            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--name":
                        if (++i >= args.Length) { error.WriteLine("--name needs a value, e.g. --name \"Life Lab\""); return 1; }
                        name = args[i];
                        break;
                    case "--out":
                    case "-o":
                        if (++i >= args.Length) { error.WriteLine("--out needs a folder, e.g. --out dist"); return 1; }
                        outDir = args[i];
                        break;
                    default:
                        if (args[i].StartsWith("-", StringComparison.Ordinal)) { error.WriteLine($"Unknown option '{args[i]}'."); return 1; }
                        script = args[i];
                        break;
                }
            }

            try
            {
                string built = Build(script, name, outDir, output, runtime ?? KScriptRuntime.Current(), platform);
                output.WriteLine($"Built {built}");
                output.WriteLine(platform == Platform.MacOS ? $"Open it from Finder, or run: open \"{built}\""
                    : platform == Platform.Windows ? $"Run {Path.Combine(built, Path.GetFileName(built) + ".exe")}"
                    : $"Run \"{Path.Combine(built, Path.GetFileName(built).Replace(' ', '-'))}\", or add {Path.GetFileName(built).Replace(' ', '-')}.desktop to ~/.local/share/applications");
                return 0;
            }
            catch (AppBuildException ex)
            {
                error.WriteLine(ex.Message);
                return 1;
            }
            catch (ProjectConfigException ex)
            {
                error.WriteLine(ex.Message);
                return 1;
            }
        }

        internal enum Platform { MacOS, Windows, Linux }

        private static Platform CurrentPlatform => OperatingSystem.IsMacOS() ? Platform.MacOS : OperatingSystem.IsWindows() ? Platform.Windows : Platform.Linux;

        /// <summary>Builds the app and returns its path.</summary>
        internal static string Build(string script, string name, string outDir, TextWriter log, KScriptRuntime runtime, Platform platform)
        {
            ProjectConfig project = ProjectConfig.FindAndLoad(script ?? ".");
            if (project != null) foreach (string w in project.Warnings) log.WriteLine($"{project.FilePath}: {w}");

            if (script != null && Directory.Exists(script)) script = null; // a project folder: its kscript.json names the script
            if (script == null)
            {
                if (project?.App.Main == null)
                    throw new AppBuildException("Which script? Run kscript app build <script>, or add \"app\": { \"main\": \"app.ks.xml\" } to kscript.json.");
                script = Path.Combine(project.Directory, project.App.Main);
            }
            script = Path.GetFullPath(script);
            if (!File.Exists(script)) throw new AppBuildException($"There is no script at '{script}'.");

            // Everything in the project (or, without one, the script's folder) goes in, so imports and data files work.
            string source = project?.Directory ?? Path.GetDirectoryName(script);
            if (!IsInside(script, source)) source = Path.GetDirectoryName(script);

            name ??= project?.App.Name ?? project?.Name ?? TitleFromFile(script);
            name = SafeName(name);
            string id = project?.App.Id ?? "app.kscript." + new string(name.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
            string version = project?.App.Version ?? "1.0.0";
            string icon = project?.App.Icon != null ? Path.Combine(project.Directory, project.App.Icon) : null;
            if (icon != null && !File.Exists(icon)) throw new AppBuildException($"app.icon: there is no file at '{icon}'.");

            outDir = Path.GetFullPath(outDir ?? Path.Combine(source, "dist"));
            var manifest = new Dictionary<string, string>
            {
                ["main"] = Path.GetRelativePath(source, script).Replace('\\', '/'),
                ["name"] = name,
                ["version"] = version,
            };

            string target;
            string payload;
            if (platform == Platform.MacOS)
            {
                target = Path.Combine(outDir, name + ".app");
                Recreate(target);
                string contents = Path.Combine(target, "Contents");
                string macos = Path.Combine(contents, "MacOS");
                string resources = Path.Combine(contents, "Resources");
                Directory.CreateDirectory(resources);
                string exe = runtime.CopyTo(macos, rename: null);
                payload = Path.Combine(resources, "app");
                string iconFile = null;
                if (icon != null)
                {
                    iconFile = "AppIcon" + Path.GetExtension(icon);
                    File.Copy(icon, Path.Combine(resources, iconFile));
                }
                File.WriteAllText(Path.Combine(contents, "Info.plist"), InfoPlist(name, id, version, Path.GetFileName(exe), iconFile));
                CopyProject(source, payload, outDir);
                WriteManifest(payload, manifest);
                if (OperatingSystem.IsMacOS()) Sign(target, log);
            }
            else if (platform == Platform.Windows)
            {
                target = Path.Combine(outDir, name);
                Recreate(target);
                string exe = runtime.CopyTo(target, rename: runtime.SingleFile ? name : null);
                MarkAsWindowsApp(exe);
                payload = Path.Combine(target, "app");
                CopyProject(source, payload, outDir);
                WriteManifest(payload, manifest);
                if (icon != null) File.Copy(icon, Path.Combine(target, "app" + Path.GetExtension(icon)));
            }
            else
            {
                target = Path.Combine(outDir, name);
                Recreate(target);
                string exe = runtime.CopyTo(target, rename: runtime.SingleFile ? SafeFileName(name) : null);
                payload = Path.Combine(target, "app");
                CopyProject(source, payload, outDir);
                WriteManifest(payload, manifest);
                string launcher = exe;
                if (!runtime.SingleFile)
                {
                    // A development build of kscript is several files; a small launcher gives the app its own name.
                    launcher = Path.Combine(target, SafeFileName(name));
                    File.WriteAllText(launcher, $"#!/bin/sh\nexec \"$(dirname \"$0\")/{Path.GetFileName(exe)}\" \"$@\"\n");
                    MakeExecutable(launcher);
                }
                string iconPath = null;
                if (icon != null)
                {
                    iconPath = Path.Combine(target, "app-icon" + Path.GetExtension(icon));
                    File.Copy(icon, iconPath);
                }
                File.WriteAllText(Path.Combine(target, SafeFileName(name) + ".desktop"),
                    $"[Desktop Entry]\nType=Application\nName={name}\nExec=\"{launcher}\"\n" + (iconPath != null ? $"Icon={iconPath}\n" : "") + "Terminal=false\nCategories=Utility;\n");
            }
            return target;
        }

        #region The app's files

        private static void Recreate(string target)
        {
            if (Directory.Exists(target))
            {
                // Only replace something that is clearly an earlier build, never an unrelated folder.
                bool earlier = File.Exists(Path.Combine(target, "Contents", "Resources", "app", ManifestName)) || File.Exists(Path.Combine(target, "app", ManifestName));
                if (!earlier) throw new AppBuildException($"{target} already exists and isn't an app kscript built. Move it, or pick another --out folder.");
                Directory.Delete(target, recursive: true);
            }
            Directory.CreateDirectory(target);
        }

        private static void CopyProject(string source, string dest, string outDir)
        {
            Directory.CreateDirectory(dest);
            foreach (string file in Directory.EnumerateFiles(source))
            {
                File.Copy(file, Path.Combine(dest, Path.GetFileName(file)));
            }
            foreach (string dir in Directory.EnumerateDirectories(source))
            {
                string folder = Path.GetFileName(dir);
                if (SkippedFolders.Contains(folder, StringComparer.OrdinalIgnoreCase) || folder.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) continue;
                if (IsInside(outDir, dir) || IsInside(dir, outDir)) continue;
                CopyProject(dir, Path.Combine(dest, folder), outDir);
            }
        }

        private static void WriteManifest(string payload, Dictionary<string, string> manifest) =>
            File.WriteAllText(Path.Combine(payload, ManifestName), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        private static string InfoPlist(string name, string id, string version, string exe, string icon)
        {
            static string X(string s) => SecurityElement.Escape(s);
            var plist = new StringBuilder();
            plist.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
            plist.Append("<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n");
            plist.Append("<plist version=\"1.0\">\n<dict>\n");
            void Key(string k, string v) => plist.Append($"  <key>{k}</key><string>{X(v)}</string>\n");
            Key("CFBundleName", name);
            Key("CFBundleDisplayName", name);
            Key("CFBundleIdentifier", id);
            Key("CFBundleVersion", version);
            Key("CFBundleShortVersionString", version);
            Key("CFBundleExecutable", exe);
            Key("CFBundlePackageType", "APPL");
            Key("LSMinimumSystemVersion", "11.0");
            if (icon != null) Key("CFBundleIconFile", icon);
            plist.Append("  <key>NSHighResolutionCapable</key><true/>\n");
            plist.Append("</dict>\n</plist>\n");
            return plist.ToString();
        }

        /// <summary>Ad-hoc signs a macOS app, which Apple Silicon needs before it will run an edited bundle.</summary>
        internal static void Sign(string app, TextWriter log, string codesign = "codesign")
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo(codesign, $"--force --deep --sign - \"{app}\"") { RedirectStandardError = true, RedirectStandardOutput = true });
                p.WaitForExit();
                if (p.ExitCode != 0) log.WriteLine($"codesign failed, so macOS may refuse to open the app: {p.StandardError.ReadToEnd().Trim()}");
            }
            catch (System.ComponentModel.Win32Exception)
            {
                log.WriteLine("codesign isn't available, so the app isn't signed. Install the Xcode command line tools: xcode-select --install");
            }
        }

        /// <summary>Switches a Windows executable to the GUI subsystem, so opening the app doesn't open a console too.</summary>
        internal static void MarkAsWindowsApp(string exe)
        {
            using var file = new FileStream(exe, FileMode.Open, FileAccess.ReadWrite);
            var reader = new BinaryReader(file);
            file.Position = 0x3C;
            int pe = reader.ReadInt32();
            file.Position = pe;
            if (reader.ReadUInt32() != 0x00004550) return; // "PE\0\0"
            const int subsystemOffset = 4 + 20 + 68;      // signature, file header, then Subsystem in the optional header
            file.Position = pe + subsystemOffset;
            var writer = new BinaryWriter(file);
            writer.Write((ushort)2);                      // IMAGE_SUBSYSTEM_WINDOWS_GUI
        }

        private static void MakeExecutable(string path)
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }

        #endregion

        #region Names and paths

        private static string TitleFromFile(string script)
        {
            string stem = Path.GetFileName(script);
            string ext = new[] { ".app.ks.xml", ".ks.xml", ".xml", ".ks", ".kscript" }.FirstOrDefault(e => stem.EndsWith(e, StringComparison.OrdinalIgnoreCase));
            if (ext != null) stem = stem[..^ext.Length];
            var words = stem.Split(new[] { '-', '_', '.', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(' ', words.Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
        }

        private static string SafeName(string name)
        {
            string cleaned = new string(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c) && c != '/' && c != '\\' && c != ':').ToArray()).Trim();
            if (cleaned.Length == 0) throw new AppBuildException($"'{name}' can't be used as an app name.");
            return cleaned;
        }

        private static string SafeFileName(string name) => name.Replace(' ', '-');

        private static bool IsInside(string path, string folder)
        {
            string p = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string f = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return p.StartsWith(f, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }

    public sealed class AppBuildException : Exception
    {
        public AppBuildException(string message) : base(message) { }
    }

    /// <summary>The kscript that is running: one file for release builds, a folder of files for development builds.</summary>
    internal sealed class KScriptRuntime
    {
        internal KScriptRuntime(string executable, string folder, bool singleFile)
        {
            Executable = executable;
            Folder = folder;
            SingleFile = singleFile;
        }

        public string Executable { get; }
        public string Folder { get; }
        public bool SingleFile { get; }

        public static KScriptRuntime Current() =>
            Current(AppContext.BaseDirectory, string.IsNullOrEmpty(typeof(KScriptRuntime).Assembly.Location), Environment.ProcessPath, OperatingSystem.IsWindows());

        internal static KScriptRuntime Current(string baseDir, bool singleFile, string processPath, bool windows)
        {
            if (singleFile) return new KScriptRuntime(processPath, null, true);

            // `dotnet KScript.dll` or the KScript apphost next to it: copy the whole build output.
            string apphost = Path.Combine(baseDir, windows ? "KScript.exe" : "KScript");
            if (!File.Exists(apphost))
                throw new AppBuildException($"This copy of kscript has no launcher ({apphost}), so it can't be put in an app. Use a release build of kscript.");
            return new KScriptRuntime(apphost, baseDir, false);
        }

        /// <summary>Copies kscript into <paramref name="dest"/> and returns the executable's path there.</summary>
        public string CopyTo(string dest, string rename)
        {
            Directory.CreateDirectory(dest);
            string exe;
            if (SingleFile)
            {
                exe = Path.Combine(dest, (rename ?? Path.GetFileNameWithoutExtension(Executable)) + Path.GetExtension(Executable));
                File.Copy(Executable, exe);
            }
            else
            {
                CopyAll(Folder, dest);
                exe = Path.Combine(dest, Path.GetFileName(Executable));
            }
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(exe, File.GetUnixFileMode(exe) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            return exe;
        }

        private static void CopyAll(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string f in Directory.EnumerateFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), overwrite: true);
            foreach (string d in Directory.EnumerateDirectories(from)) CopyAll(d, Path.Combine(to, Path.GetFileName(d)));
        }
    }

    /// <summary>Finds the script a packaged app should run, when kscript is running inside one.</summary>
    public static class AppLauncher
    {
        /// <summary>The manifest of the app this kscript is inside, or null when it is plain kscript.</summary>
        public static string FindManifest() => FindManifest(AppContext.BaseDirectory);

        /// <summary>The manifest of the app whose kscript runs from <paramref name="baseDir"/>, or null.</summary>
        public static string FindManifest(string baseDir)
        {
            foreach (string candidate in new[]
            {
                Path.Combine(baseDir, "..", "Resources", "app", AppCommand.ManifestName), // macOS: Name.app/Contents/MacOS/kscript
                Path.Combine(baseDir, "app", AppCommand.ManifestName),                   // Windows and Linux: Name/kscript + Name/app
            })
            {
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
            return null;
        }

        /// <summary>The script the manifest names.</summary>
        public static string MainScript(string manifest)
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(manifest));
            string main = doc.RootElement.GetProperty("main").GetString();
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifest), main));
        }

        /// <summary>Arguments the app was opened with, without the ones macOS adds (-psn_…).</summary>
        public static string[] ScriptArguments(string[] args) => args.Where(a => !a.StartsWith("-psn_", StringComparison.Ordinal)).ToArray();
    }
}
