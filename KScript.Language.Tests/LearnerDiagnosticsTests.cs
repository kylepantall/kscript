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
            string text = $"{word} twice(n: int) {{\n  return $n * 2\n}}\necho @twice(2)\n";
            DocumentAnalysis a = Analyze(text);
            Diagnostic d = Assert.Single(a.Diagnostics);
            Assert.Equal($"Functions start with fn, not {word}: fn greet(who) {{ }}.", d.Message);
            // It is read as the function it was meant to be, so its body and its callers are checked as usual.
            Assert.Contains(a.Symbols, s => s.Kind == SymbolKind.Function && s.Name == "twice");
            string expected = "fn twice(n: int) {\n  return $n * 2\n}\necho @twice(2)\n";
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
        [InlineData("switch 1 {\n}\n", "KScript writes match, not switch: match $value { 1 => { } _ => { } }", "Change to match")]
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
            Assert.Contains(Errors("<kscript><let name=\"x\" value=\"1 &amp;&amp; 2\" /></kscript>", "a.ks.xml"), d => d.Message == "Use 'and' instead of '&&'.");
        }

        [Theory]
        [InlineData("len", "KScript has no @len. Use the count method: $list->count().")]
        [InlineData("strlen", "KScript has no @strlen. Use the count method: $text->count().")]
        [InlineData("typeof", "KScript has no @typeof. Use the type method: $value->type().")]
        public void CommandsOfOtherLanguagesNameTheMethod(string name, string message)
        {
            Diagnostic d = Assert.Single(Errors($"nums = [1, 2]\necho @{name}($nums)\n"));
            Assert.Equal(DiagnosticCodes.UnknownCommand, d.Code);
            Assert.Equal(message, d.Message);
        }

        [Fact]
        public void AFunctionOfYourOwnWithThatNameIsNotAHabit()
        {
            Assert.Empty(Errors("fn len(list) => $list->count()\necho @len([1, 2])\n"));
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
        [InlineData("names = [1, 2]\n  |> map(fn(n) => $n)\necho $names\n")]
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
        public void SingleQuotesAreNotFilledIn()
        {
            const string text = "name = 'Joan'\nn = 2\ngreeting = 'Hello $name, you have {$n * 2}!'\necho $greeting\n";
            Diagnostic d = Assert.Single(Analyze(text).Diagnostics);
            Assert.Equal(DiagnosticCodes.TextNotFilledIn, d.Code);
            Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
            Assert.StartsWith("$name is not filled in here: single quotes keep text as written. Use double quotes", d.Message);
            string expected = "name = 'Joan'\nn = 2\ngreeting = \"Hello $name, you have {$n * 2}!\"\necho $greeting\n";
            Assert.Equal(expected, Fixed(text, d));
            Assert.Empty(Analyze(expected).Diagnostics);
        }

        [Fact]
        public void AnXmlScriptIsOfferedTheJoinedForm()
        {
            const string text = "<kscript><let name=\"who\" value=\"'a'\" /><let name=\"g\" value=\"'Hi $who'\" /><echo>$g</echo></kscript>";
            Diagnostic d = Assert.Single(Analyze(text, "a.ks.xml").Diagnostics);
            Assert.Equal(DiagnosticCodes.TextNotFilledIn, d.Code);
            Assert.Contains("Join the parts instead: 'Hi ' + $who.", d.Message);
            Assert.Contains("value=\"'Hi ' + $who\"", Fixed(text, d));
        }

        [Theory]
        [InlineData("name = 'Joan'\necho \"Hello $name\"\n")]
        [InlineData("name = 'Joan'\ng = \"Hello $name\"\necho $g\n")]
        [InlineData("fn greet(who: string) {\n  return \"Hi $who!\"\n}\necho @greet('a')\n")]
        [InlineData("price = 'It costs $5'\necho $price\n")]
        [InlineData("label = '$nobody is here'\necho $label\n")]
        [InlineData("name = 'Joan'\ntext = 'Hello ' + $name\necho $text\n")]
        [InlineData("fn shout(text: string) {\n  return $text->upper()\n}\nname = 'Joan'\necho @shout('$name') + $name\n")]
        public void FilledInTextAndPlainTextAreLeftAlone(string text)
        {
            Assert.Empty(Analyze(text).Diagnostics);
        }

        [Fact]
        public void ValuesInsideDoubleQuotesAreChecked()
        {
            // The names inside the text are real uses: an unknown one is an error, a known one counts as used.
            Diagnostic d = Assert.Single(Analyze("g = \"Hello $nobody and { $missing->upper() }\"\necho $g\n").Diagnostics, x => x.Message.Contains("$nobody"));
            Assert.Equal(DiagnosticSeverity.Error, d.Severity);
            Assert.Equal(12, d.Span.Start);
            Assert.Contains(Analyze("n = 5\ng = \"{ $n->upper() }\"\necho $g\n").Diagnostics, x => x.Code == DiagnosticCodes.TypeMismatch);
            // A brace always holds an expression, whatever it opens with: a type, a number, a bracket.
            Assert.Empty(Analyze("type P {\n  n: int\n}\ng = \"{ P(n: 1).n } { 1 + 2 } { (3) } { [1, 2]->count() }\"\necho $g\n").Diagnostics);
            Diagnostic id = Assert.Single(Analyze("route = \"/users/{id}\"\necho $route\n").Diagnostics);
            Assert.Contains("'id' isn't a value", id.Message);
            Assert.Contains(Analyze("css = \"a { color: red }\"\necho $css\n").Diagnostics, x => x.Message.Contains("write {{ and }} for braces, or use 'single quotes'"));
            // Doubled braces and single quotes are text.
            Assert.Empty(Analyze("route = \"{{x}} a@b.com $5\"\necho $route\npath = '/users/{id} \\d{3}'\necho $path\n").Diagnostics);
        }

        [Fact]
        public void TheEditorWorksInsideDoubleQuotes()
        {
            (string text, int offset) = TestHelpers.WithCaret("name = 'Joan'\ng = \"Hello $na‸me and { $name->upper() }\"\necho $g\n");
            DocumentAnalysis a = Analyze(text);
            Assert.Contains("name", Service.GetHover(a, offset)?.Markdown);
            Assert.Equal(3, Service.GetReferences(a, offset, includeDeclaration: true).Count);
            RenameResult rename = Service.Rename(a, offset, "who");
            Assert.Null(rename.Error);
            Assert.Equal("who = 'Joan'\ng = \"Hello $who and { $who->upper() }\"\necho $g\n", AnalyzerTests.Apply(text, rename.Changes));

            (text, offset) = TestHelpers.WithCaret("name = 'Joan'\ng = \"Hello { $name->‸ }\"\necho $g\n");
            Assert.Contains(Service.GetCompletions(Analyze(text), offset), i => i.Label == "upper");
            (text, offset) = TestHelpers.WithCaret("name = 'Joan'\ng = \"Hello $na‸\"\necho $g\n");
            Assert.Contains(Service.GetCompletions(Analyze(text), offset), i => i.Label.TrimStart('$') == "name");
        }

        [Fact]
        public void TextThatCannotBeRewrittenGetsNoFix()
        {
            Diagnostic d = Analyze("name = 'Joan'\ng = 'Say \"hi\" to $name'\necho $g\n").Diagnostics.Single(x => x.Code == DiagnosticCodes.TextNotFilledIn);
            Assert.Empty(d.Fixes);
            Assert.EndsWith("Use double quotes to fill it in.", d.Message);
        }

        [Fact]
        public void SqlTextFilledInBeforeItReachesSqlIsWarnedAbout()
        {
            const string head = "import KScript.Data\nid = 4\n";
            Diagnostic d = Assert.Single(Analyze(head + "query = \"select * from users where id = {$id}\"\necho @sql($query)\n").Diagnostics);
            Assert.Equal(DiagnosticCodes.TextNotFilledIn, d.Code);
            Assert.StartsWith("$query was given text in double quotes, so its values are already pasted into the SQL", d.Message);
            // Written in the call, or held in single quotes, the text reaches @sql as written.
            Assert.Empty(Analyze(head + "echo @sql(\"select * from users where id = {$id}\")\n").Diagnostics);
            Assert.DoesNotContain(Analyze(head + "query = 'select * from users where id = {$id}'\necho @sql($query)\n").Diagnostics, x => x.Message.Contains("$query"));
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
            string docs = File.ReadAllText(Path.Combine(FormatterTests.RepoRoot(), "docs", "errors.md")).Replace("\r\n", "\n");
            foreach (System.Reflection.FieldInfo field in typeof(DiagnosticCodes).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                string code = (string)field.GetValue(null);
                Assert.True(docs.Contains($"\n## {code}\n", StringComparison.Ordinal), $"docs/errors.md has no \"## {code}\" section for DiagnosticCodes.{field.Name}.");
                Assert.EndsWith("/docs/errors.md#" + code.ToLowerInvariant(), DiagnosticCodes.HelpUrl(code));
            }
        }
    }
}
