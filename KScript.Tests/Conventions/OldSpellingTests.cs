using KScript.Language.Analysis;
using KScript.Projects;
using KScript.Runtime;
using KScript.Syntax;
using KScript.Syntax.Markup;

namespace KScript.Tests.Conventions;

/// <summary>
/// A clean break: a .kscript file written with an old spelling doesn't run. Each one is refused when the file is
/// loaded, is an error in <c>kscript check</c> with the same message, and is rewritten by <c>kscript check --fix</c>.
/// XML keeps its own forms.
/// </summary>
public class OldSpellingTests
{
    /// <summary>The old spelling, part of the message that says what to write, the file after --fix, and what that prints (null: not run).</summary>
    public static readonly TheoryData<string, string, string, string> Spellings = new()
    {
        // else if, = between a name and its value, a comma in a catch list
        { "a = 2\nif $a == 1 {\n  echo \"1\"\n} else if $a == 2 {\n  echo \"2\"\n}\n", "Write elif", "a = 2\nif $a == 1 {\n  echo \"1\"\n} elif $a == 2 {\n  echo \"2\"\n}\n", "2\n" },
        { "import KScript.Terminal\ndetail(name = \"A\") \"1\"\n", "Write name:", "import KScript.Terminal\ndetail(name: \"A\") \"1\"\n", null },
        { "try {\n  echo \"1\"\n} catch NotFound, Conflict as e {\n  echo $e.message\n}\n", "NotFound | Conflict", "try {\n  echo \"1\"\n} catch NotFound | Conflict as e {\n  echo $e.message\n}\n", "1\n" },
        // A list written as one string
        { "import KScript.Http\nRoutes {\n  Middleware(use: \"log, cors\")\n}\n", "use: [log, cors]", "import KScript.Http\nRoutes {\n  Middleware(use: [log, cors])\n}\n", null },
        { "import KScript.Document\nDocument(title: \"T\", to: \"out/a.md, out/b.tex\") {\n  Paragraph \"p\"\n}\n", "to: ['out/a.md', 'out/b.tex']", "import KScript.Document\nDocument(title: \"T\", to: ['out/a.md', 'out/b.tex']) {\n  Paragraph \"p\"\n}\n", null },
        { "type Point(fields: \"x: int, y: int\")\necho Point(x: 1, y: 2).x\n", "fields: [x: int, y: int]", "type Point(fields: [x: int, y: int])\necho Point(x: 1, y: 2).x\n", "1\n" },
        // Names are declared bare
        { "x = 1\n$x += 1\necho $x\n", "Assign to x, without $", "x = 1\nx += 1\necho $x\n", "2\n" },
        { "f = fn($a) => $a * 2\necho $f(2)\n", "Declare the parameter as a", "f = fn(a) => $a * 2\necho $f(2)\n", "4\n" },
        { "echo \"{ [1, 2]->map(fn($a) => $a * 2) }\"\n", "Declare the parameter as a", "echo \"{ [1, 2]->map(fn(a) => $a * 2) }\"\n", "2, 4\n" },
        // Comparisons are symbols
        { "n = 1\nif $n lt 10 {\n  echo \"x\"\n}\n", "Write <", "n = 1\nif $n < 10 {\n  echo \"x\"\n}\n", "x\n" },
        { "n = 1\necho \"{ $n ge 10 ? 'a' : 'b' }\"\n", "Write >=", "n = 1\necho \"{ $n >= 10 ? 'a' : 'b' }\"\n", "b\n" },
        // true and false
        { "import KScript.Terminal\ntable(rows: [{ a: 1 }], header: no)\n", "Write false", "import KScript.Terminal\ntable(rows: [{ a: 1 }], header: false)\n", null },
        // Types are named bare, and an optional field is marked on its name
        { "type Repo {\n  name: string\n}\nr = $Repo(name: 'x')\necho $r.name\n", "Write Repo", "type Repo {\n  name: string\n}\nr = Repo(name: 'x')\necho $r.name\n", "x\n" },
        { "type Repo {\n  name: string\n  age: int?\n}\necho Repo(name: 'x').name\n", "age?: int", "type Repo {\n  name: string\n  age?: int\n}\necho Repo(name: 'x').name\n", "x\n" },
        // A model's options go in its brackets
        { "import KScript.Data\nmodel User {\n  timestamps: true\n  name: string\n}\necho \"ok\"\n", "Write a model's options in its brackets", "import KScript.Data\nmodel User(timestamps) {\n  name: string\n}\necho \"ok\"\n", "ok\n" },
        // Text is quoted, and a function is @name
        { "import KScript.Terminal\ndetail(name: People) \"2\"\n", "name: \"People\"", "import KScript.Terminal\ndetail(name: \"People\") \"2\"\n", null },
        { "fn job() {\n  echo \"x\"\n}\nspawn(fn: job)\n", "Write @job", "fn job() {\n  echo \"x\"\n}\nspawn(fn: @job)\n", null },
    };

    private static string TestPath => Path.Combine(Path.GetTempPath(), "old-spelling.kscript");

    [Theory]
    [MemberData(nameof(Spellings))]
    public void AnOldSpellingIsRefusedAtRunTimeAndByCheckAndFixed(string old, string says, string expected, string prints)
    {
        // The engine refuses it, saying what to write.
        KScriptException refused = Assert.Throws<KScriptException>(() => ScriptRunner.Run(old, path: TestPath));
        Assert.Contains(says, refused.Message);

        // kscript check reports the same thing as an error, with its fix.
        List<Diagnostic> found = Analyzer.Analyze(old, "test.kscript").Diagnostics.Where(d => OldSpellings.IsOldSpelling(d.Code)).ToList();
        Assert.NotEmpty(found);
        Assert.All(found, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
        Assert.Contains(found, d => d.Message == refused.Message);

        // No setting makes it run: the standards can't turn it down to a warning.
        ProjectConfig off = ProjectConfig.Parse("{ \"lint\": { \"rules\": { \"KS6*\": \"off\" } } }", Path.Combine(Path.GetTempPath(), "kscript.json"));
        int count = found.Count;
        List<Diagnostic> kept = LintRules.Apply(found.ToList(), off, null);
        Assert.Equal(count, kept.Count);
        Assert.All(kept, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));

        // kscript check --fix rewrites it, and the result is clean and runs.
        string now = Fixer.Apply(old, "test.kscript").Text;
        Assert.Equal(expected, now);
        Assert.DoesNotContain(Analyzer.Analyze(now, "test.kscript").Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        if (prints != null)
        {
            Assert.Equal(prints, ScriptRunner.Run(now, path: TestPath));
        }
    }

    [Fact]
    public void CodeInDoubleQuotesIsAnErrorThatTheFixerPutsInBraces()
    {
        const string old = "import KScript.UI\nWindow(title: \"x\") {\n  count = 0\n  Text \"{$count}\"\n  Button(on_click: \"count += 1\") \"Go\"\n}\n";
        Diagnostic found = Assert.Single(Analyzer.Analyze(old, "test.kscript").Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal(DiagnosticCodes.InvalidAttributeValue, found.Code);
        string now = Fixer.Apply(old, "test.kscript").Text;
        Assert.Contains("Button(on_click: { count += 1 }) \"Go\"", now);
        Assert.DoesNotContain(Analyzer.Analyze(now, "test.kscript").Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void XmlKeepsItsOwnForms()
    {
        // Comparison words, comma lists in attributes and yes/no are how XML writes them.
        const string xml = "<kscript>\n  <let name=\"n\" value=\"1\" />\n  <if test=\"$n lt 10\">\n    <echo>small</echo>\n  </if>\n  <type name=\"Point\" fields=\"x: int, y: int\" />\n  <echo value=\"Point(x: 1, y: 2).y\" />\n</kscript>\n";
        Assert.Equal("small\n2\n", ScriptRunner.Run(xml));
        Assert.DoesNotContain(Analyzer.Analyze(xml, "test.ks.xml").Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void StylePreferencesStayWarnings()
    {
        // else after a return (KS6001) and an untyped parameter (KS6007) are how a file reads, not whether it runs.
        const string code = "fn pick(n) {\n  if $n > 1 {\n    return \"a\"\n  } else {\n    return \"b\"\n  }\n}\necho @pick(2)\n";
        Assert.Equal("a\n", ScriptRunner.Run(code, path: TestPath));
        Assert.DoesNotContain(Analyzer.Analyze(code, "test.kscript").Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        foreach (string style in new[] { DiagnosticCodes.PreferEarlyExit, DiagnosticCodes.MissingParameterType, DiagnosticCodes.OneTypePerFile, DiagnosticCodes.NamingStyle, DiagnosticCodes.DurationUnit })
        {
            Assert.False(OldSpellings.IsOldSpelling(style));
        }
    }

    [Fact]
    public void ATypeWithoutFieldsIsToldTheSameThingByTheCheckerAndTheEngine()
    {
        KScriptException code = Assert.Throws<KScriptException>(() => ScriptRunner.Run("type T\n", path: TestPath));
        Assert.Contains("fields: [name: string, age?: int]", code.Message);
        Assert.Contains("path: \"sample.json\"", code.Message);
        Assert.Contains(Analyzer.Analyze("type T\n", "test.kscript").Diagnostics, d => d.Message == code.Message);

        KScriptException xml = Assert.Throws<KScriptException>(() => ScriptRunner.Run("<kscript><type name=\"T\" /></kscript>"));
        Assert.Contains("fields=\"name: string, age?: int\"", xml.Message);
        Assert.Contains(Analyzer.Analyze("<kscript><type name=\"T\" /></kscript>", "test.ks.xml").Diagnostics, d => d.Message == xml.Message);
    }

    [Fact]
    public void ARenamedArgumentOfLogProviderSaysItsNewName()
    {
        const string code = "log_provider(type: \"console\", colour: true)\n";
        Diagnostic found = Assert.Single(Analyzer.Analyze(code, "test.kscript").Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal(DiagnosticCodes.RetiredAttribute, found.Code);
        Assert.Contains("'colour' is now color", found.Message);
        Assert.Contains("'colour' is now color", Assert.Throws<KScriptException>(() => ScriptRunner.Run(code, path: TestPath)).Message);
        Assert.Contains("color: true", Fixer.Apply(code, "test.kscript").Text);
    }

    [Fact]
    public void WaitTakesItsOneArgumentWithoutTheName()
    {
        const string code = "wait(1ms)\nwait(duration: 1ms)\nn = 2ms\nwait($n)\necho \"done\"\n";
        Assert.Empty(Analyzer.Analyze(code, "test.kscript").Diagnostics);
        Assert.Equal("done\n", ScriptRunner.Run(code, path: TestPath));
        // Both spellings are the same statement.
        MarkupDocument document = CodeParser.Parse("wait(500ms)\nwait(duration: 500ms)\n");
        Assert.Equal(new[] { "500ms", "500ms" }, document.RootElements.Single().Children.OfType<MarkupElement>().Select(e => e.GetAttributeValue("duration")));
        Assert.Contains(Analyzer.Analyze("wait(1ms, 2)\n", "test.kscript").Diagnostics, d => d.Message.Contains("wait takes one value"));
    }
}
