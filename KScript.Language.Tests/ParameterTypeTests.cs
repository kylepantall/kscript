using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Syntax;

namespace KScript.Language.Tests
{
    /// <summary>KS6007 and the parameter types worked out from calls: untyped parameters get a hint, a warning and a quick fix.</summary>
    public class ParameterTypeTests
    {
        private static readonly LanguageService Service = new LanguageService(files: EmptyFileSystem.Instance);

        private static DocumentAnalysis Analyze(string text, string file = "a.kscript") => Service.Analyze(text, Path.Combine(Path.GetTempPath(), file));

        private static List<Diagnostic> Warnings(string text, string file = "a.kscript") =>
            Analyze(text, file).Diagnostics.Where(d => d.Code == DiagnosticCodes.MissingParameterType).ToList();

        [Fact]
        public void CallsGiveUntypedParametersTheirTypes()
        {
            string text = "fn cached(key, compute) => $compute($key)\nx = @cached('a', fn(k) => 1)\ny = @cached('b', fn(k) => 2)\necho $x + $y\n";
            DocumentAnalysis a = Analyze(text);
            Symbol key = a.Symbols.Single(s => s.Name == "key");
            Assert.True(key.TypeFromCalls);
            Assert.Equal(KType.String, key.Shape.Type);
            Assert.Equal(KType.Fn, a.Symbols.Single(s => s.Name == "compute").Shape.Type);

            List<InlayHint> hints = Service.GetInlayHints(a).Where(h => h.Label.StartsWith(": ", StringComparison.Ordinal)).ToList();
            Assert.Equal(new[] { ": string", ": fn" }, hints.Take(2).Select(h => h.Label));
            Assert.EndsWith("What every call in this file passes.", hints[0].Tooltip);

            List<Diagnostic> warnings = a.Diagnostics.Where(d => d.Code == DiagnosticCodes.MissingParameterType).ToList();
            Assert.Equal("Give key a type: every call passes a string, so write key: string.", warnings[0].Message);
            Assert.Equal("Give compute a type: every call passes a function, so write compute: fn.", warnings[1].Message);
            Assert.Equal(DiagnosticSeverity.Warning, warnings[0].Severity);
            string fixedText = AnalyzerTests.Apply(text, warnings[0].Fixes[0].Changes);
            fixedText = AnalyzerTests.Apply(fixedText, Warnings(fixedText)[0].Fixes[0].Changes);
            Assert.StartsWith("fn cached(key: string, compute: fn) =>", fixedText);
            Assert.Empty(Warnings(fixedText));
        }

        [Fact]
        public void PipesNamedArgumentsAndListsCount()
        {
            string text = "fn total(items, scale) => $items->sum() * $scale\nfn label(n) => 'n' + $n\n"
                + "a = [1, 2] |> total(scale: 2)\nb = @total([3], 1)\nc = 4 |> @label()\necho $a + $b + $c\n";
            List<Diagnostic> warnings = Warnings(text);
            Assert.Equal(new[] { "number[]", "number", "number" }, warnings.Select(w => w.Fixes[0].Changes[0].NewText.Substring(2)));
            Assert.Equal("Give items a type: every call passes a list, so write items: number[].", warnings[0].Message);
        }

        [Fact]
        public void DisagreeingOrUnknownCallsGiveNoType()
        {
            // Different types, an unknown value, a different list or map: no type is claimed and the fix isn't offered.
            string text = "fn f(a, b, c, d) => $a\nimport KScript.IO\n"
                + "@f(1, $nope, [1], { x: 1 })\n@f('x', 2, ['a'], { y: 'b' })\n";
            DocumentAnalysis a = Analyze(text);
            Symbol first = a.Symbols.Single(s => s.Name == "a" && s.Kind == SymbolKind.Parameter);
            Assert.False(first.TypeFromCalls);
            List<Diagnostic> warnings = Warnings(text);
            Assert.Equal("Give a a type, such as a: string or a: map, so callers and the editor know what it takes.", warnings[0].Message);
            Assert.Empty(warnings[0].Fixes);
            Assert.Empty(warnings[1].Fixes);
            Assert.Equal(": list", warnings[2].Fixes[0].Changes[0].NewText);
            Assert.Equal(": map", warnings[3].Fixes[0].Changes[0].NewText);
        }

        [Fact]
        public void RecordsAndListsOfThemAreWrittenByName()
        {
            string text = "type Task {\n  title: string\n}\nfn one(t) => $t.title\nfn all(ts) => $ts->count()\n"
                + "t = Task({ title: 'a' })\necho @one($t)\necho @all([$t])\n";
            Assert.Equal(new[] { ": Task", ": map[]" }, Warnings(text).Select(w => w.Fixes[0].Changes[0].NewText));
            Assert.Equal(": list", Warnings("fn f(grid) => $grid\necho @f([[1]])\n")[0].Fixes[0].Changes[0].NewText);
            Assert.Empty(Warnings("type Task {\n  title: string\n}\nfn f(t) => $t\necho @f(Task)\n")[0].Fixes);
        }

        [Fact]
        public void TypedDefaultedAndJobParametersAreLeftAlone()
        {
            Assert.Empty(Warnings("fn f(a: int, b = 2, c: string = 'x') => $a + $b\necho @f(1)\n"));
            Assert.Empty(Warnings("import KScript.Queue\njob Send {\n  to: string\n  fn handle() {\n  }\n  fn failed(err) {\n    echo $err.message\n  }\n}\n"));
            Assert.Empty(Warnings("echo [1]->map(fn(x) => $x * 2)\n"));
            // XML scripts are checked the same way.
            Assert.Single(Warnings("<kscript><fn name=\"f\" params=\"x\" value=\"$x\" /><echo>@f(1)</echo></kscript>", "a.ks.xml"));
            Assert.Equal(": number", Warnings("<kscript><fn name=\"f\" params=\"x\" value=\"$x\" /><echo>@f(1)</echo></kscript>", "a.ks.xml")[0].Fixes[0].Changes[0].NewText);
        }

        [Fact]
        public void ProjectsCanTurnItOff()
        {
            Assert.Equal("warning", KScript.Projects.ProjectConfig.Recommended.SeverityFor(DiagnosticCodes.MissingParameterType));
        }
    }
}
