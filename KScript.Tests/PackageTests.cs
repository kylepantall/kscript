using KScript.Runtime;

using static KScript.Tests.ScriptRunner;

namespace KScript.Tests;

/// <summary>Packages: imports, and the Terminal, Math, IO, Markdown and Net packages.</summary>
public class PackageTests
{
    #region Imports

    [Fact]
    public void PackageElementsNeedAnImport()
    {
        KScriptException ex = Fails("<input to=\"x\">?</input>");
        Assert.Equal("Import", ex.Type);
        Assert.Contains("<import package=\"KScript.Terminal\" />", ex.Message);
    }

    [Fact]
    public void PackageCommandsNeedAnImport()
    {
        KScriptException ex = Fails("<echo>{ @sqrt(4) }</echo>");
        Assert.Contains("KScript.Math", ex.Message);
    }

    [Fact]
    public void ImportsWithAndWithoutPrefix()
    {
        Assert.Equal("2\n", RunBody("<echo>{ @sqrt(4) }</echo>", "", "KScript.Math"));
        Assert.Equal("3 3\n", Run("<kscript><import package=\"Math\" as=\"math\" /><echo>{ @math.sqrt(9) } { math.sqrt(9) }</echo></kscript>"));
    }

    [Fact]
    public void APrefixedImportKeepsCommandsUnderThePrefix()
    {
        Assert.Throws<KScriptException>(() => Run("<kscript><import package=\"KScript.Math\" as=\"m\" /><echo>{ @sqrt(9) }</echo></kscript>"));
    }

    [Fact]
    public void UnknownPackagesSuggestTheClosest()
    {
        KScriptException ex = Assert.Throws<KScriptException>(() => Run("<kscript><import package=\"KScript.Termnal\" /></kscript>"));
        Assert.Contains("KScript.Terminal", ex.Message);
    }

    [Fact]
    public void ImportsGoAtTheTop()
    {
        Assert.Throws<KScriptException>(() => Run("<kscript><if test=\"true\"><import package=\"KScript.Math\" /></if></kscript>"));
    }

    [Fact]
    public void DefaultPackagesComeFromOptions()
    {
        var options = new InterpreterOptions();
        options.DefaultPackages.Add("KScript.Math");
        Assert.Equal("2\n", Run("<kscript><echo>{ @sqrt(4) }</echo></kscript>", options: options));
    }

    [Fact]
    public void ScriptImports()
    {
        string dir = Path.Combine(Path.GetTempPath(), "kscript-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "lib.ks.xml"), "<kscript><fn name=\"double\" params=\"n\" value=\"$n * 2\" /><let name=\"limit\" value=\"10\" const=\"yes\" /></kscript>");
            string main = Path.Combine(dir, "main.ks.xml");
            Assert.Equal("8 10 6\n", Run("<kscript><import src=\"lib.ks.xml\" as=\"lib\" /><import src=\"lib.ks.xml\" names=\"double\" /><echo>{ lib.double(4) } { lib.limit } { @double(3) }</echo></kscript>", path: main));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void PackageRegistryLoadsLazily()
    {
        var registry = PackageRegistry.Default;
        Assert.True(registry.IsKnown("Terminal"));
        Assert.Equal("KScript.Terminal", PackageRegistry.Normalise("terminal".Replace("t", "T", StringComparison.Ordinal)[..1] + "erminal"));
        Assert.NotNull(registry.Get("KScript.Terminal"));
        Assert.Null(registry.Get("KScript.Nothing"));
    }

    #endregion

    #region Terminal

    [Fact]
    public void InputConvertsAndRetries()
    {
        Assert.Equal("Age: \nPlease enter a whole number.\nAge: \n43\n", RunBody("<input to=\"age\" type=\"int\">Age: </input><echo>{ $age + 1 }</echo>", "abc\n42\n", "KScript.Terminal"));
    }

    [Fact]
    public void InputDefault()
    {
        Assert.EndsWith("Kyle\n", RunBody("<input to=\"name\" default=\"'Kyle'\">Name? </input><echo>$name</echo>", "\n", "KScript.Terminal"));
    }

    [Fact]
    public void InputWithNoMoreInputIsAnError()
    {
        Assert.Throws<KScriptException>(() => RunBody("<input to=\"name\">Name? </input>", "", "KScript.Terminal"));
    }

    [Fact]
    public void MenuPicksByNumberOrLabel()
    {
        Assert.EndsWith("Exit 1\n", RunBody("<menu to=\"choice\" index=\"i\" options=\"['Settings', 'Exit']\">Next?</menu><echo>$choice $i</echo>", "2\n", "KScript.Terminal"));
        Assert.EndsWith("Settings\n", RunBody("<menu to=\"choice\" options=\"['Settings', 'Exit']\" /><echo>$choice</echo>", "settings\n", "KScript.Terminal"));
    }

    [Fact]
    public void ConfirmAndPause()
    {
        Assert.EndsWith("true\n", RunBody("<confirm to=\"ok\">Sure?</confirm><echo>$ok</echo>", "y\n", "KScript.Terminal"));
        Assert.EndsWith("false\n", RunBody("<confirm to=\"ok\" default=\"no\">Sure?</confirm><echo>$ok</echo>", "\n", "KScript.Terminal"));
        Assert.StartsWith("Press enter", RunBody("<pause>Press enter</pause>", "\n", "KScript.Terminal"));
    }

    [Fact]
    public void ColoursArePlainWhenNotATerminal()
    {
        Assert.Equal("ok\n", RunBody("<echo>{ @color('ok', colour: 'green') }</echo>", "", "KScript.Terminal"));
        Assert.Equal("false\n", RunBody("<echo>{ @is_terminal() }</echo>", "", "KScript.Terminal"));
    }

    [Fact]
    public void MarkdownEchoFormat()
    {
        string output = RunBody("<echo format=\"markdown\">\n  # Title\n  Some **bold** text\n  - one\n  - two\n</echo>", "", "KScript.Terminal");
        Assert.Equal("Title\n\nSome bold text\n\n• one\n• two\n", output);
    }

    [Fact]
    public void MarkdownEchoNeedsTerminal()
    {
        KScriptException ex = Fails("<echo format=\"markdown\">x</echo>");
        Assert.Contains("KScript.Terminal", ex.Message);
    }

    #endregion

    #region Math

    [Theory]
    [InlineData("@sqrt(16)", "4")]
    [InlineData("@pow(2, exponent: 8)", "256")]
    [InlineData("@round(2.345, digits: 2)", "2.35")]
    [InlineData("@clamp(15, low: 0, high: 10)", "10")]
    [InlineData("@median(3, 1, 2, 10)", "2.5")]
    [InlineData("@average([2, 4])", "3")]
    [InlineData("@gcd(a: 12, b: 18)", "6")]
    [InlineData("@factorial(5)", "120")]
    [InlineData("$pi->round(digits: 4)", "3.1416")]
    [InlineData("[1, 2, 3]->median()", "2")]
    public void MathCommands(string expression, string expected)
    {
        Assert.Equal(expected, Eval(expression, "KScript.Math"));
    }

    [Fact]
    public void RandomIsRepeatableWithASeed()
    {
        string a = RunBody("<do>@random_seed(7)</do><echo>{ @random(low: 1, high: 100) } { @random(low: 1, high: 100) }</echo>", "", "KScript.Math");
        string b = RunBody("<do>@random_seed(7)</do><echo>{ @random(low: 1, high: 100) } { @random(low: 1, high: 100) }</echo>", "", "KScript.Math");
        Assert.Equal(a, b);
    }

    #endregion

    #region IO

    [Fact]
    public void FilesCanBeWrittenListedReadMovedAndDeleted()
    {
        string dir = Path.Combine(Path.GetTempPath(), "kscript-io-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string script = """
                <kscript>
                  <import package="KScript.IO" />
                  <write to="'out/a.txt'">hello</write>
                  <write to="'out/a.txt'" append="yes">again</write>
                  <do>@write_json('out/data.json', value: { n: 1 })</do>
                  <echo>{ @read_lines('out/a.txt')->count() } { @read_json('out/data.json').n }</echo>
                  <for each="f" in="@files('out', pattern: '*.txt')">
                    <echo>{ $f->name() } { $f->extension() } { $f->size() gt 0 }</echo>
                  </for>
                  <mkdir path="'moved'" />
                  <move from="'out/a.txt'" to="'moved'" />
                  <copy from="'moved/a.txt'" to="'moved/b.txt'" />
                  <echo>{ @exists('out/a.txt') } { @is_file('moved/a.txt') } { @is_file('moved/b.txt') }</echo>
                  <delete path="'moved'" />
                  <echo>{ @is_folder('moved') }</echo>
                </kscript>
                """;
            Assert.Equal("2 1\na.txt txt true\nfalse true true\nfalse\n", Run(script, path: Path.Combine(dir, "io.ks.xml")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void MissingFilesAreFileNotFound()
    {
        Assert.Equal("FileNotFound\n", RunBody("<try><echo>{ @read_text('/no/such/file.txt') }</echo><catch as=\"e\"><echo>$e.type</echo></catch></try>", "", "KScript.IO"));
    }

    [Fact]
    public void MoveDoesntOverwriteUnlessAsked()
    {
        string dir = Path.Combine(Path.GetTempPath(), "kscript-mv-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.txt"), "a");
            File.WriteAllText(Path.Combine(dir, "b.txt"), "b");
            string path = Path.Combine(dir, "s.ks.xml");
            Assert.Throws<KScriptException>(() => Run("<kscript><import package=\"KScript.IO\" /><move from=\"'a.txt'\" to=\"'b.txt'\" /></kscript>", path: path));
            Run("<kscript><import package=\"KScript.IO\" /><move from=\"'a.txt'\" to=\"'b.txt'\" overwrite=\"yes\" /></kscript>", path: path);
            Assert.Equal("a", File.ReadAllText(Path.Combine(dir, "b.txt")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    #endregion

    #region Markdown and Net

    [Fact]
    public void MarkdownToHtml()
    {
        Assert.Equal("Hi|<h1 id=\"hi\">Hi</h1>", Eval("@markdown('# Hi').title + '|' + @markdown('# Hi').html->trim()", "KScript.Markdown"));
        Assert.Equal("<p><em>x</em></p>", Eval("'*x*'->markdown()->trim()", "KScript.Markdown"));
    }

    [Fact]
    public void FrontMatter()
    {
        Assert.Equal("Post 3", Eval("@markdown('---\ntitle: Post\nn: 3\n---\nBody').title + ' ' + @markdown('---\ntitle: Post\nn: 3\n---\nBody').meta.n", "KScript.Markdown"));
    }

    [Fact]
    public void NetHelpers()
    {
        Assert.Equal("a=1&b=x%20y", Eval("@query_string({ a: 1, b: 'x y' })", "KScript.Net"));
        KScriptException ex = Fails("<echo>{ @api('ftp://example.com') }</echo>", "KScript.Net");
        Assert.Contains("http", ex.Message);
    }

    #endregion
}
