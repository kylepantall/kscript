using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Syntax;

using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests
{
    /// <summary>Issues #207 and #210, and the editor gaps found while writing StickyBoard's sticky note component.</summary>
    public class BoardIssueBatchTests
    {
        private static readonly string Folder = Path.Combine(Path.GetTempPath(), "kscript-board-batch");

        private const string Boards = """
            import KScript.Data

            /// Biggest upload, in bytes.
            const max_file_size = 5000000
            const max_files = 20

            /// Finds one of the owner's notes.
            fn find_note(owner: string, id: int) => { id: $id, owner: $owner }

            fn move(owner: string, id: int, x: number = 0) {
              echo $x
            }
            """;

        private static MemoryFiles Files() => new MemoryFiles()
            .Add(Path.Combine(Folder, "lib", "boards.kscript"), Boards);

        private static readonly LanguageService Service = new LanguageService(files: Files());

        private static List<CompletionItem> Complete(string textWithCaret, string file = "components/sticky-note.kscript")
        {
            (string text, int offset) = WithCaret(textWithCaret);
            return Service.GetCompletions(Service.Analyze(text, Path.Combine(Folder, file)), offset);
        }

        private const string Component = """
            import KScript.Web
            import "../lib/boards.kscript" as boards

            type StickyNote extends Component {
              construct({ owner: string, note_id: int })

              fn move(x: number) {
                note = @boards.find_note($owner, $note_id)
                @boards.move($owner, $note_id, x: $x)
                CARET
              }

              fn render(): Component => ```html
                <p>{ $owner }</p>
                ```
            }
            """;

        [Theory]
        [InlineData("@boa‸")]
        [InlineData("@‸")]
        [InlineData("@b‸")]
        public void ImportAliasCompletesAtAStatementInAComponentMethod(string typed)
        {
            var items = Complete(Component.Replace("CARET", typed, StringComparison.Ordinal));
            Assert.True(items.Any(i => i.Label == "boards"), string.Join(",", items.Select(i => i.Label).Take(40)));
        }

        [Theory]
        [InlineData("import \"lib/boards.kscript\" as boards\n@boa‸\n", "main.kscript")]
        [InlineData("import \"lib/boards.kscript\" as boards\nfn go() {\n  @boa‸\n}\n", "main.kscript")]
        [InlineData("import \"lib/boards.kscript\" as boards\nx = @boa‸\n", "main.kscript")]
        public void ImportAliasCompletesInScripts(string text, string file)
        {
            var items = Complete(text, file);
            Assert.True(items.Any(i => i.Label == "boards"), string.Join(",", items.Select(i => i.Label).Take(40)));
        }

        [Theory]
        [InlineData("@boards.‸")]
        [InlineData("@boards.fi‸")]
        public void ImportMembersCompleteAtAStatementInAComponentMethod(string typed)
        {
            var items = Complete(Component.Replace("CARET", typed, StringComparison.Ordinal));
            Assert.Contains(items, i => i.Label == "find_note");
        }
    
        [Theory]
        [InlineData("size = @boards.max_fi‸les\necho $size\n")]
        [InlineData("fn go() {\n  @boards.max_fi‸les\n}\n@go()\n")]
        public void HoverOnAnImportedConstantNamesItsSourceOnce(string body)
        {
            (string text, int offset) = WithCaret("import \"lib/boards.kscript\" as boards\n" + body);
            var hover = Service.GetHover(Service.Analyze(text, Path.Combine(Folder, "main.kscript")), offset);
            Assert.Equal("```kscript\nconst boards.max_files: number = 20\n```\n\nImported as boards from lib/boards.kscript", hover.Markdown);
        }

        [Fact]
        public void HoverOnAnImportedConstantShowsItsDocComment()
        {
            (string text, int offset) = WithCaret("import \"lib/boards.kscript\" as boards\nsize = @boards.max_file‸_size\necho $size\n");
            var hover = Service.GetHover(Service.Analyze(text, Path.Combine(Folder, "main.kscript")), offset);
            Assert.Equal("```kscript\nconst boards.max_file_size: number = 5000000\n```\n\nBiggest upload, in bytes.\n\nImported as boards from lib/boards.kscript", hover.Markdown);
        }
    
        [Theory]
        [InlineData("@boards.mo‸ve('a', 1)\n", "x: number = 0)\n```\n\nImported as boards from lib/boards.kscript")]
        [InlineData("note = @boards.find_n‸ote('a', 1)\necho $note\n", "Finds one of the owner's notes.\n\nImported as boards from lib/boards.kscript")]
        public void HoverOnAnImportedFunctionNamesItsSourceOnce(string body, string ending)
        {
            (string text, int offset) = WithCaret("import \"lib/boards.kscript\" as boards\n" + body);
            var hover = Service.GetHover(Service.Analyze(text, Path.Combine(Folder, "main.kscript")), offset);
            Assert.EndsWith(ending, hover.Markdown);
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(hover.Markdown, "boards\\.kscript"));
        }
    
        private const string Article = """
            import KScript.Data

            model Article {
              title: string
              published: bool = false

              fn live(query) => $query->where(published: true)
              fn unused(query) => $query->where(published: false)
            }
            """;

        [Theory]
        [InlineData("echo Article->live()->count()")]
        [InlineData("echo Article->with_trashed()->live()->count()")]
        [InlineData("echo Article->where(title: 'a')->live()->count()")]
        public void AScopeCalledFromAnotherFileIsUsed(string call)
        {
            // #207: a scope called on the model, or on a query of it, in a script that imports the model.
            string folder = Path.Combine(Folder, "scopes");
            string path = Path.Combine(folder, "models", "article.kscript");
            var files = new MemoryFiles()
                .Add(Path.Combine(folder, "kscript.json"), """{ "name": "scope-repro", "packages": ["KScript.Data"] }""")
                .Add(path, Article)
                .Add(Path.Combine(folder, "main.kscript"), "import KScript.Data\nimport \"models/article.kscript\"\n\n" + call + "\n");
            ProjectUsage.ClearCache();
            var unused = Analyzer.Analyze(Article, path, files).Diagnostics.Where(d => d.Code == DiagnosticCodes.UnusedFunction).ToList();
            Assert.Equal("The function unused is never called. ../main.kscript imports this script but doesn't call it.", Assert.Single(unused).Message);
        }

        private const string Mixin = """
            import KScript.Data

            model Owned(mixin, index: [owner]) {
              owner: string(min: 1, max: 80)

              fn owned_by(query, owner: string) => $query->where(owner: $owner)
            }

            model Item(soft_deletes) extends Owned {
              title: string = ''
            }

            $db->create_table(Item)
            Item->create(owner: 'a', title: 'one')
            CALL
            """;

        [Theory]
        [InlineData("echo Item->owned_by('a')->count()")]
        [InlineData("echo Item->with_trashed()->owned_by('b')->first().title")]
        [InlineData("echo Item->where(title: 'one')->owned_by('a')->count()")]
        public void AScopeFromAMixinIsKnownOnTheModelAndItsQueries(string call)
        {
            // #210: KS5003 Unknown method, and KS6002 never called, for a scope the model gets from its mixin.
            AssertClean(Analyzer.Analyze(Mixin.Replace("CALL", call, StringComparison.Ordinal), Path.Combine(Folder, "p.kscript"), EmptyFileSystem.Instance));
        }

        [Fact]
        public void AnInheritedScopeIsStillCheckedAndReturnsTheModelsQuery()
        {
            var wrongCount = Analyzer.Analyze(Mixin.Replace("CALL", "echo Item->owned_by('a', 'b')->count()", StringComparison.Ordinal), Path.Combine(Folder, "p.kscript"), EmptyFileSystem.Instance);
            Assert.Contains(wrongCount.Diagnostics, d => d.Code == DiagnosticCodes.ArgumentCount);
            var field = Analyzer.Analyze(Mixin.Replace("CALL", "echo Item->owned_by('a')->first().titel", StringComparison.Ordinal), Path.Combine(Folder, "p.kscript"), EmptyFileSystem.Instance);
            Assert.Contains(field.Diagnostics, d => d.Message.Contains("titel", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData("@boards.max_file_size", "it reads @boards.max_file_size and drops it", false)]
        [InlineData("@boards.move", "@boards.move without brackets is the function itself", true)]
        [InlineData("@go", "@go without brackets is the function itself", true)]
        public void AStatementThatOnlyNamesSomethingDoesNothing(string statement, string message, bool callFix)
        {
            string text = "import \"lib/boards.kscript\" as boards\n\nfn go() {\n  " + statement + "\n  echo 'done'\n}\n@go()\n";
            var analysis = Analyzer.Analyze(text, Path.Combine(Folder, "main.kscript"), Files());
            var d = AssertSingle(analysis, DiagnosticCodes.StatementSyntax);
            Assert.Contains(message, d.Message);
            Assert.Equal(DiagnosticSeverity.Error, d.Severity);
            QuickFix remove = Assert.Single(d.Fixes, f => f.Title == "Remove the statement");
            Assert.Equal("import \"lib/boards.kscript\" as boards\n\nfn go() {\n  echo 'done'\n}\n@go()\n", Apply(text, remove));
            QuickFix call = d.Fixes.FirstOrDefault(f => f.Title.StartsWith("Call it", StringComparison.Ordinal));
            Assert.Equal(callFix, call != null);
            if (call != null) Assert.Contains(statement + "()\n", Apply(text, call));
        }

        [Fact]
        public void AnOrdinaryStatementWithNoEffectCanBeRemovedToo()
        {
            const string text = "x = 1\n$x + 2\necho $x\n";
            var d = AssertSingle(Analyzer.Analyze(text, Path.Combine(Folder, "main.kscript"), EmptyFileSystem.Instance), DiagnosticCodes.StatementSyntax);
            Assert.Equal("x = 1\necho $x\n", Apply(text, Assert.Single(d.Fixes)));
        }

        private static string Apply(string text, QuickFix fix)
        {
            foreach (var change in fix.Changes.OrderByDescending(c => c.Span.Start))
                text = text.Substring(0, change.Span.Start) + change.NewText + text.Substring(change.Span.End);
            return text;
        }
    
        [Fact]
        public void APackageCommandNamedThroughAnAliasCanBeCalledToo()
        {
            const string text = "import KScript.Math as m\n@m.sqrt\n";
            var d = AssertSingle(Analyzer.Analyze(text, Path.Combine(Folder, "main.kscript"), EmptyFileSystem.Instance), DiagnosticCodes.StatementSyntax);
            Assert.Contains("@m.sqrt without brackets is the function itself", d.Message);
            Assert.Contains(d.Fixes, f => f.Title == "Call it: @m.sqrt()");
        }

        [Fact]
        public void AStatementSharingItsLineIsRemovedAlone()
        {
            const string text = "fn go() {\n  echo 'a'\n}\necho 'b'; @go\n";
            var d = AssertSingle(Analyzer.Analyze(text, Path.Combine(Folder, "main.kscript"), EmptyFileSystem.Instance), DiagnosticCodes.StatementSyntax);
            Assert.Equal("fn go() {\n  echo 'a'\n}\necho 'b'; \n", Apply(text, d.Fixes.Single(f => f.Title == "Remove the statement")));
            Assert.Equal("fn go() {\n  echo 'a'\n}\necho 'b'; @go()\n", Apply(text, d.Fixes.Single(f => f.Title == "Call it: @go()")));
        }
    }
}
