using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Syntax;
using Xunit;

namespace KScript.Language.Tests
{
    /// <summary>Values of the wrong kind, caught before the script runs: misspelt fields, arithmetic on text, text where a number is needed.</summary>
    public class KindCheckTests
    {
        private static readonly LanguageService Service = new LanguageService();

        private static List<Diagnostic> Check(string text, string file = "a.kscript") =>
            Service.Analyze(text, Path.Combine(Path.GetTempPath(), file)).Diagnostics.Where(d => d.Code != DiagnosticCodes.UnusedVariable && d.Code != DiagnosticCodes.MissingParameterType).ToList();

        private static Diagnostic One(string text, string file = "a.kscript") => Assert.Single(Check(text, file));

        [Fact]
        public void AMisspeltFieldOfAMapIsWarnedAbout()
        {
            const string text = "person = { name: 'Joan', age: 82 }\necho $person.nmae\n";
            Diagnostic d = One(text);
            Assert.Equal(DiagnosticCodes.UnknownField, d.Code);
            Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
            Assert.Equal("This map has no field 'nmae', so this reads as nothing. Did you mean name?", d.Message);
            Assert.Equal("person = { name: 'Joan', age: 82 }\necho $person.name\n", AnalyzerTests.Apply(text, d.Fixes[0].Changes));
        }

        [Fact]
        public void TheItemsOfAListOfMapsAreCheckedToo()
        {
            Diagnostic d = One("people = [{ name: 'Joan' }, { name: 'Sam' }]\nfor p in $people {\n  echo $p.nam\n}\n");
            Assert.Contains("Did you mean name?", d.Message);
            Assert.Contains("Did you mean name?", One("<kscript><let name=\"p\" value=\"{ name: 'a' }\" /><echo>{ $p.nmae }</echo></kscript>", "a.ks.xml").Message);
        }

        [Theory]
        [InlineData("person = { name: 'Joan', age: 82 }\necho $person.email ?? 'none'\n")] // not close to a field: may be optional
        [InlineData("point = { x: 1, y: 2 }\necho $point.z\n")] // short names are close without being typos
        [InlineData("person = { name: 'Joan' }\nperson.nmae = 'x'\necho $person.nmae\n")] // the field is given a value
        [InlineData("person = { name: 'Joan' }\necho $person.nmae\nperson['nmae'] = 'x'\n")]
        [InlineData("person = { name: 'Joan' }\necho $person.name->count()\nlist = [1]\necho $list->count()\n")]
        [InlineData("data = '{}'->json()\necho $data.nmae\n")]
        public void FieldsThatMayBeThereAreLeftAlone(string text)
        {
            Assert.DoesNotContain(Check(text), d => d.Code == DiagnosticCodes.UnknownField);
        }

        [Fact]
        public void AFieldOfTextOrANumberPointsAtTheMethod()
        {
            const string text = "name = 'Joan'\necho $name.upper\n";
            Diagnostic d = One(text);
            Assert.Equal(DiagnosticCodes.UnknownField, d.Code);
            Assert.Equal("Text has no fields, so .upper stops the script. To call the method, write ->upper().", d.Message);
            Assert.Equal("name = 'Joan'\necho $name->upper()\n", AnalyzerTests.Apply(text, d.Fixes[0].Changes));

            d = One("n = 5\necho $n.size\n");
            Assert.Equal("A number has no fields, so .size stops the script.", d.Message);
            Assert.Empty(d.Fixes);
            Assert.StartsWith("A true/false value has no fields", One("ok = true\necho $ok.value\n").Message);
        }

        [Theory]
        [InlineData("x = 'a' - 1\n", "'-' needs a number here, but 'a' is text.")]
        [InlineData("x = 10 / 'two'\n", "'/' needs a number here, but 'two' is text.")]
        [InlineData("name = 'Joan'\nx = $name % 2\n", "'%' needs a number here, but $name holds the text 'Joan'.")]
        [InlineData("x = 2 * 'ab'\n", "'*' needs a number here, but 'ab' is text. To repeat text, put the text first: 'ab' * 3.")]
        [InlineData("list = [1, 2]\nx = $list - 1\n", "'-' needs a number here, but $list is a list.")]
        [InlineData("x = { a: 1 } ** 2\n", "'**' needs a number here, but this is a map.")]
        [InlineData("x = [1, 2] + 3\n", "'+' joins two lists, but this is a number. To add an item to a list, use ->push(item).")]
        [InlineData("x = 3 + [1, 2]\n", "'+' joins two lists, but this is a number. To add an item to the start of a list, use ->prepend(item).")]
        public void ArithmeticThatCannotWorkIsWarnedAbout(string text, string message)
        {
            Diagnostic d = One(text);
            Assert.Equal(DiagnosticCodes.TypeMismatch, d.Code);
            Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
            Assert.Equal(message, d.Message);
        }

        [Fact]
        public void BothSidesAreReported()
        {
            Assert.Equal(2, Check("x = 'a' - 'b'\n").Count);
            Assert.Single(Check("x = 'a' * 'b'\n"));
        }

        [Theory]
        [InlineData("x = '5' - 1\n")] // text that reads as a number is converted
        [InlineData("x = 'ab' * 3\n")] // repeats the text
        [InlineData("name = 'Joan'\nx = $name * 2\n")]
        [InlineData("x = 'a' + 1\ny = 1 + 'a'\n")] // joins text
        [InlineData("x = [1] + [2]\ny = { a: 1 } + { b: 2 }\nz = [1] + 'a'\n")]
        [InlineData("name = 'Joan'\nname = 5\nx = $name - 1\n")] // no longer the text it started as
        [InlineData("name = 'Joan'\nx = $name - 1\nname = 5\n")]
        [InlineData("x = @now() - @now()\n")]
        [InlineData("fn half(n) {\n  return $n / 2\n}\necho @half(4)\n")]
        [InlineData("n = 3\nx = \"{$n}\" - 1\n")] // filled-in text may be a number
        public void ArithmeticThatMayWorkIsLeftAlone(string text)
        {
            Assert.Empty(Check(text));
        }

        [Theory]
        [InlineData("fn double(n: int) {\n  return $n * 2\n}\necho @double('five')\n", "The 'n' argument needs a number, but 'five' is text.")]
        [InlineData("fn double(n: int) {\n  return $n * 2\n}\necho @double(n: [1])\n", "The 'n' argument needs a number, but this is a list.")]
        [InlineData("fn half(n: float) {\n  return $n / 2\n}\nname = 'Joan'\necho @half($name)\n", "The 'n' argument needs a number, but $name holds the text 'Joan'.")]
        [InlineData("echo 'ab'->repeat('x')\n", "The 'count' argument needs a number, but 'x' is text.")]
        [InlineData("echo 'ab'->pad_left('x')\n", "The 'width' argument needs a number, but 'x' is text.")]
        [InlineData("echo [1, 2, 3]->take('two')\n", "The 'count' argument needs a number, but 'two' is text.")]
        [InlineData("echo 5->clamp(1, 'ten')\n", "The 'max' argument needs a number, but 'ten' is text.")]
        [InlineData("echo 'abc'->slice(1, end: 'z')\n", "The 'end' argument needs a number, but 'z' is text.")]
        public void TextWhereANumberIsNeededIsWarnedAbout(string text, string message)
        {
            Diagnostic d = One(text);
            Assert.Equal(DiagnosticCodes.TypeMismatch, d.Code);
            Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
            Assert.Equal(message, d.Message);
        }

        [Theory]
        [InlineData("fn double(n: int) {\n  return $n * 2\n}\necho @double('5') @double(5)\n")]
        [InlineData("fn show(n) {\n  return $n\n}\necho @show('five')\n")]
        [InlineData("fn show(n: string) {\n  return $n\n}\necho @show('five')\n")]
        [InlineData("echo 'ab'->repeat('3') 'ab'->repeat(3) 'ab'->pad_left(5, '.')\n")]
        [InlineData("w = '8'\necho 'ab'->pad_left($w)\n")]
        public void ArgumentsThatMayWorkAreLeftAlone(string text)
        {
            Assert.DoesNotContain(Check(text), d => d.Code == DiagnosticCodes.TypeMismatch);
        }
    }
}
