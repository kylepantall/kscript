using KScript.UI;

using static KScript.Tests.ScriptRunner;

namespace KScript.Tests;

/// <summary>The desktop samples: Life Lab and the desktop app of a new project.</summary>
[Collection("KScript.UI")]
public class DesktopSampleTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "kscript-desktop-" + Guid.NewGuid().ToString("N"));

    public DesktopSampleTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
    }

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
