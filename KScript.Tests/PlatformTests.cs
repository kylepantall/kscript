using System.Globalization;

namespace KScript.Tests;

public class PlatformTests
{
    [Fact]
    public void JoinPath_UsesPlatformSeparator()
    {
        string expected = Path.Combine("dir", "sub", "file.txt");
        Assert.Equal(expected + "\n", ScriptRunner.RunBody("<echo>@join_path('dir', 'sub', 'file.txt')</echo>"));
    }

    [Fact]
    public void K_OsPlatform_IsKnown()
    {
        string platform = ScriptRunner.RunBody("<echo>@k('os.platform')</echo>").Trim();
        Assert.Contains(platform, new[] { "windows", "linux", "macos", "freebsd" });
    }

    [Fact]
    public void K_OsName_IsNotEmpty()
    {
        // Used WMI (Windows only) and returned an empty string elsewhere.
        Assert.False(string.IsNullOrWhiteSpace(ScriptRunner.RunBody("<echo>@k('os.name')</echo>")));
    }

    [Fact]
    public void K_PathSeparator()
    {
        Assert.Equal(Path.DirectorySeparatorChar + "\n", ScriptRunner.RunBody("<echo>@k('path.separator')</echo>"));
    }

    [Theory]
    [InlineData("filename", "b.txt")]
    [InlineData("extension", ".txt")]
    [InlineData("name", "b")]
    public void Path_Parts(string type, string expected)
    {
        string path = Path.Combine("a", "b.txt");
        Assert.Equal(expected + "\n", ScriptRunner.RunBody($"<echo>@path('{path}','{type}')</echo>"));
    }

    [Fact]
    public void ReadText_ReadsLocalFile()
    {
        // Every path used to be treated as a URL, so local files could not be read.
        string file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "file contents");
            Assert.Equal("file contents\n", ScriptRunner.RunBody($"<echo>@read_text('{file}')</echo>"));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void GetFiles_FiltersByExtension()
    {
        string dir = Directory.CreateTempSubdirectory("kscript").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.txt"), "");
            File.WriteAllText(Path.Combine(dir, "b.cs"), "");
            File.WriteAllText(Path.Combine(dir, "c.md"), "");

            string output = ScriptRunner.RunBody($"<echo>@getfiles('{dir}', ';', '$value', 'txt, .cs')</echo>").Trim();

            Assert.Equal(new[] { Path.Combine(dir, "a.txt"), Path.Combine(dir, "b.cs") }, output.Split(';'));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Registry_OnNonWindows_ReportsUnsupported()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var ex = Assert.Throws<KScriptExceptions.KScriptException>(() =>
            ScriptRunner.RunBody("<echo>@registry('HKEY_CURRENT_USER\\Software', 'x')</echo>"));
        Assert.Contains("only supported on Windows", ex.Message);
    }

    [Fact]
    public void ConsoleCommands_DoNotThrowWhenRedirected()
    {
        // Console.Clear/ReadKey throw when stdin/stdout are not a terminal (pipes, CI).
        Assert.Equal("done\n", ScriptRunner.RunBody("<clear/><anykey/><echo>done</echo>", input: "x"));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("en-US")]
    public void Numbers_AreCultureInvariant(string culture)
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal("3.5\n", ScriptRunner.RunBody("<echo>@math('7/2')</echo>"));
            Assert.Equal("yes\n", ScriptRunner.RunBody("<echo>@more_less('1.5','lt','2.25')</echo>"));
            Assert.Equal("2.5\n", ScriptRunner.RunBody("<echo>@average('2','3')</echo>"));
            Assert.Equal("2.5\n", ScriptRunner.RunBody("<def id=\"n\">1.5</def><echo>$n->increment()</echo>"));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
