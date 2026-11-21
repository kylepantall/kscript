using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Syntax;

namespace KScript.Language.Tests
{
    /// <summary>
    /// What someone new to KScript is told when they write what they know from another language, leave a line
    /// unfinished, or expect text to be filled in: the message names the mistake, in the file's own format.
    /// </summary>
    public class LearnerDiagnosticsTests
    {
        private static readonly LanguageService Service = new LanguageService(files: EmptyFileSystem.Instance);

        private static DocumentAnalysis Analyze(string text, string file = "a.kscript") => Service.Analyze(text, Path.Combine(Path.GetTempPath(), file));

        private static List<Diagnostic> Errors(string text, string file = "a.kscript") =>
            Analyze(text, file).Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

        private static string Fixed(string text, Diagnostic d) => AnalyzerTests.Apply(text, d.Fixes[0].Changes);

        [Theory]
        [InlineData("print \"hi\"\n", "print", "echo \"hi\"\n")]
        [InlineData("println(\"hi\")\n", "println", "echo \"hi\"\n")]
        [InlineData("console.log(\"hi\")\n", "console.log", "echo \"hi\"\n")]
        [InlineData("puts \"hi\"\n", "puts", "echo \"hi\"\n")]
        [InlineData("pritn \"hi\"\n", "pritn", "echo \"hi\"\n")]
        public void PrintingPointsToEcho(string text, string word, string expected)
        {
            Diagnostic d = Assert.Single(Errors(text));
            Assert.Equal(DiagnosticCodes.UnknownElement, d.Code);
            Assert.Equal($"KScript prints with echo, not {word}: echo \"Hello\".", d.Message);
            Assert.Equal("Change to echo", d.Fixes[0].Title);
            Assert.Equal(expected, Fixed(text, d));
            Assert.Empty(Errors(expected));
        }

        [Theory]
        [InlineData("function")]
        [InlineData("def")]
        [InlineData("func")]
        public void FunctionKeywordsPointToFn(string word)
        {
            string text = $"{word} twice(n) {{\n  return $n * 2\n}}\necho @twice(2)\n";
            DocumentAnalysis a = Analyze(text);
            Diagnostic d = Assert.Single(a.Diagnostics);
            Assert.Equal($"Functions start with fn, not {word}: fn greet(who) {{ }}.", d.Message);
            // It is read as the function it was meant to be, so its body and its callers are checked as usual.
            Assert.Contains(a.Symbols, s => s.Kind == SymbolKind.Function && s.Name == "twice");
            string expected = "fn twice(n) {\n  return $n * 2\n}\necho @twice(2)\n";
            Assert.Equal(expected, Fixed(text, d));
            Assert.Empty(Analyze(expected).Diagnostics);
        }

        [Fact]
        public void AWordThatOnlyLooksLikeAFunctionKeywordIsLeftAlone()
        {
            // No name and bracket after it, so it isn't a function declaration: the keyword table answers instead.
            Diagnostic d = Assert.Single(Errors("function\n"));
            Assert.Equal("KScript writes fn, not function: fn greet(who) { }", d.Message);
        }

        [Fact]
        public void VarIsDropped()
        {
            const string text = "var count = 1\necho $count\n";
            Diagnostic d = Assert.Single(Errors(text));
            Assert.Contains("Drop 'var'", d.Message);
            Assert.Equal("Remove 'var'", d.Fixes[0].Title);
            Assert.Equal("count = 1\necho $count\n", Fixed(text, d));
        }

        [Theory]
        [InlineData("n = 1\nn++\necho $n\n", "KScript has no ++. Write n += 1.", "n = 1\nn += 1\necho $n\n")]
        [InlineData("n = 1\nn--\necho $n\n", "KScript has no --. Write n -= 1.", "n = 1\nn -= 1\necho $n\n")]
        public void IncrementIsWrittenOut(string text, string message, string expected)
        {
            Diagnostic d = Assert.Single(Errors(text));
            Assert.Equal(message, d.Message);
            Assert.Equal(expected, Fixed(text, d));
            Assert.Empty(Errors(expected));
        }

        [Theory]
        [InlineData("n = 1\nn == 2\necho $n\n", "n = 1\nn = 2\necho $n\n")]
        [InlineData("n = 1\nn === 2\necho $n\n", "n = 1\nn = 2\necho $n\n")]
        public void AComparisonOnItsOwnLineIsExplained(string text, string expected)
        {
            Diagnostic d = Assert.Single(Errors(text));
            Assert.StartsWith("This compares n and does nothing with the answer.", d.Message);
            Assert.DoesNotContain("removed in KScript 2", d.Message);
            Assert.Equal(expected, Fixed(text, d));
        }

        [Theory]
        [InlineData("foreach x in [1] {\n  echo $x\n}\n", "KScript writes for, not foreach: for item in $list { }", "Change to for")]
        [InlineData("switch 1 {\n}\n", "KScript writes match, not switch: match $value { case 1 { } default { } }", "Change to match")]
        [InlineData("raise Validation \"no\"\n", "KScript writes throw, not raise: throw Validation \"Age must be positive\"", "Change to throw")]
        public void KeywordsOfOtherLanguagesNameKScriptsWord(string text, string message, string fix)
        {
            Diagnostic d = Errors(text).First(x => x.Code == DiagnosticCodes.UnknownElement);
            Assert.Equal(message, d.Message);
            Assert.Equal(fix, d.Fixes[0].Title);
        }

        [Fact]
        public void UnknownStatementsAreNotCalledElements()
        {
            Diagnostic d = Assert.Single(Errors("ehco \"hi\"\n"));
            Assert.Equal("Unknown statement 'ehco'. Did you mean echo?", d.Message);
            Assert.Equal("Change to echo", d.Fixes[0].Title);
            // An XML script keeps its own wording.
            Diagnostic xml = Assert.Single(Errors("<kscript><ehco>hi</ehco></kscript>", "a.ks.xml"));
            Assert.Equal("Unknown element <ehco>. Did you mean <echo>?", xml.Message);
            Assert.Equal("Change to <echo>", xml.Fixes[0].Title);
        }

        [Fact]
        public void KScriptOneElementsAreOnlyReportedInXml()
        {
            Assert.DoesNotContain(Errors("x = 1\nx == 1\ndef\n"), d => d.Message.Contains("removed in KScript 2"));
            Assert.Contains(Errors("<kscript><def id=\"x\" /></kscript>", "a.ks.xml"), d => d.Message.Contains("<def> was removed in KScript 2"));
        }

        [Theory]
        [InlineData("return 5\n", "'return' only works inside fn.")]
        [InlineData("x = 1\nif $x == 1 && $x == 1 {\n}\n", "Use 'and' instead of '&&'.")]
        [InlineData("x = 1\nif $x = 1 {\n}\n", "Assignment isn't allowed here. Use == (or ===) to compare; assign on a line of its own: x = 1.")]
        [InlineData("for i in 0..3 {\n  echo $i\n", "This for block is missing its closing }.")]
        public void CodeFilesAreNotToldAboutXml(string text, string message)
        {
            List<Diagnostic> errors = Errors(text);
            Assert.Contains(errors, d => d.Message == message);
            Assert.DoesNotContain(errors, d => d.Message.Contains('<') || d.Message.Contains("XML"));
        }

        [Fact]
        public void XmlScriptsKeepTheirWording()
        {
            Assert.Contains(Errors("<kscript><return value=\"5\" /></kscript>", "a.ks.xml"), d => d.Message == "<return> must be inside <fn>.");
            Assert.Contains(Errors("<kscript><let name=\"x\" value=\"1 &amp;&amp; 2\" /></kscript>", "a.ks.xml"), d => d.Message.Contains("isn't allowed in XML"));
        }

        [Theory]
        [InlineData("len", "KScript has no @len. Use the length method: $list->length().")]
        [InlineData("strlen", "KScript has no @strlen. Use the length method: $text->length().")]
        [InlineData("typeof", "KScript has no @typeof. Use @type($value), or $value->type().")]
        public void CommandsOfOtherLanguagesNameTheMethod(string name, string message)
        {
            Diagnostic d = Assert.Single(Errors($"nums = [1, 2]\necho @{name}($nums)\n"));
            Assert.Equal(DiagnosticCodes.UnknownCommand, d.Code);
            Assert.Equal(message, d.Message);
        }

        [Fact]
        public void AFunctionOfYourOwnWithThatNameIsNotAHabit()
        {
            Assert.Empty(Errors("fn len(list) => $list->length()\necho @len([1, 2])\n"));
        }

        [Fact]
        public void AnUnfinishedLineIsReportedWhereItIs()
        {
            const string text = "k = 5 +\nz = 1\necho \"$k $z\"\n";
            Diagnostic d = Assert.Single(Analyze(text).Diagnostics);
            Assert.Equal("Expected an expression after '+'.", d.Message);
            Assert.Equal(0, Analyze(text).Source.GetPosition(d.Span.Start).Line);
        }

        [Theory]
        [InlineData("total = 1 +\n  2\necho $total\n")]
        [InlineData("a = 1\ntotal = $a +\n  $a\necho $total\n")]
        [InlineData("ok = true and\n  not false\necho $ok\n")]
        [InlineData("names = [1, 2]\n  |> map(fn($n) => $n)\necho $names\n")]
        public void AValueCanStillCarryOnAfterAnOperator(string text)
        {
            Assert.Empty(Analyze(text).Diagnostics);
        }

        [Theory]
        [InlineData("echo \"unclosed\nx = 1\necho $x\n", "This text is missing its closing \".")]
        [InlineData("y = \"unclosed\nx = 1\necho \"$x $y\"\n", "This string is missing its closing \".")]
        [InlineData("y = 'unclosed\nx = 1\necho \"$x $y\"\n", "This string is missing its closing '.")]
        public void AMissingQuoteEndsWithItsLine(string text, string message)
        {
            DocumentAnalysis a = Analyze(text);
            Assert.Contains(a.Diagnostics, d => d.Message == message && a.Source.GetPosition(d.Span.Start).Line == 0);
            // The lines after it are still code: $x is declared and read.
            Assert.All(a.Diagnostics, d => Assert.Equal(0, a.Source.GetPosition(d.Span.Start).Line));
            Assert.Contains(a.Symbols, s => s.Name == "x");
        }

        [Fact]
        public void TextOverSeveralLinesStillWorks()
        {
            Assert.Empty(Analyze("echo \"one\ntwo\"\nx = \"three\nfour\"\necho $x\n").Diagnostics);
        }

        [Fact]
        public void TextUsedAsAValueIsNotATemplate()
        {
            const string text = "name = 'Joan'\nn = 2\ngreeting = \"Hello $name, you have {$n * 2} and {$n}!\"\necho $greeting\n";
            Diagnostic d = Assert.Single(Analyze(text).Diagnostics);
            Assert.Equal(DiagnosticCodes.TextNotFilledIn, d.Code);
            Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
            Assert.StartsWith("$name is not filled in here: only the text after echo is a template", d.Message);
            string expected = "name = 'Joan'\nn = 2\ngreeting = 'Hello ' + $name + ', you have ' + ($n * 2) + ' and ' + $n + '!'\necho $greeting\n";
            Assert.Equal(expected, Fixed(text, d));
            Assert.Empty(Analyze(expected).Diagnostics);
        }

        [Theory]
        [InlineData("fn greet(who) {\n  return \"Hi $who!\"\n}\necho @greet('a')\n", "a.kscript")]
        [InlineData("<kscript><let name=\"who\" value=\"'a'\" /><let name=\"g\" value=\"'Hi $who'\" /><echo>$g</echo></kscript>", "a.ks.xml")]
        public void TheWarningCoversReturnsAndXml(string text, string file)
        {
            Diagnostic d = Assert.Single(Analyze(text, file).Diagnostics);
            Assert.Equal(DiagnosticCodes.TextNotFilledIn, d.Code);
            Assert.Contains("$who is not filled in here", d.Message);
        }

        [Theory]
        [InlineData("name = 'Joan'\necho \"Hello $name\"\n")]
        [InlineData("price = \"It costs $5\"\necho $price\n")]
        [InlineData("label = \"$nobody is here\"\necho $label\n")]
        [InlineData("name = 'Joan'\ntext = 'Hello ' + $name\necho $text\n")]
        public void TemplatesAndPlainTextAreLeftAlone(string text)
        {
            Assert.DoesNotContain(Analyze(text).Diagnostics, d => d.Code == DiagnosticCodes.TextNotFilledIn);
        }

        [Fact]
        public void TextThatCannotBeRewrittenGetsNoFix()
        {
            Diagnostic d = Analyze("name = 'Joan'\ng = \"It's $name\"\necho $g\n").Diagnostics.Single(x => x.Code == DiagnosticCodes.TextNotFilledIn);
            Assert.Empty(d.Fixes);
            Assert.EndsWith("Join the parts with + instead.", d.Message);
        }

        [Fact]
        public void AVariableGivenAnotherTypeIsNotHeldToTheFirst()
        {
            // x holds text by the time ->upper() runs, so nothing is wrong.
            Assert.Empty(Analyze("x = 5\nx = 'now text'\necho $x->upper()\n").Diagnostics);
            // With one type throughout, the check still applies.
            Assert.Contains(Analyze("x = 5\nx = 6\necho $x->upper()\n").Diagnostics, d => d.Code == DiagnosticCodes.TypeMismatch);
        }

        [Fact]
        public void EveryCodeIsExplainedInTheDocs()
        {
            string docs = File.ReadAllText(Path.Combine(FormatterTests.RepoRoot(), "docs", "errors.md"));
            foreach (System.Reflection.FieldInfo field in typeof(DiagnosticCodes).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                string code = (string)field.GetValue(null);
                Assert.True(docs.Contains($"\n## {code}\n", StringComparison.Ordinal), $"docs/errors.md has no \"## {code}\" section for DiagnosticCodes.{field.Name}.");
                Assert.EndsWith("/docs/errors.md#" + code.ToLowerInvariant(), DiagnosticCodes.HelpUrl(code));
            }
        }
    }
}
