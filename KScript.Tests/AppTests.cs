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
