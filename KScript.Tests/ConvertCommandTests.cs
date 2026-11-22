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
