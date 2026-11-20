using System.Globalization;

using KScript.IO;
using KScript.Runtime;

namespace KScript.Tests;

public sealed class IOTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "kscript-io-" + Guid.NewGuid().ToString("N"));

    public IOTests() => Directory.CreateDirectory(dir);

    public void Dispose() => Directory.Delete(dir, recursive: true);

    private string Run(string body, Action<KScriptHost> setup = null)
    {
        var output = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        var host = KScriptHost.ForText(output);
        setup?.Invoke(host);
        new Interpreter(host).RunText("<kscript><import package=\"KScript.IO\" />" + body + "</kscript>", Path.Combine(dir, "io.ks.xml"));
        return output.ToString();
    }

    private KScriptException Fails(string body, Action<KScriptHost> setup = null) => Assert.Throws<KScriptException>(() => Run(body, setup));

    private string P(params string[] parts) => Path.Combine(new[] { dir }.Concat(parts).ToArray());

    [Fact]
    public void File_values_answer_questions_about_themselves()
    {
        File.WriteAllText(P("notes.txt"), "one\ntwo");
        string output = Run(
            "<let name=\"f\" value=\"@file('notes.txt')\" />" +
            "<echo>{ $f.name } { $f.extension } { $f.nothing ?? 'n' } { $f.path == $f->path() } { $f->name() } { $f->stem() } { $f->extension() }</echo>" +
            "<echo>{ $f->exists() } { $f->is_file() } { $f->is_folder() } { $f->size() } { $f->read_text()->length() } { $f->read_lines()->count() }</echo>" +
            "<echo>{ $f->modified() != null } { $f->created() != null } { $f->directory()->is_folder() } { $f->to_string() == $f.path } { $f == @file('notes.txt') }</echo>" +
            "<echo>{ $f->directory()->join('a', 'b.txt')->relative_to($f->directory()) } { @file('none.txt')->size() } { @file('none.txt')->exists() }</echo>");
        string sep = Path.DirectorySeparatorChar.ToString();
        Assert.Equal($"notes.txt txt n true notes.txt notes txt\ntrue true false 7 7 2\ntrue true true true true\na{sep}b.txt 0 false\n", output);
        Assert.Contains("nope", Fails("<do>@file('notes.txt')->nope()</do>").Message);

        var a = new FileValue(P("x"));
        Assert.Equal(a, new FileValue(P("x")));
        Assert.Equal(a.GetHashCode(), new FileValue(P("x")).GetHashCode());
        Assert.NotEqual(a, (object)"x");
        Assert.Equal(a.Path, a.ToString());
        Assert.Equal(a.Path, a.ToText());
    }

    [Fact]
    public void Copy_and_move_handle_files_and_folders()
    {
        Directory.CreateDirectory(P("src", "inner"));
        File.WriteAllText(P("src", "a.txt"), "a");
        File.WriteAllText(P("src", "inner", "b.txt"), "b");
        Directory.CreateDirectory(P("into"));

        Run("<copy from=\"'src'\" to=\"'copy'\" /><copy from=\"'src'\" to=\"'into'\" /><copy from=\"'src/a.txt'\" to=\"'src/a.txt'\" />");
        Assert.Equal("b", File.ReadAllText(P("copy", "inner", "b.txt")));
        Assert.Equal("a", File.ReadAllText(P("into", "src", "a.txt")));

        File.WriteAllText(P("copy", "a.txt"), "changed");
        Assert.Contains("already exists", Fails("<copy from=\"'src/a.txt'\" to=\"'single.txt'\" /><copy from=\"'src/a.txt'\" to=\"'single.txt'\" />").Message);
        Run("<copy from=\"'copy'\" to=\"'copy2'\" overwrite=\"yes\" /><move from=\"'single.txt'\" to=\"'renamed.txt'\" />");
        Assert.True(File.Exists(P("renamed.txt")));
        Assert.Equal("changed", File.ReadAllText(P("copy2", "a.txt")));

        Run("<move from=\"'copy'\" to=\"'moved'\" /><move from=\"'copy2'\" to=\"'moved2'\" />");
        Assert.False(Directory.Exists(P("copy")));
        Directory.CreateDirectory(P("target"));
        File.WriteAllText(P("target", "old.txt"), "old");
        Run("<move from=\"'moved'\" to=\"'target'\" /><mkdir path=\"'fresh/deep'\" />");
        Assert.Equal("changed", File.ReadAllText(P("target", "moved", "a.txt")));
        Assert.True(Directory.Exists(P("fresh", "deep")));

        Run("<move from=\"'moved2'\" to=\"'fresh'\" overwrite=\"yes\" />");
        Assert.True(File.Exists(P("fresh", "moved2", "inner", "b.txt")));
        Directory.CreateDirectory(P("dest", "moved2"));
        File.WriteAllText(P("dest", "moved2", "stale.txt"), "stale");
        Run("<move from=\"'fresh/moved2'\" to=\"'dest'\" overwrite=\"yes\" />");
        Assert.False(File.Exists(P("dest", "moved2", "stale.txt")));
        Assert.True(File.Exists(P("dest", "moved2", "a.txt")));
        Assert.Contains("There is no file or folder", Fails("<copy from=\"'missing'\" to=\"'x'\" />").Message);
    }

    [Fact]
    public void Paths_join_from_text_and_file_values()
    {
        string sep = Path.DirectorySeparatorChar.ToString();
        Assert.Equal($"a{sep}b{sep}c.txt\n", Run("<echo>{ @join_path('a', 'b', 'c.txt') }</echo>"));
        Assert.Equal($"{P("x")}\n", Run($"<echo>{{ @join_path(@file('.'), 'x') }}</echo>"));
        Assert.Contains("needs at least one part", Fails("<echo>{ @join_path() }</echo>").Message);
    }

    [Fact]
    public void Known_folders_have_paths()
    {
        string output = Run(
            "<echo>{ ['home', 'desktop', 'documents', 'downloads', 'music', 'pictures', 'videos', 'temp']->map(fn($n) => @known_folder($n)->length() > 0)->join(' ') }</echo>" +
            "<echo>{ @known_folder(' Home ') == @home() } { @known_folder('appdata') != null } { @temp_folder() == @known_folder('temp') }</echo>");
        Assert.Equal("true true true true true true true true\ntrue true true\n", output);
        Assert.Contains("doesn't know 'attic'", Fails("<echo>{ @known_folder('attic') }</echo>").Message);
    }
}
