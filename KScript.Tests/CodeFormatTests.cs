using System.Globalization;

using KScript.Runtime;

namespace KScript.Tests;

/// <summary>.kscript files run on the same interpreter as XML scripts and must behave the same.</summary>
public class CodeFormatTests
{
    private static string RunFile(string path, string input = "")
    {
        var output = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        new Interpreter(KScriptHost.ForText(output, input), options: new InterpreterOptions { Timeout = TimeSpan.FromSeconds(20) }).RunFile(path);
        return output.ToString().Replace("\r\n", "\n");
    }

    private static string Run(string code) => ScriptRunner.Run(code, path: Path.Combine(Path.GetTempPath(), "test.kscript"));

    [Theory]
    [InlineData("tour")]
    [InlineData("oldest")]
    public void SamplesPrintTheSameAsTheirXmlVersions(string name)
    {
        string xml = RunFile(ScriptRunner.RepoFile("samples", "kscript2", name + ".ks.xml"));
        Assert.Equal(xml, RunFile(ScriptRunner.RepoFile("samples", "code-format", name + ".kscript")));
    }

    [Fact]
    public void ImportsWorkAcrossFormats()
    {
        Assert.Equal("hello-world\nhello-world\n", RunFile(ScriptRunner.RepoFile("samples", "code-format", "imports.kscript")));

        string dir = Path.Combine(Path.GetTempPath(), "kscript-code-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "lib.kscript"), "fn shout(s) => $s->upper() + '!'\nconst greeting = 'hi'");
            File.WriteAllText(Path.Combine(dir, "main.ks.xml"), "<kscript><import src=\"lib.kscript\" /><echo>{ @shout($greeting) }</echo></kscript>");
            Assert.Equal("HI!\n", RunFile(Path.Combine(dir, "main.ks.xml")));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void MoveFilesSampleTakesInput()
    {
        string root = Path.Combine(Path.GetTempPath(), "kscript-code-move-" + Guid.NewGuid().ToString("N"));
        string from = Path.Combine(root, "from"), to = Path.Combine(root, "to");
        Directory.CreateDirectory(from);
        Directory.CreateDirectory(to);
        try
        {
            File.WriteAllText(Path.Combine(from, "a.txt"), "a");
            File.WriteAllText(Path.Combine(from, "c.log"), "c");
            string output = RunFile(ScriptRunner.RepoFile("samples", "code-format", "move-files.kscript"), $"{from}\n{to}\ntxt\n");
            Assert.Contains("Moved", output);
            Assert.True(File.Exists(Path.Combine(to, "a.txt")));
            Assert.True(File.Exists(Path.Combine(from, "c.log")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ControlFlow()
    {
        const string code = """
            fn classify(n) {
              if $n < 0 {
                return 'negative'
              } elif $n == 0 {
                return 'zero'
              } else {
                return 'positive'
              }
            }
            for n in [-2, 0, 5] { echo "$n is { @classify($n) }" }
            let total = 0
            for i in 0..5 {
              if $i == 3 { continue }
              $total += $i
            }
            echo $total
            for x in [] { echo "never" } else { echo "empty" }
            match 'b' {
              case 'a' { echo "A" }
              case 'b', 'c' { echo "B or C" }
              default { echo "?" }
            }
            """;
        Assert.Equal("-2 is negative\n0 is zero\n5 is positive\n7\nempty\nB or C\n", Run(code));
    }

    [Fact]
    public void ErrorsPointAtTheLineAndColumn()
    {
        var error = Assert.Throws<KScriptException>(() => Run("let a = 1\n\nlet b = $a +* 2"));
        Assert.Equal(3, error.Line);
        Assert.Equal(13, error.Column);

        var thrown = Assert.Throws<KScriptException>(() => Run("try {\n  throw Validation \"bad {1 + 1}\"\n} catch err when $err.type == 'Other' {\n  echo \"no\"\n}"));
        Assert.Equal("Validation", thrown.Type);
        Assert.Equal("bad 2", thrown.Message);
    }
}
