using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Syntax;

using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests
{
    /// <summary>The editor side of KScript.Auth's Ownable mixin (#208), @guest/@owner (#209) and ->dispatch(delay:, at:).</summary>
    public class OwnableAnalysisTests
    {
        private static readonly string Folder = Path.Combine(Path.GetTempPath(), "kscript-ownable");

        private const string Note = """
            import KScript.Data
            import KScript.Auth

            model Note(timestamps) extends Ownable {
              title: string = ''
            }

            """;

        private static DocumentAnalysis Check(string text) =>
            Analyzer.Analyze(text, Path.Combine(Folder, "main.kscript"), EmptyFileSystem.Instance);

        [Theory]
        [InlineData("echo Note->mine()->count()")]
        [InlineData("echo Note->mine(as: 'user:1')->first().owner")]
        [InlineData("echo Note->where(title: 'a')->mine()->pluck(title)")]
        [InlineData("echo Note->first()->is_mine()")]
        [InlineData("echo Note->create(title: 'a', owner: @owner(as: 3)).owner")]
        [InlineData("echo @guest() ?? 'none'")]
        [InlineData("echo @acting_as_guest()")]
        public void OwnableComesWithKScriptAuth(string call)
        {
            // The mixin is a KScript script inside the package: its field, scope and method are known wherever
            // KScript.Auth is imported, and extending it counts as using the package.
            AssertClean(Check(Note + "$db->create_table(Note)\n" + call + "\n"));
        }

        [Fact]
        public void OwnableIsTypedFromItsSource()
        {
            var analysis = Check(Note + "x = Note->first().owner\ny = @owner()\nz = @guest()\necho \"{ $x } { $y } { $z }\"\n");
            AssertClean(analysis);
            var unknown = Check(Note + "echo Note->first().ownr\n");
            Assert.Contains(unknown.Diagnostics, d => d.Message.Contains("ownr", StringComparison.Ordinal));
            var args = Check(Note + "echo Note->mine(1, 2)->count()\n");
            Assert.Contains(args.Diagnostics, d => d.Code == DiagnosticCodes.ArgumentCount);
        }

        [Fact]
        public void ExtendingOwnableWithoutTheImportOffersIt()
        {
            const string text = "import KScript.Data\n\nmodel Note extends Ownable {\n  title: string = ''\n}\n";
            var d = Assert.Single(Check(text).Diagnostics, x => x.Message.Contains("Ownable comes with KScript.Auth", StringComparison.Ordinal));
            Assert.Equal(DiagnosticSeverity.Error, d.Severity);
            QuickFix fix = Assert.Single(d.Fixes);
            Assert.Equal("Import KScript.Auth", fix.Title);
            string fixedText = text;
            foreach (var change in fix.Changes.OrderByDescending(c => c.Span.Start))
                fixedText = fixedText.Substring(0, change.Span.Start) + change.NewText + fixedText.Substring(change.Span.End);
            Assert.Contains("import KScript.Auth", fixedText);
            Assert.DoesNotContain(Check(fixedText).Diagnostics, x => x.Message.Contains("Ownable", StringComparison.Ordinal));
        }

        [Fact]
        public void DispatchTakesADelayOrATimeNotBoth()
        {
            const string job = "import KScript.Queue\n\njob Ping {\n  n: int\n\n  fn handle() {\n    echo $n\n  }\n}\n\n";
            AssertClean(WithoutUnused(Check(job + "Ping(n: 1)->dispatch(delay: 5m)\nPing(n: 2)->dispatch(at: @now())\n")));
            var both = WithoutUnused(Check(job + "Ping(n: 1)->dispatch(delay: 5m, at: '2026-12-01 09:00')\n"));
            var d = Assert.Single(both.Diagnostics);
            Assert.Equal(DiagnosticSeverity.Error, d.Severity);
            Assert.Equal("->dispatch() takes delay: or at:, not both.", d.Message);
            Assert.Equal("at", (job + "Ping(n: 1)->dispatch(delay: 5m, at: '2026-12-01 09:00')\n").Substring(d.Span.Start, d.Span.Length));
        }
    }
}
