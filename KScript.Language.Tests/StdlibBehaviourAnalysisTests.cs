using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Syntax;

using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests
{
    /// <summary>
    /// What the editor and <c>kscript check</c> say about the conventions every standard package follows: fields are
    /// read with a dot, renamed methods say what to write, and a length of time is written with its unit (KS6018).
    /// </summary>
    public class StdlibBehaviourAnalysisTests
    {
        private static readonly LanguageService Service = new LanguageService();

        private static DocumentAnalysis Code(string code) => WithoutUnused(Analyze(code, "behaviour.kscript"));

        private static string Fixed(Diagnostic d, string code)
        {
            TextChange change = d.Fixes[0].Changes[0];
            return code.Substring(0, change.Span.Start) + change.NewText + code.Substring(change.Span.End);
        }

        [Fact]
        public void A_field_of_a_built_in_value_is_known_and_typed()
        {
            AssertClean(Code("d = '2026-11-19'->to_date()\ny: int = $d.year\necho $y\n"));
            AssertClean(Code("import KScript.IO\nf = @file('a.txt')\necho \"{ $f.name } { $f.size } { $f.exists }\"\n"));
        }

        [Fact]
        public void Calling_a_field_says_to_drop_the_brackets_and_fixes_it()
        {
            const string code = "d = '2026-11-19'->to_date()\necho $d->year()\n";
            Diagnostic d = AssertSingle(Code(code), DiagnosticCodes.UnknownMethod);
            Assert.Contains("year is a field of a date, not a method", d.Message);
            Assert.Equal("d = '2026-11-19'->to_date()\necho $d.year\n", Fixed(d, code));
        }

        [Fact]
        public void Reading_a_method_as_a_field_says_to_call_it()
        {
            const string code = "d = '2026-11-19'->to_date()\necho $d.format\n";
            Diagnostic d = AssertSingle(Code(code), DiagnosticCodes.UnknownField);
            Assert.Contains("format is a method of a date, not a field", d.Message);
            Assert.Equal("d = '2026-11-19'->to_date()\necho $d->format()\n", Fixed(d, code));
        }

        [Theory]
        [InlineData("import KScript.Cache\necho $cache->put('a', 1)\n", "->put() is no longer part of KScript. Use ->set(key, value).", "import KScript.Cache\necho $cache->set('a', 1)\n")]
        [InlineData("import KScript.Cache\necho $cache->forget('a')\n", "Use ->remove(key).", "import KScript.Cache\necho $cache->remove('a')\n")]
        [InlineData("import KScript.Cache\necho $cache->flush()\n", "Use ->clear().", "import KScript.Cache\necho $cache->clear()\n")]
        [InlineData("p: password = 'correct horse battery'\necho $p->strong()\n", "Use ->is_strong().", "p: password = 'correct horse battery'\necho $p->is_strong()\n")]
        [InlineData("a: ip = '10.0.0.1'\necho $a->private()\n", "Read the field .is_private", "a: ip = '10.0.0.1'\necho $a.is_private\n")]
        public void A_renamed_method_says_what_to_write_and_fixes_it(string code, string message, string fixedCode)
        {
            Diagnostic d = AssertSingle(Code(code), DiagnosticCodes.UnknownMethod);
            Assert.Contains(message, d.Message);
            Assert.Equal(fixedCode, Fixed(d, code));
        }

        [Fact]
        public void Fields_are_offered_after_a_dot()
        {
            (string text, int offset) = WithCaret("d = '2026-11-19'->to_date()\necho $d.‸\n");
            List<string> labels = Service.GetCompletions(Service.Analyze(text, "behaviour.kscript"), offset).Select(i => i.Label).ToList();
            Assert.Contains("year", labels);
            Assert.Contains("weekday", labels);
            Assert.DoesNotContain("format", labels);
        }

        [Fact]
        public void Hovering_a_field_shows_its_type_and_description()
        {
            (string text, int offset) = WithCaret("d = '2026-11-19'->to_date()\necho $d.ye‸ar\n");
            HoverResult hover = Service.GetHover(Service.Analyze(text, "behaviour.kscript"), offset);
            Assert.NotNull(hover);
            Assert.Contains("date.year", hover.Markdown);
        }

        [Theory]
        // An element's argument
        [InlineData("wait(duration: 2)\n", "wait(duration: 2s)\n")]
        [InlineData("wait(duration: 0.5)\n", "wait(duration: 0.5s)\n")]
        [InlineData("wait(duration: '500ms')\n", "wait(duration: 500ms)\n")]
        [InlineData("wait(duration: \"1m30s\")\n", "wait(duration: 1m30s)\n")]
        // A command's or a method's
        [InlineData("import KScript.Cache\n$cache->set('a', 1, ttl: 90)\n", "import KScript.Cache\n$cache->set('a', 1, ttl: 90s)\n")]
        [InlineData("import KScript.Cache\n$cache->set('a', 1, ttl: '10m')\n", "import KScript.Cache\n$cache->set('a', 1, ttl: 10m)\n")]
        public void KS6018_a_length_of_time_is_written_with_its_unit(string code, string fixedCode)
        {
            Diagnostic d = AssertSingle(Code(code), DiagnosticCodes.DurationUnit);
            Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
            Assert.Contains("with its unit and no quotes", d.Message);
            Assert.Equal(fixedCode, Fixed(d, code));
            Assert.Contains(DiagnosticCodes.DurationUnit, Fixer.Rules);
            Assert.Equal(fixedCode, Fixer.Apply(code, "behaviour.kscript").Text);
        }

        [Theory]
        [InlineData("wait(duration: 2s)\n")]
        [InlineData("wait(duration: 1h30m)\n")]
        [InlineData("wait(duration: 0)\n")]
        [InlineData("pause = 2s\nwait(duration: $pause)\n")]
        [InlineData("import KScript.Cache\n$cache->set('a', 1, ttl: 10m)\n")]
        [InlineData("import KScript.Cache\nlimit = 90\n$cache->set('a', 1, ttl: $limit)\n")]
        public void KS6018_leaves_durations_and_values_worked_out_alone(string code) => AssertClean(Code(code));

        [Fact]
        public void A_duration_literal_is_a_duration()
        {
            AssertClean(Code("d: duration = 1h30m\necho $d.minutes\n"));
        }
    }
}
