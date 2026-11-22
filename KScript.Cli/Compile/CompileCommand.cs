using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;

using KScript.Projects;
using KScript.Runtime;
using KScript.Updates;

namespace KScript.Compile
{
    /// <summary>What <c>kscript compile</c> needs from the machine; tests swap parts of it.</summary>
    internal sealed class CompileEnvironment
    {
        public string Version { get; set; } = SelfUpdater.CurrentVersion;
        public string Repository { get; set; } = Environment.GetEnvironmentVariable("KSCRIPT_REPO") ?? SelfUpdater.DefaultRepository;
        /// <summary>The release runtime id of this computer, such as osx-arm64.</summary>
        public string HostRuntime { get; set; } = SelfUpdater.RuntimeId();
        /// <summary>
        /// The running kscript when it is a single-file build, else null. A Native AOT kscript (the release build) has
        /// no .NET bundle to build programs from, so it downloads the matching runtime, as it does for other platforms.
        /// </summary>
        public string CurrentBinary { get; set; } = CurrentBundle(System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported);
        /// <summary>Where kscript keeps the runtimes it downloads for other platforms.</summary>
        public string RuntimeCache { get; set; } = DefaultCache();
        public HttpMessageHandler Handler { get; set; } = new HttpClientHandler();
        /// <summary>Seals a Mac app with Apple's codesign, which only exists on a Mac. Returns an error or null.</summary>
        public Func<string, string> Codesign { get; set; } = OperatingSystem.IsMacOS() ? RunCodesign : null;

        internal static string CurrentBundle(bool managed) =>
            managed ? SelfUpdater.InstalledBinaryPath(SelfUpdater.AssemblyLocation(), Environment.ProcessPath) : null;

        internal static string DefaultCache() =>
            OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "kscript", "runtimes")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kscript", "runtimes");

        internal static string RunCodesign(string app) => RunCodesign(app, "codesign");

        internal static string RunCodesign(string app, string tool)
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo(tool) { ArgumentList = { "--force", "--deep", "--sign", "-", app }, RedirectStandardError = true, RedirectStandardOutput = true });
                p.WaitForExit();
                return p.ExitCode == 0 ? null : p.StandardError.ReadToEnd().Trim();
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                return ex.Message;
            }
        }
    }

    /// <summary>
    /// <c>kscript compile [script or folder] [--target ...] [--out dist] [--name "My App"]</c>: builds programs that run
    /// on computers without kscript. Each is one file (a Mac desktop app is an .app around one file), made from the
    /// release kscript for that platform with only the packages the script uses, the script, its imports and its
    /// files inside. Programs for other platforms use that platform's release kscript, downloaded once.
    /// </summary>
    public static class CompileCommand
    {
        internal sealed record Target(string Name, string Runtime)
        {
            public bool Windows => Runtime.StartsWith("win-", StringComparison.Ordinal);
            public bool Mac => Runtime.StartsWith("osx-", StringComparison.Ordinal);
        }

        internal static readonly Target[] Targets =
        {
            new Target("mac-arm64", "osx-arm64"),
            new Target("mac-x64", "osx-x64"),
            new Target("linux-x64", "linux-x64"),
            new Target("linux-arm64", "linux-arm64"),
            new Target("win-x64", "win-x64"),
        };

        private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["mac"] = "mac-arm64", ["macos"] = "mac-arm64", ["linux"] = "linux-x64", ["win"] = "win-x64", ["windows"] = "win-x64",
            ["windows-x64"] = "win-x64", ["osx-arm64"] = "mac-arm64", ["osx-x64"] = "mac-x64", ["macos-arm64"] = "mac-arm64", ["macos-x64"] = "mac-x64",
        };

        private static readonly string[] SkippedFolders = { ".git", "node_modules", "bin", "obj", "dist", ".vscode", ".idea" };

        public static int Run(string[] args, TextWriter output, TextWriter error) => Run(args, output, error, new CompileEnvironment());

        internal static int Run(string[] args, TextWriter output, TextWriter error, CompileEnvironment env)
        {
            if (args.Length > 0 && args[0] is "--help" or "-h" or "help")
            {
                PrintUsage(output);
                return 0;
            }

            string source = null, name = null, outDir = null, runtime = null, to = "exe", targetText = null;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string Value(string example)
                {
                    if (++i >= args.Length) throw new CompileException($"{a} needs a value, e.g. {a} {example}");
                    return args[i];
                }
                try
                {
                    switch (a)
                    {
                        case "--target": case "-t": targetText = targetText == null ? Value("mac-arm64,win-x64") : targetText + "," + Value("linux-x64"); break;
                        case "--out": case "-o": outDir = Value("dist"); break;
                        case "--name": name = Value("\"Life Lab\""); break;
                        case "--runtime": runtime = Value("path/to/kscript"); break;
                        case "--to": to = Value("exe"); break;
                        default:
                            if (a.StartsWith("-", StringComparison.Ordinal)) throw new CompileException($"Unknown option '{a}'. Run kscript compile --help for the options.");
                            if (source != null) throw new CompileException($"kscript compile builds one script or project at a time, but got '{source}' and '{a}'.");
                            source = a;
                            break;
                    }
                }
                catch (CompileException ex)
                {
                    KScript.Runtime.ErrorReport.Write(error, ex);
                    return 1;
                }
            }

            try
            {
                if (!string.Equals(to, "exe", StringComparison.OrdinalIgnoreCase))
                    throw new CompileException($"kscript compile --to {to} isn't available yet. Programs (--to exe, the default) are; compiling to other languages is on the roadmap (docs/roadmap.md).");
                List<Target> targets = ParseTargets(targetText, env.HostRuntime);
                if (runtime != null && targets.Count != 1) throw new CompileException("--runtime is the kscript for one platform, so it needs exactly one --target.");

                ProgramPlan plan = Plan(source, name, outDir, output, out string sourceFolder);
                outDir = Path.GetFullPath(outDir ?? Path.Combine(sourceFolder, "dist"));
                var cli = new KScript.Output.CliOutput(output);
                cli.Info($"Compiling {cli.Style.Bold(plan.Name)} ({Describe(plan.Kind)}) with {string.Join(", ", plan.Packages)}");
                cli.NewLine();

                var store = new RuntimeStore(env, output);
                foreach (Target target in targets)
                {
                    string kscript = runtime != null ? store.FromFile(runtime, target) : store.Get(target);
                    string built = Write(plan, target, SingleFileBundle.Read(kscript), Path.Combine(outDir, target.Name), env, output);
                    cli.TwoColumn($"{cli.Style.Green(target.Name)} {built}", cli.Style.Gray(SizeOf(built)) + " " + cli.Style.Apply("DONE", "1;32"));
                }
                cli.NewLine();
                cli.Success($"Compiled {plan.Name} for {targets.Count} target{(targets.Count == 1 ? string.Empty : "s")}, in [{outDir}].");
                return 0;
            }
            catch (CompileException ex)
            {
                KScript.Runtime.ErrorReport.Write(error, ex);
                return 1;
            }
            catch (ProjectConfigException ex)
            {
                KScript.Runtime.ErrorReport.Write(error, ex);
                return 1;
            }
            catch (KScriptException ex)
            {
                error.WriteLine("The script has an error, so it wasn't compiled:");
                KScript.Runtime.ErrorReport.Write(error, ex);
                return 1;
            }
            catch (HttpRequestException ex)
            {
                KScript.Runtime.ErrorReport.Write(error, "Couldn't download the kscript runtime.", ex);
                return 1;
            }
        }

        private static void PrintUsage(TextWriter output)
        {
            output.WriteLine("Usage: kscript compile [script or project folder] [--target name[,name]] [--out folder] [--name \"My App\"]");
            output.WriteLine();
            output.WriteLine("Builds a program that runs without kscript: a terminal script, a <KScript.Window> desktop app, or a");
            output.WriteLine("kscript serve site. It holds the script, its imports and files, and only the packages it uses.");
            output.WriteLine();
            output.WriteLine("  --target    " + string.Join(", ", Targets.Select(t => t.Name)) + ", or all (default: this computer).");
            output.WriteLine("              Programs for other computers use that platform's kscript release, downloaded once.");
            output.WriteLine("  --out       Where programs go (default: dist/ next to the script or kscript.json), one folder per target.");
            output.WriteLine("  --name      The program's name (default: the app section of kscript.json, or the script's name).");
            output.WriteLine("  --runtime   A kscript release binary or archive for the target, instead of downloading one.");
            output.WriteLine("  --to        exe (the default). Other languages are planned; see docs/roadmap.md.");
            output.WriteLine();
            output.WriteLine("With no script, kscript.json names it: { \"app\": { \"main\": \"app.ks.xml\", \"name\": \"My App\", \"icon\": \"icon.png\" } }.");
            output.WriteLine("A project without app.main but with pages compiles to a program that serves the site.");
        }

        private static string Describe(ProgramKind kind) => kind switch
        {
            ProgramKind.Window => "desktop app",
            ProgramKind.Site => "web site",
            _ => "terminal program",
        };

        internal static List<Target> ParseTargets(string text, string hostRuntime)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                Target host = Targets.FirstOrDefault(t => t.Runtime == hostRuntime)
                    ?? throw new CompileException("There's no kscript release for this computer, so pick a --target: " + string.Join(", ", Targets.Select(t => t.Name)) + ".");
                return new List<Target> { host };
            }
            var list = new List<Target>();
            foreach (string part in text.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (part.Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    list.AddRange(Targets);
                    continue;
                }
                string canonical = Aliases.TryGetValue(part, out string alias) ? alias : part;
                Target target = Targets.FirstOrDefault(t => t.Name.Equals(canonical, StringComparison.OrdinalIgnoreCase))
                    ?? throw new CompileException($"There's no target '{part}'. Targets: {string.Join(", ", Targets.Select(t => t.Name))}, or all.");
                list.Add(target);
            }
            return list.Distinct().ToList();
        }

        #region What goes in

        /// <summary>Works out what the program is, which packages it needs, and the files it carries.</summary>
        internal static ProgramPlan Plan(string source, string name, string outDir, TextWriter log, out string sourceFolder)
        {
            ProjectConfig project = ProjectConfig.FindAndLoad(source ?? ".");
            if (project != null) foreach (string w in project.Warnings) log.WriteLine($"{project.FilePath}: {w}");

            var plan = new ProgramPlan();
            string script = source != null && File.Exists(source) ? Path.GetFullPath(source) : null;
            if (source != null && script == null && !Directory.Exists(source)) throw new CompileException($"There is no script or folder at '{source}'.");
            if (script == null && project?.App.Main != null) script = Path.GetFullPath(Path.Combine(project.Directory, project.App.Main));
            if (script != null && !File.Exists(script)) throw new CompileException($"app.main: there is no script at '{script}'.");

            string siteRoot = null;
            if (script == null)
            {
                siteRoot = project != null ? project.SiteRoot : Path.GetFullPath(source ?? ".");
                if (!LooksLikeSite(siteRoot))
                    throw new CompileException("Which script? Run kscript compile <script>, or add \"app\": { \"main\": \"app.ks.xml\" } to kscript.json.");
            }

            // Everything in the project (or, without one, the script's or site's folder) goes in, so imports and data work.
            sourceFolder = project?.Directory ?? (script != null ? Path.GetDirectoryName(script) : siteRoot);
            if (script != null && !IsInside(script, sourceFolder)) sourceFolder = Path.GetDirectoryName(script);
            if (siteRoot != null && !IsInside(siteRoot, sourceFolder)) sourceFolder = siteRoot;

            plan.Packages.Add("KScript.Core");
            foreach (string p in project?.Packages ?? new List<string>()) plan.Packages.Add(PackageRegistry.Normalise(p));
            if (script != null)
            {
                var options = new InterpreterOptions();
                options.DefaultPackages.AddRange(project?.Packages ?? new List<string>());
                var interpreter = new Interpreter(new KScriptHost(TextWriter.Null), options: options);
                Module main = interpreter.Load(script);
                foreach (Module m in interpreter.LoadedModules)
                {
                    foreach (KScriptPackage p in m.Packages) plan.Packages.Add(p.Name);
                    if (!IsInside(m.Path, sourceFolder)) throw new CompileException($"{Path.GetFileName(script)} imports {m.Path}, which is outside {sourceFolder}, so it can't go in the program.");
                }
                plan.Kind = main.MainElementName == "Window" ? ProgramKind.Window : ProgramKind.Script;
                plan.Main = Relative(sourceFolder, script);
            }
            else
            {
                plan.Kind = ProgramKind.Site;
                plan.Main = Relative(sourceFolder, siteRoot);
                foreach (string p in new[] { "KScript.Http", "KScript.Web", "KScript.IO", "KScript.Style" }) plan.Packages.Add(p);
            }

            var skipped = new List<string>();
            Collect(sourceFolder, sourceFolder, outDir == null ? null : Path.GetFullPath(outDir), plan, skipped);
            if (skipped.Count > 0) log.WriteLine($"Left out {string.Join(", ", skipped)}: secrets don't belong in a program. Set them as environment variables where it runs.");
            if (plan.Kind == ProgramKind.Site)
            {
                // Pages import packages at their top; any file that names one brings it in.
                var import = new Regex("<import\\s[^>]*package\\s*=\\s*\"([^\"]+)\"");
                foreach (var file in plan.Files.Where(f => !f.Key.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
                    foreach (Match m in import.Matches(Encoding.UTF8.GetString(file.Value)))
                        plan.Packages.Add(PackageRegistry.Normalise(m.Groups[1].Value));
            }
            AddRequired(plan.Packages);

            string defaultName = script == null ? project?.Name ?? Path.GetFileName(siteRoot.TrimEnd(Path.DirectorySeparatorChar))
                : plan.Kind == ProgramKind.Window ? TitleFromFile(script) : StemOf(script);
            plan.Name = SafeName(name ?? project?.App.Name ?? (plan.Kind == ProgramKind.Window ? project?.Name : null) ?? defaultName);
            plan.FileName = plan.Name.Replace(' ', '-');
            plan.Id = project?.App.Id ?? "app.kscript." + new string(plan.Name.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
            plan.Version = project?.App.Version ?? "1.0.0";
            if (project?.App.Icon != null)
            {
                plan.IconPath = Path.GetFullPath(Path.Combine(project.Directory, project.App.Icon));
                if (!File.Exists(plan.IconPath)) throw new CompileException($"app.icon: there is no file at '{plan.IconPath}'.");
            }
            return plan;
        }

        private static bool LooksLikeSite(string folder) =>
            Directory.Exists(folder) && Directory.EnumerateFiles(folder).Select(Path.GetFileName)
                .Any(f => f.StartsWith("index.", StringComparison.OrdinalIgnoreCase) || f.StartsWith("routes.", StringComparison.OrdinalIgnoreCase) || f.StartsWith("api.", StringComparison.OrdinalIgnoreCase));

        /// <summary>Adds the packages that the chosen packages always import.</summary>
        private static void AddRequired(SortedSet<string> packages)
        {
            var queue = new Queue<string>(packages);
            while (queue.Count > 0)
            {
                KScriptPackage package = PackageRegistry.Default.Get(queue.Dequeue());
                if (package == null) continue;
                foreach (string r in package.Requires)
                    if (packages.Add(PackageRegistry.Normalise(r))) queue.Enqueue(r);
            }
        }

        /// <summary>Gathers the files under <paramref name="folder"/>, leaving out secrets, tests, build output and the --out folder.</summary>
        private static void Collect(string root, string folder, string outDir, ProgramPlan plan, List<string> skipped)
        {
            foreach (string file in Directory.EnumerateFiles(folder).OrderBy(f => f, StringComparer.Ordinal))
            {
                string fileName = Path.GetFileName(file);
                if (fileName.Equals(".env", StringComparison.OrdinalIgnoreCase) || fileName.StartsWith(".env.", StringComparison.OrdinalIgnoreCase))
                {
                    skipped.Add(Relative(root, file));
                    continue;
                }
                if (TestSettings.IsTestFileName(file)) continue;
                plan.Files[Relative(root, file)] = File.ReadAllBytes(file);
            }
            foreach (string dir in Directory.EnumerateDirectories(folder))
            {
                string name = Path.GetFileName(dir);
                if (SkippedFolders.Contains(name, StringComparer.OrdinalIgnoreCase) || name.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) continue;
                if (outDir != null && IsInside(dir, outDir)) continue;
                Collect(root, dir, outDir, plan, skipped);
            }
        }

        #endregion

        #region What comes out

        /// <summary>Writes the program for one target and returns its path.</summary>
        internal static string Write(ProgramPlan plan, Target target, SingleFileBundle runtime, string folder, CompileEnvironment env, TextWriter log)
        {
            List<IconImage> icon = plan.IconPath != null ? Icons.Load(plan.IconPath) : null;
            SingleFileBundle bundle = ProgramPackager.Package(runtime, plan, out _);
            bool window = plan.Kind == ProgramKind.Window;
            if (target.Windows) bundle.Host = WindowsExecutable.Customize(bundle.Host, plan.Name, plan.Version, icon, gui: window);
            byte[] program = bundle.ToBytes();
            if (target.Mac) program = MacExecutable.Sign(program, plan.Id);
            Directory.CreateDirectory(folder);

            if (target.Mac && window)
            {
                string app = Path.Combine(folder, plan.Name + ".app");
                if (Directory.Exists(app))
                {
                    if (!File.Exists(Path.Combine(app, "Contents", "Info.plist")))
                        throw new CompileException($"{app} already exists and isn't a Mac app. Move it, or pick another --out folder.");
                    Directory.Delete(app, recursive: true);
                }
                string contents = Path.Combine(app, "Contents");
                Directory.CreateDirectory(Path.Combine(contents, "MacOS"));
                Directory.CreateDirectory(Path.Combine(contents, "Resources"));
                WriteProgram(Path.Combine(contents, "MacOS", plan.FileName), program);
                string iconFile = null;
                if (icon != null)
                {
                    iconFile = "AppIcon.icns";
                    File.WriteAllBytes(Path.Combine(contents, "Resources", iconFile), Icons.ToIcns(icon));
                }
                File.WriteAllText(Path.Combine(contents, "Info.plist"), InfoPlist(plan, iconFile));
                string failed = env.Codesign?.Invoke(app);
                if (failed != null) log.WriteLine($"codesign couldn't seal {plan.Name}.app, so macOS may say it is damaged: {failed}");
                return app;
            }

            string exe = Path.Combine(folder, (target.Windows ? plan.Name : plan.FileName) + (target.Windows ? ".exe" : ""));
            WriteProgram(exe, program);
            if (window && !target.Windows && !target.Mac)
            {
                // Linux: a launcher entry and the icon, for ~/.local/share/applications and ~/.local/share/icons.
                byte[] png = icon != null ? Icons.ToPng(icon) : null;
                if (png != null) File.WriteAllBytes(Path.Combine(folder, plan.FileName + ".png"), png);
                File.WriteAllText(Path.Combine(folder, plan.FileName + ".desktop"),
                    $"[Desktop Entry]\nType=Application\nName={plan.Name}\nExec={plan.FileName}\n" + (png != null ? $"Icon={plan.FileName}\n" : "") + "Terminal=false\nCategories=Utility;\n");
            }
            return exe;
        }

        private static void WriteProgram(string path, byte[] program)
        {
            string staged = path + ".tmp";
            File.WriteAllBytes(staged, program);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(staged, File.GetUnixFileMode(staged) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            File.Move(staged, path, overwrite: true);
        }

        internal static string InfoPlist(ProgramPlan plan, string icon)
        {
            static string X(string s) => SecurityElement.Escape(s);
            var plist = new StringBuilder();
            plist.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
            plist.Append("<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n");
            plist.Append("<plist version=\"1.0\">\n<dict>\n");
            void Key(string k, string v) => plist.Append($"  <key>{k}</key><string>{X(v)}</string>\n");
            Key("CFBundleName", plan.Name);
            Key("CFBundleDisplayName", plan.Name);
            Key("CFBundleIdentifier", plan.Id);
            Key("CFBundleVersion", plan.Version);
            Key("CFBundleShortVersionString", plan.Version);
            Key("CFBundleExecutable", plan.FileName);
            Key("CFBundlePackageType", "APPL");
            Key("LSMinimumSystemVersion", "11.0");
            if (icon != null) Key("CFBundleIconFile", icon);
            plist.Append("  <key>NSHighResolutionCapable</key><true/>\n");
            plist.Append("</dict>\n</plist>\n");
            return plist.ToString();
        }

        private static string SizeOf(string path)
        {
            long bytes = File.Exists(path) ? new FileInfo(path).Length
                : Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
            return $"{bytes / 1024.0 / 1024.0:0.0} MB";
        }

        #endregion

        #region Names and paths

        private static string StemOf(string script)
        {
            string stem = Path.GetFileName(script);
            string ext = new[] { ".app.ks.xml", ".ks.xml", ".xml", ".kscript", ".ks" }.FirstOrDefault(e => stem.EndsWith(e, StringComparison.OrdinalIgnoreCase));
            return ext != null && stem.Length > ext.Length ? stem[..^ext.Length] : stem;
        }

        private static string TitleFromFile(string script)
        {
            var words = StemOf(script).Split(new[] { '-', '_', '.', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(' ', words.Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
        }

        internal static string SafeName(string name)
        {
            string cleaned = new string((name ?? "").Where(c => !Path.GetInvalidFileNameChars().Contains(c) && c != '/' && c != '\\' && c != ':' && c != '*' && c != '?' && c != '"' && c != '<' && c != '>' && c != '|').ToArray()).Trim();
            if (cleaned.Length == 0) throw new CompileException($"'{name}' can't be used as a program name.");
            return cleaned;
        }

        private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

        private static bool IsInside(string path, string folder)
        {
            string p = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string f = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return p.StartsWith(f, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }

    /// <summary>
    /// Finds the kscript runtime for a target: this kscript when it is a single-file build, one given with --runtime, a
    /// cached one, or the release's kscript-runtime-&lt;rid&gt; archive (the single-file build that programs are made from).
    /// </summary>
    internal sealed class RuntimeStore
    {
        private readonly CompileEnvironment env;
        private readonly TextWriter log;

        public RuntimeStore(CompileEnvironment env, TextWriter log)
        {
            this.env = env;
            this.log = log;
        }

        public string FromFile(string path, CompileCommand.Target target)
        {
            path = Path.GetFullPath(path);
            if (!File.Exists(path)) throw new CompileException($"--runtime: there is no file at '{path}'.");
            if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)) return path;
            string extracted = Path.Combine(Path.GetTempPath(), "kscript-runtime-" + Guid.NewGuid().ToString("N"), BinaryName(target));
            Directory.CreateDirectory(Path.GetDirectoryName(extracted));
            using (var archive = File.OpenRead(path)) Extract(archive, path, extracted);
            return extracted;
        }

        public string Get(CompileCommand.Target target)
        {
            if (target.Runtime == env.HostRuntime && env.CurrentBinary != null) return env.CurrentBinary;

            string version = env.Version;
            string cached = Path.Combine(env.RuntimeCache, version, target.Runtime, BinaryName(target));
            if (File.Exists(cached)) return cached;
            if (!SelfUpdater.TryParseVersion(version, out Version parsed) || parsed == new Version(0, 0, 0) || version.Contains("-dev", StringComparison.Ordinal))
                throw new CompileException($"This kscript is a development build ({version}), so there's no release to get the {target.Name} kscript from. Use a release kscript, or pass --runtime with a kscript built for {target.Name}.");

            string asset = SelfUpdater.RuntimeAssetName(target.Runtime);
            string url = $"https://github.com/{env.Repository}/releases/download/v{version}/{asset}";
            new KScript.Output.CliOutput(log).Info($"Downloading kscript {version} for {target.Name} (once; kept in {env.RuntimeCache})");
            using var client = new HttpClient(env.Handler, disposeHandler: false);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("kscript-compile");
            using HttpResponseMessage response = client.GetAsync(url).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
                throw new CompileException($"Couldn't download {url} ({(int)response.StatusCode}). kscript {version} may have no release for {target.Name}.");

            Directory.CreateDirectory(Path.GetDirectoryName(cached));
            string staged = cached + ".download";
            using (Stream archive = response.Content.ReadAsStream()) Extract(archive, asset, staged);
            File.Move(staged, cached, overwrite: true);
            return cached;
        }

        private static string BinaryName(CompileCommand.Target target) => target.Windows ? "kscript.exe" : "kscript";

        private static void Extract(Stream archive, string name, string destination)
        {
            try
            {
                SelfUpdater.ExtractBinary(archive, name, destination);
            }
            catch (InvalidDataException ex)
            {
                throw new CompileException($"{Path.GetFileName(name)}: {ex.Message}");
            }
        }
    }
}
