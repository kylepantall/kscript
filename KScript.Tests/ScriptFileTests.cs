namespace KScript.Tests;

/// <summary>
/// Runs the scripts shipped in the repository so they keep working.
/// </summary>
public class ScriptFileTests
{
    private static string RepoFile(params string[] parts)
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "KScript.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        return Path.Combine(new[] { dir }.Concat(parts).ToArray());
    }

    private static string RunFile(string path)
    {
        TextWriter originalOut = Console.Out;
        var output = new StringWriter { NewLine = "\n" };
        try
        {
            Console.SetOut(output);
            var parser = new KScriptParser(path);
            parser.Properties.ForceQuiet = true;
            parser.Parse();
        }
        finally
        {
            Console.SetOut(originalOut);
        }
        return output.ToString().Replace("\r\n", "\n");
    }

    [Fact]
    public void FindOldestPerson()
    {
        Assert.Equal("Joan is the oldest at 82 years old.\n", RunFile(RepoFile("Example Scripts", "Find Oldest Person.xml")));
    }

}
