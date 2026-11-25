using KScript.Conversion;

namespace KScript.Tests;

/// <summary>kscript convert: options, folders, mistakes and warnings.</summary>
public sealed class ConvertCommandTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("kscript-convert").FullName;

    public void Dispose() => Directory.Delete(dir, true);

    private string Write(string name, string text)
    {
        string path = Path.Combine(dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        var o = new StringWriter { NewLine = "\n" };
        var e = new StringWriter { NewLine = "\n" };
        int code = ConvertCommand.Run(args, o, e);
        return (code, o.ToString(), e.ToString());
    }

    [Fact]
    public void Converted_scripts_are_laid_out_as_fmt_would()
    {
        Write("p/kscript.json", "{}");
        string xml = Write("p/lib/people.ks.xml", "<kscript>\n  <let name=\"people\" value=\"[{ name: 'Joan', age: 82 }, { name: 'Sam', age: 35 }, { name: 'Ada', age: 36 }, { name: 'Grace', age: 45 }]\" />\n  <echo>{ $people->count() }</echo>\n</kscript>\n");
        var (code, _, error) = Run(xml);
        Assert.True(code == 0, error);
        string written = Path.Combine(dir, "p", "lib", "people.kscript");
        var check = new StringWriter();
        Assert.True(KScript.LanguageServer.FormatCommand.Run(new[] { written, "--check" }, check) == 0, File.ReadAllText(written) + check);

        string back = Path.Combine(dir, "p", "lib", "back.ks.xml");
        Assert.Equal(0, Run(written, "--out", back).Code);
        check = new StringWriter();
        Assert.True(KScript.LanguageServer.FormatCommand.Run(new[] { back, "--check" }, check) == 0, File.ReadAllText(back) + check);
    }

    [Fact]
    public void Scripts_left_in_their_own_syntax_import_the_converted_files()
    {
        Write("q/kscript.json", "{}");
        Write("q/models/note.ks.xml", "<kscript>\n  <fn name=\"note\" value=\"'a note'\" />\n</kscript>\n");
        string component = Write("q/lib/uses.kscript", "import \"../models/note.ks.xml\"\n\necho @note()\n");
        var (code, output, error) = Run(Path.Combine(dir, "q"), "--syntax", "kscript");
        Assert.True(code == 0, error);
        Assert.Contains("import \"../models/note.kscript\"", File.ReadAllText(component));
        Assert.Contains("uses.kscript: now names the converted files", output);
    }

    [Fact]
    public void A_file_that_cannot_be_written_is_reported_in_plain_words()
    {
        string script = Write("w/a.kscript", "echo \"hi\"\n");
        var (code, _, error) = Run(script, "--out", Path.Combine(dir, "w", "nowhere", "a.ks.xml"));
        Assert.Equal(1, code);
        Assert.Contains("there is no folder at", error);
        Assert.DoesNotContain("Could not find a part of the path", error);
    }

    [Fact]
    public void Options_are_checked()
    {
        Assert.Contains("Usage: kscript convert", Run("--help").Out);
        Assert.Contains("Usage: kscript convert", Run("-h").Out);
        Assert.Contains("Usage: kscript convert", Run().Err);
        Assert.Contains("--syntax needs kscript or xml", Run("--syntax", "json", "a").Err);
        Assert.Contains("--syntax needs kscript or xml", Run("a", "--syntax").Err);
        Assert.Contains("--out needs a file name", Run("a", "--out").Err);
        Assert.Contains("Unknown option '--nope'", Run("--nope").Err);
        var missing = Run(Path.Combine(dir, "missing.ks.xml"));
        Assert.Equal(2, missing.Code);
        Assert.Contains("There is no file or folder", missing.Err);
    }

    [Fact]
    public void Out_force_and_to_pick_the_files_and_names()
    {
        string xml = Write("a.ks.xml", "<kscript>\n  <echo>hi</echo>\n</kscript>\n");
        string code = Write("b.kscript", "echo \"b\"\n");
        Write("a.test.ks.xml", "<kscript>\n  <echo>t</echo>\n</kscript>\n");

        var tooMany = Run(dir, "--out", Path.Combine(dir, "x.kscript"));
        Assert.Equal(2, tooMany.Code);
        Assert.Contains("--out works with one file", tooMany.Err);

        string named = Path.Combine(dir, "named.kscript");
        Assert.Equal(0, Run(xml, "--out", named).Code);
        Assert.Equal("echo \"hi\"\n", File.ReadAllText(named));
        Assert.Equal(1, Run(xml, "--out", named).Code);
        Assert.Equal(0, Run(xml, "--out", named, "--force").Code);
        Assert.Equal(0, Run(xml, "--out", named, "-f").Code);

        // --syntax kscript converts only the XML scripts in a folder (tests too); --syntax xml only the .kscript files.
        var toCode = Run(dir, "--syntax", "kscript", "--stdout");
        Assert.Equal(0, toCode.Code);
        Assert.Equal("echo \"hi\"\necho \"t\"\n", toCode.Out);
        var toXml = Run(dir, "--syntax", "xml", "--stdout");
        Assert.Contains("<echo>b</echo>", toXml.Out);
        Assert.Contains("<echo>hi</echo>", toXml.Out);

        // Naming a file already in the target format skips it.
        Assert.Contains("is already .kscript; skipped", Run(code, "--syntax", "kscript").Err);
        Assert.Contains("is already XML; skipped", Run(xml, "--syntax", "xml").Err);
    }

    [Fact]
    public void Syntax_errors_stop_a_file_and_warnings_are_printed()
    {
        string broken = Write("broken.kscript", "x = 1\nif $x {\n");
        var r = Run(broken);
        Assert.Equal(1, r.Code);
        Assert.Contains("broken.kscript(", r.Err);
        Assert.Contains("Fix it before converting.", r.Err);

        string warned = Write("w.ks.xml", "<kscript>\n  stray\n  <echo>hi</echo>\n</kscript>\n");
        var w = Run(warned, "--stdout");
        Assert.Equal(0, w.Code);
        Assert.Contains("w.ks.xml: Text outside an element", w.Err);
    }
}
