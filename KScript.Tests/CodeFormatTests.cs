using System.Globalization;

using KScript.Runtime;
using KScript.Syntax;
using KScript.Syntax.Markup;

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
    [InlineData("repos")]
    [InlineData("errors")]
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
            echo $label->count()
            """;
        Assert.Equal("5\n2\n", Run(code));
        Assert.Contains("const", Assert.Throws<KScriptException>(() => Run("const k = 1\nk = 2")).Message);
    }

    /// <summary>XML to .kscript to XML: every version runs the same, and converting again changes nothing.</summary>
    [Theory]
    [InlineData("samples/kscript2/tour.ks.xml")]
    [InlineData("samples/kscript2/oldest.ks.xml")]
    [InlineData("samples/kscript2/imports.ks.xml")]
    [InlineData("samples/kscript2/types/repo.ks.xml")]
    [InlineData("samples/kscript2/errors/out-of-stock.ks.xml")]
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
    public void AnExplicitKscriptBlockIsTheRootAndCanImport()
    {
        string dir = Path.Combine(Path.GetTempPath(), "kscript-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "lib"));
        try
        {
            File.WriteAllText(Path.Combine(dir, "lib", "s.kscript"), "fn shout(x) => $x->upper()");
            string main = Path.Combine(dir, "main.kscript");
            File.WriteAllText(main, "// comment\nkscript {\n  import \"lib/s.kscript\"\n  import KScript.Math\n\n  echo \"{ @shout('hi') }\"\n}\n");
            Assert.Equal("HI\n", RunFile(main));

            MarkupElement root = Assert.Single(CodeParser.Parse(File.ReadAllText(main)).RootElements);
            Assert.Equal("kscript", root.Name);
            Assert.Equal(new[] { "import", "import", "echo" }, root.ChildElements.Select(e => e.Name));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void BlockCommentsWorkOnTheirOwnLinesAtLineEndsAndInsideExpressions()
    {
        const string code = "/*\n  Header\n    indented\n*/\n"
            + "x = 1 /* inline */\n"
            + "echo \"x is $x\" /* trailing */\n"
            + "/* one */ echo \"two\"\n"
            + "list = [\n  1, /* first */\n  2  // second\n]\n"
            + "y = $x + /* mid */ 2\n"
            + "z = $x /* a\n  multi-line trailing */\n"
            + "/**\n * Star style\n * second\n */\n"
            + "if $x > 0 {\n  /* nested */\n  echo \"{ $list->count() } $y $z\"\n}\n";
        Assert.Equal("x is 1\ntwo\n2 3 1\n", Run(code));

        MarkupDocument doc = CodeParser.Parse(code);
        Assert.Empty(doc.Diagnostics);
        List<MarkupComment> comments = doc.DescendantNodes().OfType<MarkupComment>().ToList();
        Assert.Equal(new[] { "Header\n  indented", "inline", "trailing", "one", "a\nmulti-line trailing", "Star style\nsecond", "nested" },
            comments.Select(c => c.Text));
        Assert.All(comments, c => Assert.True(c.IsBlock));

        // Converting keeps every comment: XML has one kind, so they come back as // lines.
        string xml = ScriptConverter.Convert(code, "a.kscript").Text;
        Assert.Contains("<let name=\"x\" value=\"1\" /> <!-- inline -->", xml);
        Assert.Contains("<echo>x is $x</echo> <!-- trailing -->", xml);
        Assert.Contains("$x + /* mid */ 2", xml);
        string back = ScriptConverter.Convert(xml, "a.ks.xml").Text;
        Assert.StartsWith("// Header\n//   indented\nx = 1 // inline\necho \"x is $x\" // trailing\n// one\n", back);
        Assert.Contains("// Star style\n// second\n", back);
        Assert.Equal("x is 1\ntwo\n2 3 1\n", Run(back));
    }

    [Fact]
    public void MissingPackageErrorsUseTheFilesFormat()
    {
        var element = Assert.Throws<KScriptException>(() => Run("Row(gap: 8) {\n}"));
        Assert.Equal("Row is part of KScript.UI. Add import KScript.UI at the top of the script.", element.Message);
        var command = Assert.Throws<KScriptException>(() => Run("echo @sqrt(4)"));
        Assert.Contains("Add import KScript.Math at the top", command.Message);
        Assert.DoesNotContain("<import", command.Message);
    }

    [Fact]
    public void AnUnclosedBlockCommentIsAnError()
    {
        Assert.Contains(CodeParser.Parse("x = 1\n/* never closed\necho \"x\"").Diagnostics, d => d.Message.Contains("missing its closing */"));
    }

    [Fact]
    public void ImportsInsideABlockSayWhereTheyGo()
    {
        var error = Assert.Throws<KScriptException>(() => Run("if true {\n  import KScript.Math\n}"));
        Assert.Equal("import goes at the top level of the file, not inside a block, before anything that uses it.", error.Message);
        Assert.Equal(2, error.Line);
        Assert.DoesNotContain("<", error.Message);
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

    [Fact]
    public void OneLineFieldsBecomeABlockWhenTheySplitCleanly()
    {
        static string Code(string type) => KScript.Syntax.Markup.ScriptConverter.Convert("<kscript>\n  " + type + "\n</kscript>\n", "x.ks.xml").Text;

        Assert.Contains("type Owner {\n  login: string\n  avatar_url?: string\n  tags: string[] = []\n  pair: list = [1, 2]\n  m: map = { a: 'x, y' }\n  n: int = @max(1, 2)\n}\n",
            Code("<type name=\"Owner\" fields=\"login: string, avatar_url?: string, tags: string[] = [], pair: list = [1, 2], m: map = { a: 'x, y' }, n: int = @max(1, 2)\" />"));
        // Lines from fields come before the <field> elements, and other options stay.
        Assert.Contains("type R(extra: keep) {\n  a: string\n  b: int from x\n}\n",
            Code("<type name=\"R\" fields=\"a: string\" extra=\"keep\"><field name=\"b\" type=\"int\" from=\"x\" /></type>"));
        // Anything that isn't a plain list of fields is left as it is.
        foreach (string odd in new[] { "a: string,, b: int", "a b: string", "a: string = 'x", "a: string = [1" })
        {
            Assert.StartsWith("type Odd(fields: ", Code($"<type name=\"Odd\" fields=\"{odd}\" />").Trim());
        }
        Assert.Equal(new[] { "a: string = 'it\\'s, ok'", "b: int" }, KScript.Syntax.Markup.ScriptConverter.FieldsAsLines("a: string = 'it\\'s, ok', b: int"));
        Assert.Null(KScript.Syntax.Markup.ScriptConverter.FieldsAsLines(" "));
    }

    [Fact]
    public void TypesAndEnumsConvertToTheShortForm()
    {
        const string xml = "<kscript>\n"
            + "  <enum name=\"Size\" values=\"s, m\" description=\"Sizes.\" />\n"
            + "  <type name=\"Repo\" extra=\"error\">\n"
            + "    <!-- fields -->\n"
            + "    <field name=\"stars\" type=\"int\" from=\"stargazers_count\" description=\"Stars.\" />\n"
            + "    <field name=\"owner\" from=\"owner name\" optional=\"yes\" />\n"
            + "    <field name=\"label\" value=\"$stars + 1\" />\n"
            + "  </type>\n"
            + "  <type name=\"Point\" fields=\"x: int, y: int\" />\n"
            + "  <type name=\"Odd\"><field name=\"a\" default=\"1\" value=\"2\" /></type>\n"
            + "  <type name=\"Odd2\"><field name=\"a\" optional=\"maybe\" /></type>\n"
            + "  <type name=\"Odd3\"><field name=\"a\" type=\"int | string\" /></type>\n"
            + "  <type name=\"Odd4\"><field name=\"a\" other=\"x\" /></type>\n"
            + "  <type name=\"Odd5\"><field name=\"a\">text</field><echo>no</echo></type>\n"
            + "  <type name=\"Odd6\"><echo>no</echo></type>\n"
            + "  <enum name=\"E\" values=\"a\" other=\"x\" />\n"
            + "  <enum name=\"E2\" />\n"
            + "  <enum name=\"E3\" values=\"a}\" />\n"
            + "  <enum name=\"E.x\" values=\"a\" />\n"
            + "  <enum name=\"3x\" values=\"a\" />\n"
            + "  <enum name=\"E4\" values=\"a\"><echo>no</echo></enum>\n"
            + "  <type name=\"Odd7\"><field type=\"int\" /></type>\n"
            + "  <type name=\"Odd8\"><field name=\"a.b\" /></type>\n"
            + "  <type name=\"Odd9\">text</type>\n"
            + "  <type name=\"Odd10\"><field name=\"a\" type=\"\" /></type>\n"
            + "  <type name=\"T.x\"><field name=\"a\" /></type>\n"
            + "  <type name=\"Odd11\"><field name=\"a\"><echo>x</echo></field></type>\n"
            + "  <type name=\"Paths\">\n"
            + "    <field name=\"a?\" optional=\"yes\" from=\"\" />\n"
            + "    <field name=\"b\" optional=\"no\" from=\"x;y\" />\n"
            + "    <field name=\"c\" from=\"x}\" />\n"
            + "    <field name=\"d\" from=\"x=y\" />\n"
            + "    <field name=\"e\" from=\"say &quot;hi&quot;\" />\n"
            + "    <field name=\"f\"> </field>\n"
            + "  </type>\n"
            + "</kscript>\n";
        string code = KScript.Syntax.Markup.ScriptConverter.Convert(xml, "x.ks.xml").Text;
        Assert.Contains("/// Sizes.\nenum Size { s, m }\n", code);
        Assert.Contains("type Repo(extra: error) {\n  // fields\n  /// Stars.\n  stars: int from stargazers_count\n  owner? from \"owner name\"\n  label => $stars + 1\n}\n", code);
        // A one-line fields attribute becomes one line per field.
        Assert.Contains("type Point {\n  x: int\n  y: int\n}\n", code);
        Assert.Contains("enum E2 { }\n", code);
        Assert.Contains("type Paths {\n  a? from \"\"\n  b from \"x;y\"\n  c from \"x}\"\n  d from \"x=y\"\n  e from \"say \\\"hi\\\"\"\n  f\n}\n", code);
        foreach (string odd in new[] { "Odd", "Odd2", "Odd3", "Odd4", "Odd5", "Odd6", "Odd7", "Odd8", "Odd9", "Odd10", "Odd11", "T.x", "E3", "E.x", "3x", "E4" })
        {
            Assert.Contains("(name: " + odd, code);
        }
        Assert.Contains("enum(name: E", code);
    }

    [Fact]
    public void TypesWorkInCodeFiles()
    {
        Assert.Equal("kscript has 12 stars, size m\n", Run(
            "enum Size { s, m }\n"
            + "type Repo {\n  name: string\n  stars: int from stargazers_count\n  size: Size = 'm'\n}\n"
            + "r = '{\"name\": \"kscript\", \"stargazers_count\": 12}'->json(as: Repo)\n"
            + "echo \"$r.name has $r.stars stars, size $r.size\""));
    }

    [Fact]
    public void DoubleQuotesFillInValuesAndSingleQuotesDoNot()
    {
        Assert.Equal(
            "Hello Joan, you have 4 and 2! Sam a+b {x} /users/{id} \\d{3} $5 a@b.com\nHello $name {$n}\nHi dear Joan!\nJOAN.\n",
            Run("name = 'Joan'\nn = 2\np = { name: 'Sam', tags: ['a', 'b'] }\n"
                + "g = \"Hello $name, you have { $n * 2 } and {$n}! $p.name { $p.tags->join('+') } {{x}} /users/{id} \\d{3} $5 a@b.com\"\n"
                + "echo $g\n"
                + "s = 'Hello $name {$n}'\necho $s\n"
                + "fn greet(who) {\n  return \"Hi $who!\"\n}\n"
                + "echo @greet(\"dear $name\")\n"
                + "echo \"{ $name->upper() }. \"->trim()\n").Replace("\r\n", "\n"));
    }

    [Fact]
    public void FilledInTextIsWorkedOutEachTimeItRuns()
    {
        Assert.Equal(" 1 2 3\n", Run("out = ''\nfor i in 1..4 {\n  out = \"$out $i\"\n}\necho $out\n").Replace("\r\n", "\n"));
    }
}
