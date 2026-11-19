using System.Globalization;

using KScript.Runtime;

namespace KScript.Tests;

/// <summary>Runs the scripts shipped in the repository so they keep working.</summary>
public class ScriptFileTests
{
    private static string RunFile(string path, string input = "")
    {
        var output = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        new Interpreter(KScriptHost.ForText(output, input)).RunFile(path);
        return output.ToString().Replace("\r\n", "\n");
    }

    [Fact]
    public void FindOldestPerson()
    {
        Assert.Equal("Joan is the oldest at 82 years old.\n", RunFile(ScriptRunner.RepoFile("Example Scripts", "Find Oldest Person.xml")));
    }

    [Fact]
    public void FileMoveScript()
    {
        string root = Path.Combine(Path.GetTempPath(), "kscript-move-" + Guid.NewGuid().ToString("N"));
        string from = Path.Combine(root, "from"), to = Path.Combine(root, "to");
        Directory.CreateDirectory(from);
        Directory.CreateDirectory(to);
        try
        {
            File.WriteAllText(Path.Combine(from, "a.txt"), "a");
            File.WriteAllText(Path.Combine(from, "b.pdf"), "b");
            File.WriteAllText(Path.Combine(from, "c.log"), "c");
            string output = RunFile(ScriptRunner.RepoFile("Example Scripts", "File Move Script.xml"), $"{from}\n{to}\ntxt, .pdf\n");
            Assert.Contains("Moved a.txt", output);
            Assert.Contains("Moved b.pdf", output);
            Assert.True(File.Exists(Path.Combine(to, "a.txt")));
            Assert.True(File.Exists(Path.Combine(to, "b.pdf")));
            Assert.True(File.Exists(Path.Combine(from, "c.log")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("oldest.ks.xml", "Joan is the oldest at 82 years old.")]
    [InlineData("imports.ks.xml", "hello-world")]
    public void Samples(string file, string expected)
    {
        Assert.Contains(expected, RunFile(ScriptRunner.RepoFile("samples", "kscript2", file)));
    }

    [Fact]
    public void Tour()
    {
        string output = RunFile(ScriptRunner.RepoFile("samples", "kscript2", "tour.ks.xml"));
        Assert.Contains("Hello, Kyle! Total: 10.", output);
        Assert.Contains("Validation: Age must be positive, got -1", output);
        Assert.EndsWith("guest true\n", output);
    }
}
