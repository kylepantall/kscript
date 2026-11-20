using KScript.Conversion;
using KScript.Docs;
using KScript.Projects;
using KScript.Scaffolding;

namespace KScript.Tests;

/// <summary>
/// The edge cases of kscript convert, make, kscript.json and kscript docs that the v0.4.0 pull requests left
/// without a test.
/// </summary>
public sealed class MergedFeatureCoverageTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "kscript-merged-" + Guid.NewGuid().ToString("N"));

    public MergedFeatureCoverageTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    private string Write(string name, string text)
    {
        string path = Path.Combine(dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, text);
        return path;
    }

    private static (int Code, string Out, string Err) Make(string root, params string[] args)
    {
        var o = new StringWriter { NewLine = "\n" };
        var e = new StringWriter { NewLine = "\n" };
        int code = MakeCommand.Run(args.Concat(new[] { "--dir", root }).ToArray(), o, e);
        return (code, o.ToString(), e.ToString());
    }

    [Fact]
    public void Convert_keeps_imports_it_has_no_converted_version_for()
    {
        string main = Write("main.ks.xml", "<kscript>\n  <import src=\"https://example.com/lib.ks.xml\" />\n  <import src=\"lib/b.kscript\" />\n</kscript>");
        Write("lib/b.kscript", "fn b() => 1");
        var output = new StringWriter();
        var error = new StringWriter();
        Assert.Equal(0, ConvertCommand.Run(new[] { main }, output, error));
        string code = File.ReadAllText(Path.Combine(dir, "main.kscript"));
        Assert.Contains("import \"https://example.com/lib.ks.xml\"", code);
        Assert.Contains("import \"lib/b.kscript\"", code);
    }

    [Fact]
    public void Make_templates_copies_one_project_template_at_a_time()
    {
        var (code, _, err) = Make(dir, "templates", "project", "web", "bare");
        Assert.Equal(1, code);
        Assert.Contains("Copy one project template at a time", err);
    }

    [Fact]
    public void Make_test_without_a_name_shows_an_example_name()
    {
        var (code, _, err) = Make(dir, "test");
        Assert.Equal(1, code);
        Assert.Contains("kscript make test checkout.", err);
    }

    [Fact]
    public void Unknown_make_settings_are_warnings()
    {
        var config = ProjectConfig.Parse("""{ "make": { "nope": 1 } }""", Path.Combine(dir, ProjectConfig.FileName));
        Assert.Contains("unknown setting 'make.nope' (expected format).", config.Warnings);
    }

    [Fact]
    public void Docs_reports_a_temp_folder_it_cant_write_to()
    {
        string notAFolder = Write("temp-is-a-file", "x");
        string[] names = { "TMPDIR", "TMP", "TEMP" };
        var previous = names.ToDictionary(n => n, Environment.GetEnvironmentVariable);
        var output = new StringWriter();
        var error = new StringWriter();
        int code;
        try
        {
            foreach (string name in names) Environment.SetEnvironmentVariable(name, notAFolder);
            code = DocsCommand.Run(new[] { "--no-open", "--port", TestServer.FreePort().ToString() }, output, error);
        }
        finally
        {
            foreach (var kv in previous) Environment.SetEnvironmentVariable(kv.Key, kv.Value);
        }
        Assert.Equal(1, code);
        Assert.Contains("Could not start the docs server", error.ToString());
    }

    [Fact]
    public void Docs_folders_that_cant_be_deleted_are_left_alone()
    {
        string old = Path.Combine(Path.GetTempPath(), "kscript-docs-stuck-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(old);
        Directory.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-3));
        Action<string> original = DocsCommand.DeleteFolder;
        try
        {
            DocsCommand.DeleteFolder = _ => throw new UnauthorizedAccessException("locked");
            DocsCommand.CleanUpOldSites();
            Assert.True(Directory.Exists(old));
        }
        finally
        {
            DocsCommand.DeleteFolder = original;
            Directory.Delete(old);
        }
    }

    [Fact]
    public void Playground_output_keeps_only_the_first_characters()
    {
        var writer = new Playground.LimitedWriter(2);
        Assert.Equal(System.Text.Encoding.UTF8, writer.Encoding);
        writer.Write('a');
        writer.Write('b');
        Assert.False(writer.Truncated);
        writer.Write('c');
        Assert.True(writer.Truncated);
        Assert.Equal("ab", writer.ToString());
    }

    [Fact]
    public void Playground_stops_a_run_that_is_cancelled_between_statements()
    {
        string code = string.Concat(Enumerable.Repeat("<echo />\n", 3000));
        Func<TimeSpan, CancellationTokenSource> original = Playground.StartTimer;
        PlaygroundResult result;
        try
        {
            // Already cancelled, so the run stops at its first cancellation check, which is between statements.
            Playground.StartTimer = _ =>
            {
                var cancelled = new CancellationTokenSource();
                cancelled.Cancel();
                return cancelled;
            };
            result = Playground.Run(code, timeout: TimeSpan.FromSeconds(30));
        }
        finally
        {
            Playground.StartTimer = original;
        }
        Assert.Equal(3, result.ExitCode);
        Assert.StartsWith("Stopped: the example ran for longer than", result.Error);
    }
}
