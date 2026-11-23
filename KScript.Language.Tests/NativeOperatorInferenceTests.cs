using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Metadata;
using KScript.Syntax;
using Xunit;

namespace KScript.Language.Tests
{
    /// <summary>
    /// The editor reads the operators, ordering and iteration native types declare: 1h + 30m is a duration,
    /// @now() + 1h a date, and an operator a type doesn't declare is warned about with the runtime's words.
    /// </summary>
    public class NativeOperatorInferenceTests
    {
        private static readonly LanguageService Service = new LanguageService();

        /// <summary>A type with no order and no walking, as many native types are (registered for these tests only).</summary>
        private static readonly NativeTypeInfo Badge = Registered(new NativeTypeInfo("lt_badge", "A badge, for tests.", "gold"));

        /// <summary>A type a for loop can walk, giving text.</summary>
        private static readonly NativeTypeInfo Phrase = Registered(new NativeTypeInfo("lt_phrase", "Words, for tests.", "'a b c'") { Ordering = "text", Iterates = "string" });

        private static NativeTypeInfo Registered(NativeTypeInfo info)
        {
            NativeTypes.Register(info);
            return info;
        }

        private static DocumentAnalysis Analyze(string text) => Service.Analyze(text, Path.Combine(Path.GetTempPath(), "native-ops.kscript"));

        private static List<Diagnostic> Check(string text) =>
            Analyze(text).Diagnostics.Where(d => d.Code != DiagnosticCodes.UnusedVariable && d.Code != DiagnosticCodes.MissingParameterType
                // The test types have no parser here, so their values can't be checked.
                && d.Code != DiagnosticCodes.InvalidTypedValue).ToList();

        private static TypeShape ShapeOf(string text, string name) => Analyze(text).Symbols.Last(s => s.Name == name).Shape;

        // ---- Inferred types ------------------------------------------------------------------------------------

        [Theory]
        [InlineData("x = 1h + 30m\necho $x\n", "duration")]
        [InlineData("x = 1h - 15m\necho $x\n", "duration")]
        [InlineData("x = 30m * 3\necho $x\n", "duration")]
        [InlineData("x = 3 * 30m\necho $x\n", "duration")]
        [InlineData("x = 1h / 4\necho $x\n", "duration")]
        [InlineData("x = 1h / 15m\necho $x\n", "number")]
        [InlineData("x = 1h10m % 30m\necho $x\n", "duration")]
        [InlineData("x = @now() + 1h\necho $x\n", "date")]
        [InlineData("x = 1h + @now()\necho $x\n", "date")]
        [InlineData("x = @now() - 1d\necho $x\n", "date")]
        [InlineData("x = -1h\necho $x\n", "duration")]
        [InlineData("d: duration = 1h\nx = $d * 2 + 30m\necho $x\n", "duration")]
        [InlineData("x = 'took ' + 1h\necho $x\n", "string")] // text joins
        [InlineData("x = 1 + 2\necho $x\n", "number")]
        [InlineData("x = -5\necho $x\n", "number")]
        public void ArithmeticOnNativeValuesHasTheDeclaredResult(string text, string expected)
        {
            Assert.Equal(expected, ShapeOf(text, "x").Type);
        }

        [Fact]
        public void ADateRangeSteppedByADurationIsAListOfDates()
        {
            TypeShape days = ShapeOf("a = @now()\nb = $a + 7d\nx = $a..$b step 1d\necho $x\n", "x");
            Assert.Equal(KType.List, days.Type);
            Assert.Equal(KType.Date, days.Element.Type);

            TypeShape hours = ShapeOf("a = @now()\nx = $a..($a + 1d) step 1h\necho $x\n", "x");
            Assert.Equal(KType.Date, hours.Element.Type);

            // Numbers still make numbers.
            Assert.Equal(KType.Number, ShapeOf("x = 0..10 step 2\necho $x\n", "x").Element.Type);
        }

        [Fact]
        public void ALoopOverADateRangeGivesDates()
        {
            Assert.Equal(KType.Date, ShapeOf("a = @now()\nfor day in $a..($a + 7d) step 1d {\n  echo $day\n}\n", "day").Type);
        }

        [Fact]
        public void ALoopOverATypeThatIteratesGivesItsItems()
        {
            Assert.Equal("string", Phrase.Iterates);
            const string loop = "fn words(p: lt_phrase) {\n  for w in $p {\n    echo $w\n  }\n}\n@words('a b')\n";
            Assert.Equal(KType.String, ShapeOf(loop, "w").Type);
            Assert.Empty(Check(loop));
        }

        // ---- Diagnostics ----------------------------------------------------------------------------------------

        [Theory]
        [InlineData("x = 1h + 30m\necho $x\n")]
        [InlineData("d: duration = 1h\nx = $d * 3\necho $x\n")]
        [InlineData("d: duration = 1h\nx = $d / 15m\necho $x\n")]
        [InlineData("x = @now() + 1h\necho $x\n")]
        [InlineData("x = -1h\necho $x\n")]
        [InlineData("d: duration = 1h\necho 'took ' + $d\n")] // text joins with +
        [InlineData("d: duration = 1h\necho $d + ' in all'\n")]
        [InlineData("d: duration = 1h\nfn twice(n) {\n  return $d * $n\n}\necho @twice(2)\n")] // the other side may fit
        [InlineData("d: duration = 1h\necho $d < 2h\n")] // durations have an order
        public void DeclaredNativeOperatorsAreNotWarnedAbout(string text)
        {
            Assert.Empty(Check(text));
        }

        [Theory]
        [InlineData("x = 1h * 1h\necho $x\n", "'*' doesn't work on a duration (1h) and a duration (1h). With duration it takes duration * number, number * duration.")]
        [InlineData("d: duration = 1h\nx = $d ** 2\necho $x\n", "'**' doesn't work on a duration ($d) and a number (2). Duration values have no '**'.")]
        [InlineData("x = 5 - 1h\necho $x\n", "'-' doesn't work on a number (5) and a duration (1h). With duration it takes duration - duration, date - duration.")]
        [InlineData("fn bump(b: lt_badge) {\n  return $b + 1\n}\necho @bump('x')\n", "'+' doesn't work on a lt_badge ($b) and a number (1). Lt_badge values have no '+'.")]
        public void AnOperatorANativeTypeDoesNotDeclareIsWarnedAbout(string text, string message)
        {
            Diagnostic d = Assert.Single(Check(text));
            Assert.Equal(DiagnosticCodes.TypeMismatch, d.Code);
            Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
            Assert.Equal(message, d.Message);
        }

        [Fact]
        public void ComparingATypeWithNoOrderIsWarnedAbout()
        {
            Assert.Equal("none", Badge.Ordering);
            Diagnostic d = Assert.Single(Check("fn rank(a: lt_badge, b: lt_badge) {\n  return $a < $b\n}\necho @rank('x', 'y')\n"));
            Assert.Equal(DiagnosticCodes.TypeMismatch, d.Code);
            Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
            Assert.Equal("Lt_badge values have no order, so '<' can't compare them. Compare a field, or the text with ->to_string().", d.Message);

            // A type with an order is fine, and == always is.
            Assert.Empty(Check("fn later(a: lt_phrase, b: lt_phrase) {\n  return $a >= $b\n}\necho @later('x', 'y')\n"));
            Assert.Empty(Check("fn same(a: lt_badge, b: lt_badge) {\n  return $a == $b\n}\necho @same('x', 'y')\n"));
        }

        [Fact]
        public void ALoopOverATypeThatCannotBeWalkedIsWarnedAbout()
        {
            Diagnostic d = Assert.Single(Check("d: duration = 1h\nfor x in $d {\n  echo $x\n}\n"));
            Assert.Equal(DiagnosticCodes.TypeMismatch, d.Code);
            Assert.EndsWith("can't loop over a duration: duration values can't be walked.", d.Message);
        }

        [Fact]
        public void ExistingArithmeticChecksStillApply()
        {
            Assert.Equal("'-' needs a number here, but 'a' is text.", Assert.Single(Check("x = 'a' - 1\necho $x\n")).Message);
            Assert.StartsWith("'+' joins two lists", Assert.Single(Check("list = [1, 2]\nx = $list + 3\necho $x\n")).Message);
        }

        // ---- Hover ----------------------------------------------------------------------------------------------

        private static HoverResult HoverAt(string textWithCaret)
        {
            int offset = textWithCaret.IndexOf('‸');
            string text = textWithCaret.Remove(offset, 1);
            return Service.GetHover(Service.Analyze(text, Path.Combine(Path.GetTempPath(), "native-ops.kscript")), offset);
        }

        [Fact]
        public void HoverOnANativeOperatorShowsItsSummaryAndResult()
        {
            HoverResult hover = HoverAt("x = 1h ‸+ 30m\necho $x\n");
            Assert.NotNull(hover);
            Assert.Contains("duration + duration: duration", hover.Markdown);
            Assert.Contains("The two lengths added: 1h + 30m is 1h30m.", hover.Markdown);

            hover = HoverAt("x = 1h ‸/ 15m\necho $x\n");
            Assert.Contains("duration / duration: number", hover.Markdown);

            hover = HoverAt("x = @now() ‸+ 1h\necho $x\n");
            Assert.Contains("date + duration: date", hover.Markdown);
        }

        [Fact]
        public void HoverOnOtherOperatorsIsUnchanged()
        {
            HoverResult hover = HoverAt("x = 7 ‸% 3\necho $x\n");
            Assert.Contains("Remainder", hover.Markdown);
        }

        // ---- Completion -----------------------------------------------------------------------------------------

        private static List<CompletionItem> Complete(string textWithCaret, string file = "native-ops.kscript")
        {
            int offset = textWithCaret.IndexOf('‸');
            string text = textWithCaret.Remove(offset, 1);
            return Service.GetCompletions(Service.Analyze(text, Path.Combine(Path.GetTempPath(), file)), offset);
        }

        [Fact]
        public void TheValueOfAVariableDeclaredWithANativeTypeOffersItsCompletions()
        {
            List<string> labels = Complete("wait: duration = ‸\n").Select(i => i.Label).ToList();
            Assert.Equal(NativeTypes.Get("duration").Completions, labels.Take(NativeTypes.Get("duration").Completions.Count));

            labels = Complete("<kscript><let name=\"wait\" type=\"duration\" value=\"‸\" /></kscript>", "native-ops.ks.xml").Select(i => i.Label).ToList();
            Assert.Contains("1h30m", labels);

            // Not after an operator, and not for a plain variable.
            Assert.DoesNotContain("1h30m", Complete("wait: duration = 1h + ‸\n").Select(i => i.Label));
            Assert.DoesNotContain("1h30m", Complete("wait = ‸\n").Select(i => i.Label));
        }

        [Fact]
        public void ANamedArgumentOfANativeTypeOffersItsCompletions()
        {
            List<string> labels = Complete("fn pause(for: duration) {\n  echo $for\n}\n@pause(for: ‸)\n").Select(i => i.Label).ToList();
            Assert.Contains("30s", labels);
            Assert.Contains("1w", labels);
        }

        // ---- Lists of one native type -----------------------------------------------------------------------

        private const string Versions = "v: semver[] = [1.0.0, 1.1.0-rc.1, 1.0.10]\n";

        [Theory]
        [InlineData("echo [1, 'two']->only(number)\n")]
        [InlineData("echo [1, 'two']->without(string)\n")]
        [InlineData("echo 1h->is_a(duration)\n")]
        [InlineData(Versions + "echo $v->only(releases)\n")]
        [InlineData(Versions + "echo $v->without(prereleases)->latest()\n")]
        public void AWordParameterTakesABareWord(string text)
        {
            Assert.Empty(Check(text));
        }

        [Theory]
        [InlineData(Versions + "echo $v\n")]
        [InlineData("names: domain[] = [example.com, api.example.co.uk]\necho $names\n")]
        [InlineData("<kscript><let name=\"v\" type=\"semver[]\" value=\"[1.0.0, 1.0.1-rc]\" /><echo>$v</echo></kscript>")]
        public void RawItemsInATypedListAreValues(string text)
        {
            string file = text.StartsWith('<') ? "native-ops.ks.xml" : "native-ops.kscript";
            Assert.DoesNotContain(Service.Analyze(text, Path.Combine(Path.GetTempPath(), file)).Diagnostics, d => d.Code != DiagnosticCodes.UnusedVariable);
        }

        [Fact]
        public void AListWithAVariableInItIsAnExpression()
        {
            Assert.Empty(Check("a: semver = 1.0.0\nv: semver[] = [$a, '2.0.0']\necho $v\n"));
            Assert.Contains(Check("v: semver[] = [$missing, '2.0.0']\necho $v\n"), d => d.Message.Contains("missing", StringComparison.Ordinal));

            // Raw and evaluated items together, as the runtime reads them.
            Assert.DoesNotContain(Analyze("a: semver = 1.0.0\nv: semver[] = [$a, 1.1.0-rc.1]\necho $v\n").Diagnostics, d => true);
            Assert.Contains(Check("v: semver[] = [1.0.0, $missing]\necho $v\n"), d => d.Message.Contains("missing", StringComparison.Ordinal));
        }

        [Fact]
        public void AListOfOneNativeTypeHasItsListMethods()
        {
            Assert.Equal("semver", ShapeOf(Versions + "x = $v->latest()\necho $x\n", "x").Type);
            Assert.Empty(Check("mixed = [3, 'hello']\necho $mixed->only(semver)->only(releases)->latest()\n"));
            Assert.Equal("string", ShapeOf(Versions + "x = $v->only(string)\necho $x\n", "x").Element?.Type);
            Assert.Equal("money", ShapeOf("m: money[] = ['GBP 9.50', 'GBP 0.50']\nx = $m->total()\necho $x\n", "x").Type);
            TypeShape kept = ShapeOf(Versions + "x = $v->only(releases)\necho $x\n", "x");
            Assert.Equal("semver", kept.Element?.Type);

            List<string> labels = Complete(Versions + "echo $v->‸\n").Select(i => i.Label).ToList();
            Assert.Contains("latest", labels);
            Assert.Contains("filter", labels); // and every list's methods
            Assert.DoesNotContain("total", labels);
            Assert.DoesNotContain("latest", Complete("n = [1, 2]\necho $n->‸\n").Select(i => i.Label));

            HoverResult hover = HoverAt(Versions + "echo $v->lat‸est()\n");
            Assert.Contains("highest release", hover.Markdown);
        }

        [Fact]
        public void APlainListDoesNotHaveATypedListsMethods()
        {
            Diagnostic d = Assert.Single(Check("n = [1, 2]\necho $n->latest()\n"));
            Assert.Equal("->latest() works on lists of semver values, but this value is a list.", d.Message);
        }

        [Fact]
        public void OnlyAndWithoutOfferTheItemsFiltersAndTypeNames()
        {
            List<CompletionItem> items = Complete(Versions + "echo $v->only(‸)\n");
            CompletionItem releases = Assert.Single(items, i => i.Label == "releases");
            Assert.Equal("Versions without a prerelease: 1.0.0 but not 1.1.0-rc.1.", releases.Documentation);
            Assert.Contains(items, i => i.Label == "prereleases");
            Assert.Contains(items, i => i.Label == "number");
            Assert.Contains(items, i => i.Label == "semver");
            Assert.Contains(Complete(Versions + "echo $v->without(‸)\n"), i => i.Label == "betas");

            // A plain list has no filters, only type names; is_a takes type names.
            List<string> plain = Complete("n = [1, 'a']\necho $n->only(‸)\n").Select(i => i.Label).ToList();
            Assert.DoesNotContain("releases", plain);
            Assert.Contains("string", plain);
            Assert.Contains("duration", Complete("echo 1h->is_a(‸)\n").Select(i => i.Label));
            Assert.Equal(new[] { "releases", "prereleases", "release_candidates", "betas", "alphas" }, NativeTypes.Get("semver").ListFilters.Select(f => f.Name));
        }
    }
}
