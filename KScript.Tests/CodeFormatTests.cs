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
            total = 0
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
    public void AssignmentDeclaresTheFirstTimeAndChangesAfter()
    {
        const string code = """
            count = 0
            fn bump() {
              count += 1
              local = 'only here'
            }
            @bump()
            @bump()
            for i in 0..3 {
              count = $count + $i
            }
            echo $count
            label: string = 42
            echo $label->length()
            """;
        Assert.Equal("5\n2\n", Run(code));
        Assert.Contains("const", Assert.Throws<KScriptException>(() => Run("const k = 1\nk = 2")).Message);
    }

    /// <summary>XML to .kscript to XML: every version runs the same, and converting again changes nothing.</summary>
    [Theory]
    [InlineData("samples/kscript2/tour.ks.xml")]
    [InlineData("samples/kscript2/oldest.ks.xml")]
    [InlineData("samples/kscript2/imports.ks.xml")]
    [InlineData("samples/life-lab/lib/life.ks.xml")]
    [InlineData("samples/life-lab/life.ks.xml")]
    [InlineData("samples/life-lab/desktop.ks.xml")]
    [InlineData("Example Scripts/Find Oldest Person.xml")]
    public void ConvertRoundTrips(string file)
    {
        string original = ScriptRunner.RepoFile(file.Split('/'));
        string dir = Path.GetDirectoryName(original);
        string xml = File.ReadAllText(original);

        string code = KScript.Syntax.Markup.ScriptConverter.Convert(xml, original).Text;
        string back = KScript.Syntax.Markup.ScriptConverter.Convert(code, "x.kscript").Text;
        string again = KScript.Syntax.Markup.ScriptConverter.Convert(back, original).Text;
        Assert.Equal(code, again);

        if (file.Contains("desktop"))
        {
            return; // a window: checked by shape in the language tests
        }
        string codePath = Path.Combine(dir, "_rt_" + Guid.NewGuid().ToString("N") + ".kscript");
        string xmlPath = Path.Combine(dir, "_rt_" + Guid.NewGuid().ToString("N") + ".ks.xml");
        try
        {
            File.WriteAllText(codePath, code);
            File.WriteAllText(xmlPath, back);
            string[] args = file.EndsWith("life.ks.xml") && !file.Contains("lib") ? new[] { "glider", "3" } : Array.Empty<string>();
            string expected = RunWithArgs(original, args);
            Assert.Equal(expected, RunWithArgs(codePath, args));
            Assert.Equal(expected, RunWithArgs(xmlPath, args));
        }
        finally
        {
            File.Delete(codePath);
            File.Delete(xmlPath);
        }
    }

    private static string RunWithArgs(string path, string[] args)
    {
        var output = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        var host = KScriptHost.ForText(output, "");
        host.Arguments = args;
        new Interpreter(host, options: new InterpreterOptions { Timeout = TimeSpan.FromSeconds(20) }).RunFile(path);
        return output.ToString().Replace("\r\n", "\n");
    }

    [Fact]
    public void ConvertCommandWritesTheOtherFormat()
    {
        string dir = Path.Combine(Path.GetTempPath(), "kscript-convert-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.ks.xml"), "<kscript>\n  <let name=\"x\" value=\"1\" />\n  <echo>$x</echo>\n</kscript>");
            var output = new StringWriter();
            var error = new StringWriter();
            Assert.Equal(0, KScript.Conversion.ConvertCommand.Run(new[] { dir }, output, error));
            Assert.Equal("x = 1\necho \"$x\"\n", File.ReadAllText(Path.Combine(dir, "a.kscript")));

            Assert.Equal(1, KScript.Conversion.ConvertCommand.Run(new[] { Path.Combine(dir, "a.ks.xml") }, output, error));
            Assert.Contains("already exists", error.ToString());

            File.Delete(Path.Combine(dir, "a.ks.xml"));
            Assert.Equal(0, KScript.Conversion.ConvertCommand.Run(new[] { Path.Combine(dir, "a.kscript") }, output, error));
            Assert.Contains("<let name=\"x\" value=\"1\" />", File.ReadAllText(Path.Combine(dir, "a.ks.xml")));

            var stdout = new StringWriter();
            Assert.Equal(0, KScript.Conversion.ConvertCommand.Run(new[] { Path.Combine(dir, "a.kscript"), "--stdout" }, stdout, error));
            Assert.StartsWith("<kscript>", stdout.ToString());
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ErrorsPointAtTheLineAndColumn()
    {
        var error = Assert.Throws<KScriptException>(() => Run("a = 1\n\nb = $a +* 2"));
        Assert.Equal(3, error.Line);
        Assert.Equal(9, error.Column);

        var thrown = Assert.Throws<KScriptException>(() => Run("try {\n  throw Validation \"bad {1 + 1}\"\n} catch err when $err.type == 'Other' {\n  echo \"no\"\n}"));
        Assert.Equal("Validation", thrown.Type);
        Assert.Equal("bad 2", thrown.Message);
    }
}
