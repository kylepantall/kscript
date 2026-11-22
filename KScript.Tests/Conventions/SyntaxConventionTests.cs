using System.Text.RegularExpressions;

using KScript.Language.Analysis;
using KScript.Language.Formatting;
using KScript.Runtime;
using KScript.Syntax;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Tests.Conventions;

/// <summary>
/// One spelling for each piece of syntax: names are declared bare (<c>x = 3</c>, <c>fn(n) =&gt; $n</c>), a match has one
/// arm syntax (<c>pattern =&gt; result</c>), a .kscript file compares with symbols and joins with words, and
/// <c>elif</c>, <c>key: value</c> and <c>catch A | B</c> have no second form. In a .kscript file a second spelling is an
/// error (KS6010, KS6012) that carries its fix, and the file doesn't run; every sample, template and docs example is
/// checked to be free of them, so they can't come back.
/// </summary>
public class SyntaxConventionTests
{
    /// <summary>Every old spelling (the codes a .kscript file doesn't run with), so no sample, template or example holds one.</summary>
    private static readonly string[] Guarded =
    {
        DiagnosticCodes.BareDeclaration, DiagnosticCodes.OneSpelling, DiagnosticCodes.PreferArrayList,
        DiagnosticCodes.QuoteText, DiagnosticCodes.BoolWords, DiagnosticCodes.TypeForm,
    };

    [Fact]
    public void TheGuardCoversEveryOldSpelling()
    {
        foreach (string code in typeof(DiagnosticCodes).GetFields().Select(f => f.GetValue(null)).OfType<string>().Where(OldSpellings.IsOldSpelling))
        {
            Assert.Contains(code, Guarded);
        }
    }

    private static List<Diagnostic> Lint(string code, string path = "test.kscript") =>
        Analyzer.Analyze(code, path).Diagnostics.Where(d => Guarded.Contains(d.Code)).OrderBy(d => d.Span.Start).ToList();

    private static string Fix(string code, string path = "test.kscript") => Fixer.Apply(code, path).Text;

    private static string Run(string code) => ScriptRunner.Run(code, path: Path.Combine(Path.GetTempPath(), "test.kscript"));

    #region The guard: samples, templates and docs

    public static IEnumerable<object[]> ScriptFiles()
    {
        string root = ScriptRunner.RepoFile();
        foreach (string folder in new[] { "samples", Path.Combine("KScript.Cli", "Scaffolding"), "Example Sites" })
        {
            string full = Path.Combine(root, folder);
            if (!Directory.Exists(full))
            {
                continue;
            }
            foreach (string file in Directory.EnumerateFiles(full, "*.kscript", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            {
                yield return new object[] { Path.GetRelativePath(root, file).Replace('\\', '/') };
            }
        }
    }

    public static IEnumerable<object[]> DocFiles()
    {
        string root = ScriptRunner.RepoFile();
        yield return new object[] { "README.md" };
        foreach (string file in Directory.EnumerateFiles(Path.Combine(root, "docs"), "*.md").OrderBy(f => f, StringComparer.Ordinal))
        {
            yield return new object[] { "docs/" + Path.GetFileName(file) };
        }
    }

    [Fact]
    public void TheGuardSeesTheSamplesAndTheDocs()
    {
        Assert.True(ScriptFiles().Count() > 40, "expected the .kscript samples and templates to be found");
        Assert.True(DocFiles().Sum(f => Fences(File.ReadAllText(ScriptRunner.RepoFile((string)f[0]))).Count()) > 100, "expected the docs' kscript examples to be found");
    }

    [Theory]
    [MemberData(nameof(ScriptFiles))]
    public void SamplesAndTemplatesUseOneSpelling(string file)
    {
        string text = File.ReadAllText(ScriptRunner.RepoFile(file));
        string skip = null;
        if (text.Contains("{{", StringComparison.Ordinal) && file.Contains("Scaffolding", StringComparison.Ordinal))
        {
            // The word that stands in for a placeholder isn't quoted where the value it stands for is a number or a
            // duration; TemplateConventionTests checks every template filled in.
            skip = DiagnosticCodes.QuoteText;
            // A template isn't a script until it is filled in: read it with its {{#if}} lines dropped and a word in
            // each {{ placeholder }}, which keeps the code around them readable.
            text = Regex.Replace(text, @"\{\{\s*[#/][^}]*\}\}", string.Empty);
            // {{ pascal }} is where a type's name goes, so its word is a type's name.
            text = Regex.Replace(text, @"\{\{\s*pascal\s*\}\}", "X");
            text = Regex.Replace(text, @"\{\{[^}]*\}\}", "x");
        }
        Assert.Empty(Problems(text, ScriptRunner.RepoFile(file), skip: skip).Select(p => $"{file}{p}"));
    }

    [Theory]
    [MemberData(nameof(DocFiles))]
    public void DocsExamplesUseOneSpelling(string file)
    {
        var problems = new List<string>();
        foreach ((string code, int line) in Fences(File.ReadAllText(ScriptRunner.RepoFile(file))))
        {
            problems.AddRange(Problems(code, "example.kscript", line).Select(p => $"{file}{p}"));
        }
        Assert.Empty(problems);
    }

    public static IEnumerable<object[]> XmlFiles()
    {
        string root = ScriptRunner.RepoFile();
        foreach (string folder in new[] { "samples", Path.Combine("KScript.Cli", "Scaffolding") })
        {
            string full = Path.Combine(root, folder);
            if (!Directory.Exists(full))
            {
                continue;
            }
            foreach (string file in Directory.EnumerateFiles(full, "*.xml", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            {
                if (File.ReadAllText(file).Contains("<kscript", StringComparison.Ordinal))
                {
                    yield return new object[] { Path.GetRelativePath(root, file).Replace('\\', '/') };
                }
            }
        }
    }

    /// <summary>The XML samples declare names bare too: <c>&lt;do&gt;total += 1&lt;/do&gt;</c> and <c>fn(n) =&gt; $n</c>.</summary>
    [Theory]
    [MemberData(nameof(XmlFiles))]
    public void XmlSamplesDeclareNamesBare(string file)
    {
        string text = File.ReadAllText(ScriptRunner.RepoFile(file));
        Assert.Empty(Problems(text, ScriptRunner.RepoFile(file)).Select(p => $"{file}{p}"));
    }

    /// <summary>Every example the catalog shows on hover, in the docs and to agents, by what it is the example of.</summary>
    private static IEnumerable<(string Key, string Example)> CatalogExamples()
    {
        foreach (KScriptPackage package in PackageRegistry.Default.LoadAll())
        {
            foreach (ElementDefinition e in package.Elements.Values)
            {
                yield return ($"{package.Name} <{e.Info.Name}>", e.Info.Example);
            }
            foreach (CommandDefinition c in package.Commands.Values)
            {
                yield return ($"{package.Name} @{c.Info.Name}", c.Info.Example);
            }
            foreach (MethodDefinition m in package.Methods)
            {
                yield return ($"{package.Name} {m.Info.ReceiverType}->{m.Info.Name}", m.Info.Example);
            }
            foreach (ValueFieldDefinition f in package.Fields)
            {
                yield return ($"{package.Name} field {f.Info.Name}", f.Info.Example);
            }
        }
    }

    [Fact]
    public void CatalogExamplesDeclareNamesBare()
    {
        var problems = new List<string>();
        int count = 0;
        foreach ((string key, string example) in CatalogExamples())
        {
            if (string.IsNullOrWhiteSpace(example))
            {
                continue;
            }
            count++;

            // An example is a few elements or one expression.
            string script = example.TrimStart().StartsWith('<') ? $"<kscript>\n{example}\n</kscript>" : $"<kscript><echo>{{ {example} }}</echo></kscript>";
            problems.AddRange(Analyzer.Analyze(script, "example.ks.xml").Diagnostics
                .Where(d => d.Code == DiagnosticCodes.BareDeclaration)
                .Select(d => $"{key}: {d.Message} In: {example}"));
        }
        Assert.True(count > 500, $"expected the catalog's examples to be found, saw {count}");
        Assert.Empty(problems.Distinct());
    }

    /// <summary>Every old spelling, and the arms of a match written the old way (case, when, default blocks).</summary>
    private static IEnumerable<string> Problems(string code, string path, int firstLine = 0, string skip = null)
    {
        SourceText source = new SourceText(code);
        foreach (Diagnostic d in Analyzer.Analyze(code, path).Diagnostics.OrderBy(d => d.Span.Start))
        {
            // KS6013 too: what the samples, templates and docs declare is named as the language names its own.
            if (d.Code == skip)
            {
                continue;
            }
            if (Guarded.Contains(d.Code) || d.Code == DiagnosticCodes.NamingStyle || (d.Code == DiagnosticCodes.RetiredElement && d.Message.Contains("arm", StringComparison.Ordinal)))
            {
                yield return $"({firstLine + source.GetPosition(d.Span.Start).Line + 1}): {d.Code} {d.Message}";
            }
        }
    }

    private static readonly Regex Fence = new Regex(@"^([ \t]*)```kscript[^\n]*\n(.*?)^\1```[ \t]*$", RegexOptions.Multiline | RegexOptions.Singleline);

    /// <summary>Each ```kscript block of a Markdown file, with the line it starts on.</summary>
    private static IEnumerable<(string Code, int Line)> Fences(string markdown)
    {
        markdown = markdown.Replace("\r\n", "\n");
        foreach (Match m in Fence.Matches(markdown))
        {
            yield return (m.Groups[2].Value, markdown.Take(m.Groups[2].Index).Count(c => c == '\n'));
        }
    }

    #endregion

    #region Declared bare (KS6010)

    [Theory]
    [InlineData("x = 1\n$x = 3\n", "x = 1\nx = 3\n")]
    [InlineData("total = 0\n$total += 1\n", "total = 0\ntotal += 1\n")]
    [InlineData("settings = { theme: 'light' }\n$settings.theme = 'dark'\n", "settings = { theme: 'light' }\nsettings.theme = 'dark'\n")]
    [InlineData("rows = [1, 2]\n$rows[0] = 9\n", "rows = [1, 2]\nrows[0] = 9\n")]
    [InlineData("n = null\n$n ??= 2\n", "n = null\nn ??= 2\n")]
    public void AnAssignmentTargetIsWrittenWithoutTheSigil(string code, string expected)
    {
        Diagnostic d = Assert.Single(Lint(code));
        Assert.Equal(DiagnosticCodes.BareDeclaration, d.Code);
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Equal(expected, Fix(code));
        Assert.Empty(Lint(expected));
    }

    [Theory]
    [InlineData("double = fn($n) => $n * 2\necho $double(2)\n", "double = fn(n) => $n * 2\necho $double(2)\n", 1)]
    [InlineData("people = [{ age: 3 }]\necho $people->sort(fn($a, $b) => $a.age - $b.age)\n", "people = [{ age: 3 }]\necho $people->sort(fn(a, b) => $a.age - $b.age)\n", 2)]
    [InlineData("echo \"{ [1, 2]->map(fn($n) => $n * 2) }\"\n", "echo \"{ [1, 2]->map(fn(n) => $n * 2) }\"\n", 1)]
    public void ALambdaDeclaresItsParametersBare(string code, string expected, int count)
    {
        List<Diagnostic> found = Lint(code);
        Assert.Equal(count, found.Count);
        Assert.All(found, d => Assert.Equal(DiagnosticCodes.BareDeclaration, d.Code));
        Assert.Equal(expected, Fix(code));
        Assert.Empty(Lint(expected));
    }

    [Fact]
    public void XmlDeclaresThemBareToo()
    {
        const string xml = "<kscript>\n  <let name=\"total\" value=\"0\" />\n  <do>$total += 1</do>\n  <echo value=\"[1, 2]->map(fn($n) => $n * 2)\" />\n</kscript>\n";
        Assert.Equal(2, Lint(xml, "test.ks.xml").Count);
        string now = Fix(xml, "test.ks.xml");
        Assert.Contains("<do>total += 1</do>", now);
        Assert.Contains("fn(n) => $n * 2", now);
        Assert.Empty(Lint(now, "test.ks.xml"));
    }

    [Fact]
    public void OnlyTheBareLambdaFormRuns()
    {
        Assert.Equal("2, 4\n", Run("echo [1, 2]->map(fn(n) => $n * 2)\n"));
        KScriptException refused = Assert.Throws<KScriptException>(() => Run("echo [1, 2]->map(fn($n) => $n * 2)\n"));
        Assert.Contains("Declare the parameter as n", refused.Message);
    }

    [Fact]
    public void ErrorsTeachTheBareForms()
    {
        Assert.Contains("such as x", ExpressionParser.ParseExpression("fn(1) => 2").Errors[0].Message);
        Assert.DoesNotContain("$x", ExpressionParser.ParseExpression("fn(1) => 2").Errors[0].Message);
        Assert.Contains("x.total += 1", ExpressionParser.ParseStatements("1 = 2").Errors[0].Message);
    }

    #endregion

    #region Comparisons are symbols, logic is words (KS6012)

    [Theory]
    [InlineData("lt", "<")]
    [InlineData("le", "<=")]
    [InlineData("gt", ">")]
    [InlineData("ge", ">=")]
    [InlineData("eq", "==")]
    [InlineData("ne", "!=")]
    public void AComparisonWordIsASymbolInCode(string word, string symbol)
    {
        string code = $"n = 3\nif $n {word} 10 and not ($n {word} 2) {{\n  echo \"{{ $n {word} 1 }}\"\n}}\n";
        List<Diagnostic> found = Lint(code);
        Assert.Equal(3, found.Count);
        Assert.All(found, d => Assert.Equal(DiagnosticCodes.OneSpelling, d.Code));
        Assert.Contains($"Write {symbol}", found[0].Message);
        Assert.Equal(code.Replace($" {word} ", $" {symbol} "), Fix(code));
    }

    [Fact]
    public void XmlKeepsTheComparisonWords()
    {
        Assert.Empty(Lint("<kscript>\n  <let name=\"n\" value=\"3\" />\n  <if test=\"$n lt 10 and $n ge 1\"><echo>ok</echo></if>\n</kscript>\n", "test.ks.xml"));
    }

    [Fact]
    public void NamesThatLookLikeTheWordsAreLeftAlone()
    {
        Assert.Empty(Lint("row = { eq: 1, gt: 2 }\necho $row.eq + $row.gt\n"));
    }

    [Theory]
    [InlineData("$a && $b", "Use 'and' instead of '&&'.")]
    [InlineData("$a || $b", "Use 'or' instead of '||'.")]
    [InlineData("!$a", "Use 'not' instead of '!'.")]
    public void LogicSymbolsAreNamedWithoutMentioningXml(string expression, string message)
    {
        Assert.Equal(message, ExpressionParser.ParseExpression(expression).Errors[0].Message);
        KScriptException error = Assert.Throws<KScriptException>(() => Run($"a = true\nb = false\necho {expression}\n"));
        Assert.Contains(message, error.Message);
        Assert.DoesNotContain("XML", error.Message);
    }

    #endregion

    #region One spelling each: elif, key: value, catch A | B (KS6012)

    [Theory]
    [InlineData("n = 1\nif $n > 1 {\n  echo \"a\"\n} else if $n > 0 {\n  echo \"b\"\n}\n", "n = 1\nif $n > 1 {\n  echo \"a\"\n} elif $n > 0 {\n  echo \"b\"\n}\n")]
    [InlineData("wait(seconds = 0)\n", "wait(seconds: 0)\n")]
    [InlineData("wait(seconds=0)\n", "wait(seconds: 0)\n")]
    [InlineData("try {\n  echo \"x\"\n} catch NotFound, Conflict as e {\n  echo \"y\"\n}\n", "try {\n  echo \"x\"\n} catch NotFound | Conflict as e {\n  echo \"y\"\n}\n")]
    [InlineData("try {\n  echo \"x\"\n} catch NotFound,Conflict,Io {\n  echo \"y\"\n}\n", "try {\n  echo \"x\"\n} catch NotFound | Conflict | Io {\n  echo \"y\"\n}\n")]
    public void ASecondSpellingIsReportedAndFixed(string code, string expected)
    {
        List<Diagnostic> found = Lint(code);
        Assert.NotEmpty(found);
        Assert.All(found, d =>
        {
            Assert.Equal(DiagnosticCodes.OneSpelling, d.Code);
            Assert.Equal(DiagnosticSeverity.Error, d.Severity);
            Assert.NotEmpty(d.Fixes);
        });
        Assert.Equal(expected, Fix(code));
        Assert.Empty(Lint(expected));
    }

    [Fact]
    public void TheSecondSpellingsDontRun()
    {
        KScriptException refused = Assert.Throws<KScriptException>(() => Run("n = 1\nif $n > 1 { echo \"a\" } else if $n > 0 { echo \"b\" }\n"));
        Assert.Contains("Write elif", refused.Message);
        Assert.Equal("b\n", Run("n = 1\nif $n > 1 { echo \"a\" } elif $n > 0 { echo \"b\" }\n"));
    }

    [Fact]
    public void TheFixerAppliesBothRules()
    {
        Assert.Contains(DiagnosticCodes.BareDeclaration, Fixer.Rules);
        Assert.Contains(DiagnosticCodes.OneSpelling, Fixer.Rules);
    }

    #endregion

    #region match: one arm syntax

    private const string MatchStatement = """
        fn tell(n) {
          match $n {
            1 => echo "one"
            2, 3 => {
              echo "two or three"
            }
            4 when $n > 3 => echo "four"
            _ when $n > 9 => { echo "big" }
            _ => echo "other"
          }
        }
        for n in [1, 3, 4, 12, 7] { @tell($n) }
        """;

    [Fact]
    public void AMatchStatementUsesArms()
    {
        MarkupDocument parsed = CodeParser.Parse(MatchStatement);
        Assert.Empty(parsed.Diagnostics);
        Assert.Equal("one\ntwo or three\nfour\nbig\nother\n", Run(MatchStatement));
    }

    [Fact]
    public void TheMatchExpressionWritesItsArmsTheSameWay()
    {
        const string code = """
            fn name(n) => match $n { 1 => 'one', 2, 3 => 'two or three', 4 when $n > 3 => 'four', _ when $n > 9 => 'big', _ => 'other' }
            fn lines(n) => match $n {
              1 => 'one'
              2, 3 => 'few'
              [4, 5] => 'some'
              _ => 'many'
            }
            echo [1, 3, 4, 12, 7]->map(fn(n) => @name($n))->join(', ')
            echo [1, 2, 5, 6]->map(fn(n) => @lines($n))->join(', ')
            """;
        Assert.Empty(Analyzer.Analyze(code, "test.kscript").Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Equal("one, two or three, four, big, other\none, few, some, many\n", Run(code));
    }

    [Fact]
    public void ArmsOnOneLineAreSeparatedByCommas()
    {
        Assert.Equal("b\n", Run("match 2 { 1 => echo \"a\", 2 => echo \"b\", _ => echo \"c\" }\n"));
    }

    [Theory]
    [InlineData("match $n {\n  case 1, 2 { echo \"low\" }\n}\n", "'case' is the XML form", "match $n {\n  1, 2 => { echo \"low\" }\n}\n")]
    [InlineData("match $n {\n  when $n > 9 { echo \"big\" }\n}\n", "'when' is the XML form", "match $n {\n  _ when $n > 9 => { echo \"big\" }\n}\n")]
    [InlineData("match $n {\n  default { echo \"?\" }\n}\n", "'default' is the XML form", "match $n {\n  _ => { echo \"?\" }\n}\n")]
    [InlineData("match $n {\n  default => echo \"?\"\n}\n", "Write _ for the arm that matches anything", "match $n {\n  _ => echo \"?\"\n}\n")]
    [InlineData("match $n {\n  case 1 => echo \"one\"\n}\n", "Drop 'case'", "match $n {\n  1 => echo \"one\"\n}\n")]
    [InlineData("match $n {\n  when $n > 9 => echo \"big\"\n}\n", "Write the test after a pattern", "match $n {\n  _ when $n > 9 => echo \"big\"\n}\n")]
    public void TheOldStatementFormsAreErrorsThatNameTheArm(string code, string message, string expected)
    {
        MarkupDocument parsed = CodeParser.Parse("n = 1\n" + code);
        Diagnostic d = Assert.Single(parsed.Diagnostics);
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Contains(message, d.Message);
        Assert.Contains("=>", d.Message);

        // The quick fix writes the arm.
        var text = new System.Text.StringBuilder("n = 1\n" + code);
        foreach (TextChange change in Assert.Single(d.Fixes).Changes.OrderByDescending(c => c.Span.Start))
        {
            text.Remove(change.Span.Start, change.Span.Length).Insert(change.Span.Start, change.NewText);
        }
        Assert.Equal("n = 1\n" + expected, text.ToString());
        Assert.Empty(CodeParser.Parse(text.ToString()).Diagnostics);

        // kscript check --fix applies it too.
        Assert.True(CodeParser.IsArmFix(d));
        Assert.Equal("n = 1\n" + expected, Fixer.Apply("n = 1\n" + code, "test.kscript", also: CodeParser.IsArmFix).Text);
    }

    [Fact]
    public void ArmsConvertToXmlCasesAndBack()
    {
        const string code = "match $n {\n  1 => echo \"one\"\n  2, 3 => {\n    echo \"two\"\n    echo \"or three\"\n  }\n  4 when $ready => echo \"four\"\n  _ when $n > 9 => echo \"big\"\n  _ => echo \"other\"\n}\n";
        string xml = ScriptConverter.Convert(code, "a.kscript").Text;
        Assert.Contains("<case is=\"1\">", xml);
        Assert.Contains("<case is=\"2, 3\">", xml);
        Assert.Contains("<case is=\"4\" when=\"$ready\">", xml);
        Assert.Contains("<case when=\"$n > 9\">", xml);
        Assert.Contains("<default>", xml);
        Assert.Equal(code, ScriptConverter.Convert(xml, "a.ks.xml").Text);
    }

    [Fact]
    public void ConvertingXmlWritesTheCodeSpellings()
    {
        const string xml = "<kscript>\n  <let name=\"n\" value=\"3\" />\n  <if test=\"$n lt 10 and $n ne 4\">\n    <echo value=\"[1, 2]->filter(fn($x) => $x ge $n)\" />\n    <echo>{ $n le 3 ? 'small' : 'big' }</echo>\n  </if>\n</kscript>\n";
        string code = ScriptConverter.Convert(xml, "a.ks.xml").Text;
        Assert.Contains("if $n < 10 and $n != 4 {", code);
        Assert.Contains("fn(x) => $x >= $n", code);
        Assert.Contains("{ $n <= 3 ? 'small' : 'big' }", code);
        Assert.Empty(Lint(code));
    }

    [Fact]
    public void TheFormatterLaysOutArms()
    {
        const string messy = "match $n {\n1,2=>echo \"low\"\n      3 => { total+=1; echo \"three\" }\n_ when $n>9=>{\necho \"big\"\n}\n  _ => echo \"other\"\n}\n";
        const string tidy = "match $n {\n  1, 2 => echo \"low\"\n  3 => {\n    total += 1\n    echo \"three\"\n  }\n  _ when $n>9 => {\n    echo \"big\"\n  }\n  _ => echo \"other\"\n}\n";
        FormatResult result = CodeFormatter.Format(messy);
        Assert.Equal(tidy, result.Text);
        Assert.Equal(tidy, CodeFormatter.Format(tidy).Text);
    }

    #endregion
}
