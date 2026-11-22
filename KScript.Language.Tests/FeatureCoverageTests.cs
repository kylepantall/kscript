using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Syntax.Text;

using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests
{
    /// <summary>Editor features in the less common places: pipes, bare words, imports, odd cursors.</summary>
    public class FeatureCoverageTests
    {
        private static readonly LanguageService Service = new LanguageService(files: new AnalyzerCoverageTests.Files());

        private static (DocumentAnalysis Analysis, int Offset) At(string textWithCaret, string file = "test.ks.xml")
        {
            var (text, offset) = WithCaret(textWithCaret);
            if (file == null || !file.EndsWith(".html", StringComparison.Ordinal))
            {
                text = Script(text);
                offset += "<kscript>\n".Length;
            }
            return (Service.Analyze(text, file == null ? null : Path.Combine(AnalyzerCoverageTests.Folder, file)), offset);
        }

        private static List<string> Labels(string textWithCaret, string file = "test.ks.xml")
        {
            var (a, offset) = At(textWithCaret, file);
            return Service.GetCompletions(a, offset).Select(c => c.Label).ToList();
        }

        private static string Hover(string textWithCaret, string file = "test.ks.xml")
        {
            var (a, offset) = At(textWithCaret, file);
            return Service.GetHover(a, offset)?.Markdown;
        }

        [Fact]
        public void Completion_in_odd_places()
        {
            Assert.DoesNotContain("elif", Labels("<if test=\"true\"></if>text <‸"));
            Assert.Empty(Labels("<on event=\"click\" target=\"‸\"></on>", "page.html"));
            Assert.NotEmpty(Labels("<let name=\"total\" value=\"1\" /><echo>$‸</echo>"));
            Assert.Contains("to_upper", Labels("<echo>@‸</echo>"));
            Assert.Empty(Labels("<import path=\"‸\" />", null));
            Assert.Empty(Labels("<echo>{ foo.‸ }</echo>"));
            Assert.DoesNotContain("elif", Labels("<if test=\"true\"></if>text <el‸"));
            Assert.NotEmpty(Labels("<let name=\"total\" value=\"1\" /><echo>Hi $to‸</echo>"));
            Assert.Contains("to_upper", Labels("<echo>Hi @to‸</echo>"));
            Assert.Contains("total", Labels("<let name=\"total\" value=\"1\" /><echo>{1} $to‸</echo>"));
            Assert.Contains("total", Labels("<let name=\"total\" value=\"1\" /><echo>{1} $‸</echo>"));
            Assert.Contains("to_upper", Labels("<echo>{1} @to‸</echo>"));
            Assert.Null(Service.ComponentFile(null, "x"));
            Assert.Empty(Service.ComponentProps(Path.Combine(AnalyzerCoverageTests.Folder, "missing.html")));
            Assert.Equal("Function ok", Service.GetCompletions(At("<import path=\"nameless.ks.xml\" as=\"lib\" /><echo>{ lib.‸ }</echo>").Analysis, At("<import path=\"nameless.ks.xml\" as=\"lib\" /><echo>{ lib.‸ }</echo>").Offset).First(c => c.Label == "ok").ToString());
        }

        [Fact]
        public void Hover_in_pipes_retired_attributes_and_operators()
        {
            Assert.Contains("CSS selector", Hover("<on event=\"click\" tar‸get=\"#x\"></on>", "page.html"));
            Assert.Contains("was removed in KScript 2", Hover("<if condit‸ion=\"x\"></if>"));
            Assert.Contains("upper", Hover("<echo>{ 'a' |> upp‸er() }</echo>"));
            Assert.Contains("@f", Hover("<fn name=\"f\" params=\"x\" value=\"$x\" /><echo>{ 1 |> ‸f() }</echo>"));
            Assert.Contains("@double", Hover("<fn name=\"double\" params=\"x\" value=\"$x * 2\" /><echo>{ 1 |> dou‸ble() }</echo>"));
            Assert.Contains("@double", Hover("<fn name=\"double\" params=\"x\" value=\"$x * 2\" /><echo>{ 1->dou‸ble() }</echo>"));
            Assert.Null(Hover("<echo>{ 'a' |> upp‸er }</echo>"));
            Assert.Null(Hover("<let name=\"x\" value=\"@nothing()\" /><let name=\"y\" value=\"$x\" /><echo>{ $x ‸+ $y }</echo>"));
            Assert.Null(Hover("<import package=\"KScript.Math\" /><echo>{ @round(1.5, xy‸z) }</echo>"));
            Assert.Null(Hover("<import package=\"KScript.Math\" /><echo>{ @round(1.5, no‸pe: 1) }</echo>"));
        }

        [Fact]
        public void Inlay_hints_skip_what_is_already_written()
        {
            var (a, _) = At("<fn name=\"f\"><return value=\"1\" /></fn><for each=\"x\" at=\"i\" in=\"[1]\"><echo>{$i}</echo></for><let>y = 3</let><echo>{$y}</echo>‸");
            List<InlayHint> hints = Service.GetInlayHints(a);
            InlayHint fn = Assert.Single(hints, h => h.Label == " → number");
            Assert.Contains("returns", fn.Tooltip);
            Assert.EndsWith(" → number", fn.ToString());
            Assert.Equal(new[] { " → number", ": number" }, hints.Select(h => h.Label)); // only $x, the loop variable

            var (values, _) = At("<let name=\"n\" value=\"3\" /><let name=\"m\" value=\"$n + 1\" /><echo>{$n} {$m}</echo>‸");
            Assert.Equal(new[] { ": number" }, Service.GetInlayHints(values).Select(h => h.Label));
        }

        [Fact]
        public void Signature_help_definitions_and_renames()
        {
            var (pipe, at) = At("<fn name=\"f\" params=\"x, y\" value=\"1\" /><echo>{ 1 |> f(‸) }</echo>");
            Assert.Contains(Service.GetSignatureHelp(pipe, at).Signatures, s => s.Label.Contains("@f"));
            var (named, at2) = At("<import package=\"KScript.Math\" /><echo>{ @round(1, nope: ‸) }</echo>");
            Assert.NotNull(Service.GetSignatureHelp(named, at2));

            var (import, at3) = At("<import path=\"nameless.ks.xml\" as=\"lib\" /><echo>{ lib.zz‸z() }</echo>");
            SourceLocation file = Assert.Single(Service.GetDefinition(import, at3));
            Assert.True(file.HasLinePositions);
            Assert.Equal(new LinePosition(0, 0), file.Start);
            Assert.Equal(new LinePosition(0, 0), file.End);

            var (plain, at4) = At("<echo>hello‸</echo>");
            Assert.Equal("Nothing to rename here.", Service.Rename(plain, at4, "x").Error);
            var (same, at5) = At("<let name=\"total\" value=\"1\" /><echo>{ $to‸tal }</echo>");
            RenameResult unchanged = Service.Rename(same, at5, "total");
            Assert.Null(unchanged.Error);
            Assert.Empty(unchanged.Changes);
        }

        [Fact]
        public void Blocks_print_their_labels()
        {
            var (a, _) = At("<let name=\"x\" value=\"1\" />\n\n<echo>{$x}</echo>‸");
            foreach (CodeBlock block in Service.GetBlocks(a)) Assert.Equal(block.Label, block.ToString());
        }
    }
}
