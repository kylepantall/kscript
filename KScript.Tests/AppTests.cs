using KScript.Apps;
using KScript.UI;

using static KScript.Tests.ScriptRunner;

namespace KScript.Tests;

/// <summary>kscript app build, and Life Lab's desktop version.</summary>
[Collection("KScript.UI")]
public class AppTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "kscript-app-" + Guid.NewGuid().ToString("N"));

    public AppTests()
    {
        Directory.CreateDirectory(Path.Combine(dir, "project", "lib"));
        Directory.CreateDirectory(Path.Combine(dir, "project", "node_modules"));
        Directory.CreateDirectory(Path.Combine(dir, "kscript"));
        File.WriteAllText(Path.Combine(dir, "project", "kscript.json"), """{ "name": "demo", "app": { "main": "app.ks.xml", "name": "Demo App", "id": "com.example.demo", "version": "2.1.0" } }""");
        File.WriteAllText(Path.Combine(dir, "project", "app.ks.xml"), "<KScript.Window><Text>Hi</Text></KScript.Window>");
        File.WriteAllText(Path.Combine(dir, "project", "lib", "util.ks.xml"), "<kscript />");
        File.WriteAllText(Path.Combine(dir, "project", "node_modules", "big.js"), "");
        // A stand-in for a single-file kscript: an MZ header, so the Windows build can look for a PE header.
        File.WriteAllBytes(Path.Combine(dir, "kscript", "kscript"), new byte[] { (byte)'M', (byte)'Z' }.Concat(new byte[126]).ToArray());
    }

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
    }

    private string Build(AppCommand.Platform platform, string script = null, string name = null) =>
        AppCommand.Build(script, name, Path.Combine(dir, "out"), TextWriter.Null,
            new KScriptRuntime(Path.Combine(dir, "kscript", "kscript"), null, singleFile: true), platform);

    [Fact]
    public void MacAppsAreBundlesWithThePlistAndTheProjectInside()
    {
        string app = Build(AppCommand.Platform.MacOS, Path.Combine(dir, "project", "app.ks.xml"));
        Assert.Equal("Demo App.app", Path.GetFileName(app));
        string plist = File.ReadAllText(Path.Combine(app, "Contents", "Info.plist"));
        Assert.Contains("<key>CFBundleIdentifier</key><string>com.example.demo</string>", plist);
        Assert.Contains("<key>CFBundleExecutable</key><string>kscript</string>", plist);
        Assert.Contains("<key>CFBundleShortVersionString</key><string>2.1.0</string>", plist);
        Assert.True(File.Exists(Path.Combine(app, "Contents", "MacOS", "kscript")));
        string payload = Path.Combine(app, "Contents", "Resources", "app");
        Assert.True(File.Exists(Path.Combine(payload, "lib", "util.ks.xml")));
        Assert.False(Directory.Exists(Path.Combine(payload, "node_modules")));

        string manifest = Path.Combine(payload, AppCommand.ManifestName);
        Assert.Equal(Path.Combine(payload, "app.ks.xml"), AppLauncher.MainScript(manifest));
    }

    [Fact]
    public void TheProjectNamesTheScriptAndBuildsReplaceEarlierOnes()
    {
        string first = Build(AppCommand.Platform.Linux, Path.Combine(dir, "project"));
        string second = Build(AppCommand.Platform.Linux, Path.Combine(dir, "project"));
        Assert.Equal(first, second);
        Assert.True(File.Exists(Path.Combine(second, "Demo-App")));
        Assert.Contains("Name=Demo App", File.ReadAllText(Path.Combine(second, "Demo-App.desktop")));
        Assert.True(File.Exists(Path.Combine(second, "app", "app.ks.xml")));
    }

    [Fact]
    public void WindowsAppsGetTheirName()
    {
        string app = Build(AppCommand.Platform.Windows, Path.Combine(dir, "project", "app.ks.xml"), name: "Hello");
        Assert.True(File.Exists(Path.Combine(app, "Hello")) || File.Exists(Path.Combine(app, "Hello.exe")));
        Assert.True(File.Exists(Path.Combine(app, "app", AppCommand.ManifestName)));
    }

    [Fact]
    public void BuildsNeverReplaceAnUnrelatedFolder()
    {
        Directory.CreateDirectory(Path.Combine(dir, "out", "Demo App"));
        File.WriteAllText(Path.Combine(dir, "out", "Demo App", "notes.txt"), "mine");
        var ex = Assert.Throws<AppBuildException>(() => Build(AppCommand.Platform.Linux, Path.Combine(dir, "project", "app.ks.xml")));
        Assert.Contains("isn't an app kscript built", ex.Message);
        Assert.True(File.Exists(Path.Combine(dir, "out", "Demo App", "notes.txt")));
    }

    [Fact]
    public void WithoutAScriptOrAppMainItSaysWhatToDo()
    {
        string bare = Path.Combine(dir, "bare");
        Directory.CreateDirectory(bare);
        File.WriteAllText(Path.Combine(bare, "kscript.json"), "{}");
        var ex = Assert.Throws<AppBuildException>(() => Build(AppCommand.Platform.Linux, bare));
        Assert.Contains("\"app\": { \"main\"", ex.Message);
    }

    private KScriptRuntime SingleFile => new KScriptRuntime(Path.Combine(dir, "kscript", "kscript"), null, singleFile: true);

    private (int Code, string Out, string Err) App(AppCommand.Platform platform, params string[] args)
    {
        var o = new StringWriter { NewLine = "\n" };
        var e = new StringWriter { NewLine = "\n" };
        int code = AppCommand.Run(args, o, e, SingleFile, platform);
        return (code, o.ToString(), e.ToString());
    }

    [Fact]
    public void TheCommandLine()
    {
        Assert.Equal(1, App(AppCommand.Platform.Linux).Code);
        Assert.Equal(0, AppCommand.Run(new[] { "help" }, TextWriter.Null, TextWriter.Null));
        Assert.Contains("Usage: kscript app build", App(AppCommand.Platform.Linux, "--help").Out);
        Assert.Contains("Unknown app command 'run'", App(AppCommand.Platform.Linux, "run").Err);
        Assert.Contains("--name needs a value", App(AppCommand.Platform.Linux, "build", "--name").Err);
        Assert.Contains("--out needs a folder", App(AppCommand.Platform.Linux, "build", "-o").Err);
        Assert.Contains("Unknown option '--fast'", App(AppCommand.Platform.Linux, "build", "--fast").Err);
        Assert.Contains("There is no script at", App(AppCommand.Platform.Linux, "build", Path.Combine(dir, "nope.ks.xml")).Err);

        File.WriteAllText(Path.Combine(dir, "project", "kscript.json"), "{ nope");
        Assert.Equal(1, App(AppCommand.Platform.Linux, "build", Path.Combine(dir, "project")).Code);
        File.WriteAllText(Path.Combine(dir, "project", "kscript.json"), """{ "app": { "main": "app.ks.xml" }, "bogus": 1 }""");

        string project = Path.Combine(dir, "project");
        string outDir = Path.Combine(dir, "out");
        var mac = App(AppCommand.Platform.MacOS, "build", project, "--out", outDir, "--name", "Mac One");
        Assert.True(mac.Code == 0, mac.Err);
        Assert.Contains("unknown setting 'bogus'", mac.Out);
        Assert.Contains("Open it from Finder", mac.Out);
        Assert.Contains("Run " + Path.Combine(outDir, "Win One", "Win One.exe"), App(AppCommand.Platform.Windows, "build", project, "-o", outDir, "--name", "Win One").Out);
        Assert.Contains("add Lin-One.desktop to ~/.local/share/applications", App(AppCommand.Platform.Linux, "build", project, "-o", outDir, "--name", "Lin One").Out);
    }

    [Fact]
    public void ScriptsWithoutAProjectAreNamedAfterTheFile()
    {
        string loose = Path.Combine(dir, "loose");
        Directory.CreateDirectory(loose);
        File.WriteAllText(Path.Combine(loose, "tidy-my_files.app.ks.xml"), "<kscript />");
        string app = Build(AppCommand.Platform.Linux, Path.Combine(loose, "tidy-my_files.app.ks.xml"));
        Assert.Equal("Tidy My Files", Path.GetFileName(app));
        Assert.Throws<AppBuildException>(() => Build(AppCommand.Platform.Linux, Path.Combine(loose, "tidy-my_files.app.ks.xml"), name: "///"));
        File.WriteAllText(Path.Combine(loose, "say hi.ks"), "<kscript />");
        Assert.Equal("Say Hi", Path.GetFileName(Build(AppCommand.Platform.Linux, Path.Combine(loose, "say hi.ks"))));
    }

    [Fact]
    public void IconsGoInEveryKindOfApp()
    {
        File.WriteAllText(Path.Combine(dir, "project", "kscript.json"), """{ "app": { "main": "app.ks.xml", "name": "Iconic", "icon": "icon.png" } }""");
        string script = Path.Combine(dir, "project", "app.ks.xml");
        Assert.Contains("app.icon: there is no file", Assert.Throws<AppBuildException>(() => Build(AppCommand.Platform.Linux, script)).Message);

        File.WriteAllText(Path.Combine(dir, "project", "icon.png"), "png");
        string mac = Build(AppCommand.Platform.MacOS, script);
        Assert.True(File.Exists(Path.Combine(mac, "Contents", "Resources", "AppIcon.png")));
        Assert.Contains("<key>CFBundleIconFile</key><string>AppIcon.png</string>", File.ReadAllText(Path.Combine(mac, "Contents", "Info.plist")));
        string linux = Build(AppCommand.Platform.Linux, script);
        Assert.Contains("Icon=" + Path.Combine(linux, "app-icon.png"), File.ReadAllText(Path.Combine(linux, "Iconic.desktop")));
    }

    [Fact]
    public void DevelopmentBuildsCopyTheWholeFolderAndGetALauncher()
    {
        string build = Path.Combine(dir, "dev");
        Directory.CreateDirectory(Path.Combine(build, "runtimes"));
        File.WriteAllText(Path.Combine(build, "KScript"), "apphost");
        File.WriteAllText(Path.Combine(build, "KScript.dll"), "dll");
        File.WriteAllText(Path.Combine(build, "runtimes", "native.so"), "so");

        KScriptRuntime runtime = KScriptRuntime.Current(build, singleFile: false, processPath: null, windows: false);
        Assert.False(runtime.SingleFile);
        string app = AppCommand.Build(Path.Combine(dir, "project", "app.ks.xml"), null, Path.Combine(dir, "out"), TextWriter.Null, runtime, AppCommand.Platform.Linux);
        Assert.True(File.Exists(Path.Combine(app, "runtimes", "native.so")));
        Assert.Equal("#!/bin/sh\nexec \"$(dirname \"$0\")/KScript\" \"$@\"\n", File.ReadAllText(Path.Combine(app, "Demo-App")));

        Assert.Contains("has no launcher", Assert.Throws<AppBuildException>(() => KScriptRuntime.Current(build, false, null, windows: true)).Message);
        Assert.True(KScriptRuntime.Current(build, singleFile: true, processPath: "/opt/kscript", windows: false).SingleFile);
        Assert.False(KScriptRuntime.Current().SingleFile);                 // the tests run next to a development build
    }

    [Fact]
    public void WindowsExesSwitchToTheGuiSubsystem()
    {
        byte[] pe = new byte[0x80 + 100];
        pe[0] = (byte)'M'; pe[1] = (byte)'Z';
        BitConverter.GetBytes(0x80).CopyTo(pe, 0x3C);
        "PE\0\0"u8.ToArray().CopyTo(pe, 0x80);
        BitConverter.GetBytes((ushort)3).CopyTo(pe, 0x80 + 92);
        string exe = Path.Combine(dir, "a.exe");
        File.WriteAllBytes(exe, pe);
        AppCommand.MarkAsWindowsApp(exe);
        Assert.Equal(2, BitConverter.ToUInt16(File.ReadAllBytes(exe), 0x80 + 92));
    }

    [Fact]
    public void SigningReportsProblems()
    {
        var log = new StringWriter();
        AppCommand.Sign(dir, log, codesign: Path.Combine(dir, "no-codesign"));
        Assert.Contains("codesign isn't available", log.ToString());
        if (OperatingSystem.IsWindows()) return;

        string fake = Path.Combine(dir, "codesign");
        File.WriteAllText(fake, "#!/bin/sh\necho 'not signed' >&2\nexit 1\n");
        File.SetUnixFileMode(fake, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        log = new StringWriter();
        AppCommand.Sign(dir, log, codesign: fake);
        Assert.Contains("codesign failed, so macOS may refuse to open the app: not signed", log.ToString());

        File.WriteAllText(fake, "#!/bin/sh\nexit 0\n");
        log = new StringWriter();
        AppCommand.Sign(dir, log, codesign: fake);
        Assert.Equal("", log.ToString());
    }

    [Fact]
    public void AppsFindTheirManifest()
    {
        string mac = Path.Combine(dir, "Mac.app", "Contents");
        Directory.CreateDirectory(Path.Combine(mac, "MacOS"));
        Directory.CreateDirectory(Path.Combine(mac, "Resources", "app"));
        File.WriteAllText(Path.Combine(mac, "Resources", "app", AppCommand.ManifestName), "{}");
        Assert.Equal(Path.GetFullPath(Path.Combine(mac, "Resources", "app", AppCommand.ManifestName)), AppLauncher.FindManifest(Path.Combine(mac, "MacOS")));

        string linux = Path.Combine(dir, "Linux");
        Directory.CreateDirectory(Path.Combine(linux, "app"));
        File.WriteAllText(Path.Combine(linux, "app", AppCommand.ManifestName), "{}");
        Assert.Equal(Path.Combine(linux, "app", AppCommand.ManifestName), AppLauncher.FindManifest(linux));

        Assert.Null(AppLauncher.FindManifest(dir));
        Assert.Null(AppLauncher.FindManifest());
    }

    [Fact]
    public void MacOSArgumentsAreDropped() =>
        Assert.Equal(new[] { "x" }, AppLauncher.ScriptArguments(new[] { "-psn_0_12345", "x" }));

    [Fact]
    public void LifeLabPlaysAndCellsCanBeDrawn()
    {
        Exception failure = null;
        UIPackage.Host = s =>
        {
            try
            {
                string Stat(string label)
                {
                    List<UINode> texts = s.FindAll("Text");
                    return texts[texts.FindIndex(t => t.Text == label) + 1].Text;
                }
                Assert.Equal(40 * 24, s.FindAll("Box").Count - 1);
                Assert.Equal("0", Stat("GENERATION"));

                s.Click("Play");
                UINode timer = s.FindAll("Timer")[0];
                Assert.True(timer.Flag("when"));
                for (int i = 0; i < 3; i++) s.Fire(s.FindAll("Timer")[0], "on-tick");
                Assert.Equal("3", Stat("GENERATION"));
                s.Click("Pause");
                Assert.False(s.FindAll("Timer")[0].Flag("when"));

                s.Click("Clear");
                Assert.Equal("0 (0%)", Stat("ALIVE"));
                s.Fire(s.FindAll("Box")[1 + 5], "on-click");
                Assert.Equal("1 (0.1%)", Stat("ALIVE"));

                s.SetBound(s.FindAll("Select")[1], "glider");
                Assert.Equal("5 (0.5%)", Stat("ALIVE"));
                Assert.Null(s.Error);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        };
        try
        {
            new KScript.Runtime.Interpreter(KScript.Runtime.KScriptHost.ForText(new StringWriter(), ""), options: new KScript.Runtime.InterpreterOptions { Timeout = TimeSpan.FromSeconds(60) })
                .RunFile(RepoFile("samples", "life-lab", "desktop.ks.xml"));
        }
        finally
        {
            UIPackage.Host = null;
        }
        if (failure != null) throw new Xunit.Sdk.XunitException("Life Lab failed: " + failure);
    }

    [Fact]
    public void ANewProjectsDesktopAppAddsTicksAndClearsTasks()
    {
        string previous = Environment.GetEnvironmentVariable("KSCRIPT_TEMPLATES");
        Environment.SetEnvironmentVariable("KSCRIPT_TEMPLATES", Path.Combine(dir, "no-user-templates"));
        try
        {
            KScript.Scaffolding.ProjectScaffolder.Create(dir, "todo");
        }
        finally
        {
            Environment.SetEnvironmentVariable("KSCRIPT_TEMPLATES", previous);
        }

        Exception failure = null;
        UIPackage.Host = s =>
        {
            try
            {
                Assert.Equal("2 tasks left", s.FindAll("Text")[^1].Text);
                s.SetBound(s.FindAll("TextBox")[0], "Write the docs");
                s.Click("Add");
                Assert.Equal(3, s.FindAll("CheckBox").Count);
                Assert.Equal("Write the docs", s.FindAll("CheckBox")[2].Text?.Trim());
                Assert.Equal("", s.FindAll("TextBox")[0].Prop("value"));

                s.SetBound(s.FindAll("CheckBox")[0], true);
                Assert.Equal("2 tasks left", s.FindAll("Text")[^1].Text);
                Assert.True(s.FindAll("CheckBox")[0].Flag("value"));

                s.Click("Clear done");
                Assert.Equal(2, s.FindAll("CheckBox").Count);
                s.Click("✕");
                Assert.Equal("1 task left", s.FindAll("Text")[^1].Text);
                Assert.Null(s.Error);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        };
        try
        {
            new KScript.Runtime.Interpreter(KScript.Runtime.KScriptHost.ForText(new StringWriter(), ""), options: new KScript.Runtime.InterpreterOptions { Timeout = TimeSpan.FromSeconds(60) })
                .RunFile(Path.Combine(dir, "todo", "desktop.ks.xml"));
        }
        finally
        {
            UIPackage.Host = null;
        }
        if (failure != null) throw new Xunit.Sdk.XunitException("The project's desktop app failed: " + failure);
    }
}
