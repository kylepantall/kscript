using KScript.Conversion;
using KScript.Syntax.Markup;

namespace KScript.Tests;

/// <summary>kscript convert on whole projects: imports, file names and kscript.json follow the converted files.</summary>
public sealed class ConvertProjectTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "kscript-convert-project-" + Guid.NewGuid().ToString("N"));

    public ConvertProjectTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    private (int Code, string Output, string Error) Convert(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        int code = ConvertCommand.Run(args, output, error);
        return (code, output.ToString(), error.ToString());
    }

    private string Write(string name, string text)
    {
        string path = Path.Combine(dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, text);
        return path;
    }

    private string Read(string name) => File.ReadAllText(Path.Combine(dir, name));

    [Fact]
    public void ANewProjectConvertsChecksAndPassesItsTestsInEitherFormat()
    {
        string previous = Environment.GetEnvironmentVariable("KSCRIPT_TEMPLATES");
        Environment.SetEnvironmentVariable("KSCRIPT_TEMPLATES", Path.Combine(dir, "no-user-templates"));
        try
        {
            KScript.Scaffolding.ProjectScaffolder.Create(dir, "todo", new Dictionary<string, string> { ["format"] = "xml" });
        }
        finally
        {
            Environment.SetEnvironmentVariable("KSCRIPT_TEMPLATES", previous);
        }
        string project = Path.Combine(dir, "todo");

        var (code, output, error) = Convert(project, "--to", "kscript");
        Assert.True(code == 0, error);
        foreach (string xml in Directory.EnumerateFiles(project, "*.ks.xml", SearchOption.AllDirectories).ToList())
        {
            File.Delete(xml);
        }

        // Nothing points at the deleted XML files any more: imports, comments, strings, pages and kscript.json.
        foreach (string file in Directory.EnumerateFiles(project, "*.*", SearchOption.AllDirectories).Where(f => !f.EndsWith("kscript.json", StringComparison.Ordinal)))
        {
            Assert.DoesNotContain(".ks.xml", File.ReadAllText(file).Replace("**/*.ks.xml", string.Empty).Replace("*.test.ks.xml", string.Empty));
        }
        string controller = File.ReadAllText(Path.Combine(project, "controllers", "tasks.kscript"));
        Assert.Contains("const file = 'data/tasks.json'", controller);
        Assert.Contains("import \"../lib/tasks.kscript\"", controller);
        Assert.Contains("import \"../lib/greeting.kscript\"", File.ReadAllText(Path.Combine(project, "lib", "greeting.kscript")));
        Assert.Contains("\"main\": \"desktop.kscript\"", File.ReadAllText(Path.Combine(project, "kscript.json")));
        Assert.True(File.Exists(Path.Combine(project, "tests", "tasks.test.kscript")));

        AssertCleanAndPassing(project);

        // And back again.
        (code, _, error) = Convert(project, "--to", "xml");
        Assert.True(code == 0, error);
        foreach (string kscript in Directory.EnumerateFiles(project, "*.kscript", SearchOption.AllDirectories).ToList())
        {
            File.Delete(kscript);
        }
        Assert.Contains("<const name=\"file\" value=\"'data/tasks.json'\" />", File.ReadAllText(Path.Combine(project, "controllers", "tasks.ks.xml")));
        AssertCleanAndPassing(project);
    }

    private static void AssertCleanAndPassing(string project)
    {
        var check = new StringWriter();
        int checkCode = KScript.LanguageServer.CheckCommand.Run(new[] { project }, check);
        Assert.True(checkCode == 0, check.ToString());

        var output = new StringWriter();
        var error = new StringWriter();
        int testCode = KScript.Testing.TestCommand.Run(Array.Empty<string>(), output, error, color: false, workingDirectory: project);
        Assert.True(testCode == 0, output + error.ToString());
        Assert.Contains("passed", output.ToString());
    }

    [Fact]
    public void ImportsFollowFilesConvertedInTheSameRun()
    {
        Write("lib/a.ks.xml", "<kscript>\n  <fn name=\"a\" value=\"1\" />\n</kscript>");
        Write("main.ks.xml", "<kscript>\n  <!-- Uses lib/a.ks.xml: <import src=\"lib/a.ks.xml\" /> -->\n  <import src=\"lib/a.ks.xml\" />\n  <import src=\"other.ks.xml\" />\n  <echo value=\"@run_script('lib/a.ks.xml')\" />\n</kscript>");
        Write("other.ks.xml", "<kscript />");

        // Only main.ks.xml and the library: other.ks.xml stays XML, and the import of it is kept with a note.
        var (code, _, error) = Convert(Path.Combine(dir, "main.ks.xml"), Path.Combine(dir, "lib", "a.ks.xml"));
        Assert.Equal(0, code);
        string main = Read("main.kscript");
        Assert.Contains("// Uses lib/a.kscript: import \"lib/a.kscript\"", main);
        Assert.Contains("import \"lib/a.kscript\"\n", main);
        Assert.Contains("import \"other.ks.xml\"", main);
        Assert.Contains("@run_script('lib/a.kscript')", main);
        Assert.Contains("imports other.ks.xml, which has no .kscript version yet", error);
    }

    [Fact]
    public void ImportsOfScriptsConvertedEarlierFollowThem()
    {
        Write("lib/a.kscript", "fn a() => 1");
        Write("main.ks.xml", "<kscript>\n  <import src=\"lib/a.ks.xml\" />\n</kscript>");
        Assert.Equal(0, Convert(Path.Combine(dir, "main.ks.xml")).Code);
        Assert.Equal("import \"lib/a.kscript\"\n", Read("main.kscript"));

        // The other way: XML to a .kscript importer.
        Write("b.kscript", "import \"lib/a.kscript\"");
        Write("lib/a.ks.xml", "<kscript />");
        Assert.Equal(0, Convert(Path.Combine(dir, "b.kscript")).Code);
        Assert.Contains("<import src=\"lib/a.ks.xml\" />", Read("b.ks.xml"));
    }

    [Fact]
    public void ConstElementsBecomeConstDeclarations()
    {
        var result = ScriptConverter.Convert("<kscript>\n  <const name=\"file\" value=\"'a.json'\" />\n  <const name=\"n\" type=\"int\">3</const>\n</kscript>", "x.ks.xml");
        Assert.Equal("const file = 'a.json'\nconst n: int = 3\n", result.Text);
    }

    [Fact]
    public void TestFilesConvertAndRunAsKscript()
    {
        Write("lib/m.ks.xml", "<kscript>\n  <fn name=\"double\" params=\"x\" value=\"$x * 2\" />\n</kscript>");
        Write("tests/m.test.ks.xml", "<kscript>\n  <import src=\"../lib/m.ks.xml\" />\n  <describe name=\"double\">\n    <test name=\"doubles\">\n      <expect value=\"@double(2)\" equals=\"4\" />\n    </test>\n  </describe>\n</kscript>");
        Assert.Equal(0, Convert(dir).Code);
        string test = Read("tests/m.test.kscript");
        Assert.Contains("describe(name: double) {", test);

        File.Delete(Path.Combine(dir, "tests", "m.test.ks.xml"));
        var output = new StringWriter();
        var error = new StringWriter();
        Assert.Equal(0, KScript.Testing.TestCommand.Run(new[] { dir }, output, error, color: false));
        Assert.Contains("1 passed", output.ToString());

        // The editor's checks know test files get KScript.Test, as kscript test gives it to them.
        var check = new StringWriter();
        Assert.True(KScript.LanguageServer.CheckCommand.Run(new[] { Path.Combine(dir, "tests", "m.test.kscript") }, check) == 0, check.ToString());
    }
}
