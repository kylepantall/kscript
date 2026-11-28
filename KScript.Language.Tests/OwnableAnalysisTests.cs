using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Syntax;

using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests
{
    /// <summary>The editor side of KScript.Auth's Ownable mixin (#208), its user and guest relations, @guest (#209) and ->dispatch(delay:, at:).</summary>
    public class OwnableAnalysisTests
    {
        private static readonly string Folder = Path.Combine(Path.GetTempPath(), "kscript-ownable");

        private const string Note = """
            import KScript.Data
            import KScript.Auth

            model Note extends [Ownable, Timestamps] {
              title: string = ''
            }

            """;

        private static DocumentAnalysis Check(string text) =>
            Analyzer.Analyze(text, Path.Combine(Folder, "main.kscript"), EmptyFileSystem.Instance);

        [Theory]
        [InlineData("echo Note->mine()->count()")]
        [InlineData("echo Note->mine(as: 1)->first().user.email")]
        [InlineData("echo Note->first().user_id ?? Note->first().guest_id")]
        [InlineData("echo Note->where(guest: @guest())->count()")]
        [InlineData("echo Note->where(title: 'a')->mine()->pluck(title)")]
        [InlineData("echo Note->first()->is_mine()")]
        [InlineData("echo Note->create(title: 'a', user: 3).user.name")]
        [InlineData("echo Note->first().guest?.id")]
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
            var analysis = Check(Note + "x = Note->first().user\ny = Note->first().guest\nz = @guest()\necho \"{ $x.email } { $y.id } { $z }\"\n");
            AssertClean(analysis);
            var unknown = Check(Note + "echo Note->first().ownr\n");
            Assert.Contains(unknown.Diagnostics, d => d.Message.Contains("ownr", StringComparison.Ordinal));
            var args = Check(Note + "echo Note->mine(1, 2)->count()\n");
            Assert.Contains(args.Diagnostics, d => d.Code == DiagnosticCodes.ArgumentCount);
        }

        [Fact]
        public void A_project_copy_of_User_replaces_KScript_Auths()
        {
            // kscript publish User, then a uuid key and a handle: Ownable's user follows the copy.
            string root = Path.Combine(Path.GetTempPath(), "kscript-ownable-copy-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "models"));
            try
            {
                File.WriteAllText(Path.Combine(root, "kscript.json"), "{ \"name\": \"copy\" }");
                File.WriteAllText(Path.Combine(root, "models", "user.kscript"),
                    "import KScript.Data\n\nmodel User(key: 'uid') {\n  uid: uuid = @uuid()\n  handle: string\n  email: email\n  password: password\n}\n");
                File.WriteAllText(Path.Combine(root, "models", "ownable.kscript"),
                    "import KScript.Data\nimport KScript.Auth\n\nmodel Ownable(mixin) {\n  user?: User\n  guest?: Guest\n\n  fn shout() => 'hi'\n}\n");
                string main = Path.Combine(root, "main.kscript");
                DocumentAnalysis Run(string call) => Analyzer.Analyze(Note + call + "\n", main, DiskFileSystem.Instance);
                AssertClean(Run("echo Note->first().user.handle"));
                AssertClean(Run("echo Note->first().user_uid"));
                AssertClean(Run("echo Note->first()->shout()"));
                Assert.Contains(Run("echo Note->first().user.name").Diagnostics, d => d.Message.Contains("'name'", StringComparison.Ordinal));
                // The copy itself knows User and Guest.
                AssertClean(WithoutUnused(Analyzer.Analyze(File.ReadAllText(Path.Combine(root, "models", "ownable.kscript")), Path.Combine(root, "models", "ownable.kscript"), DiskFileSystem.Instance)));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
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
