using KScript.Language.Analysis;

namespace KScript.Language.Tests
{
    /// <summary>
    /// A record's own methods stay known when the record reaches a script through another script's function: an item
    /// of a model's list field, from a library function (#197).
    /// </summary>
    public class ImportedRecordMethodTests
    {
        private static string Folder => AnalyzerCoverageTests.Folder;

        private static AnalyzerCoverageTests.Files Project() => new AnalyzerCoverageTests.Files(
            ("types/link.kscript", """
                type Link {
                  url: string

                  fn address() => $url->replace(/^https?:\/\//, "")
                }
                """),
            ("models/note.kscript", """
                import KScript.Data
                import "../types/link.kscript"

                model Note extends [Timestamps, SoftDeletes] {
                  title: string = ''
                  links: Link[] = []
                }
                """),
            ("lib/notes.kscript", """
                import KScript.Data
                import "../models/note.kscript"
                import "../types/link.kscript"

                /// @public
                fn find(owner: string, id: any) => Note->where(title: $owner, id: $id)->first()
                """));

        private static DocumentAnalysis Check(string text, string file = "b.kscript") =>
            TestHelpers.WithoutUnused(Analyzer.Analyze(text, Path.Combine(Folder, file), Project()));

        [Fact]
        public void ListItemOfAnImportedRecordKeepsItsTypesMethods()
        {
            DocumentAnalysis a = Check("""
                import "lib/notes.kscript" as notes
                b = @notes.find('x', 1)
                echo $b.links[0]->address()
                for link in $b.links {
                  echo $link->address()
                }
                """);
            TestHelpers.AssertClean(a);
        }
    
        [Fact]
        public void ComponentTemplateReachesTheImportedRecordsListItems()
        {
            DocumentAnalysis a = Check("""
                import KScript.Web
                import "../lib/notes.kscript" as notes

                type NoteLinks extends Component {
                  construct({ id: int })

                  fn render() => ```html
                    <let name="b" value="@notes.find('x', $id)" />
                    <p @each($b.links, as: l)>{$l->address()}</p>
                    <for each="link" in="$b.links"><a>{$link->address()}</a></for>
                    ```
                }
                """, "_components/note-links.kscript");
            TestHelpers.AssertClean(a);
        }

        [Fact]
        public void MethodsTheImportedTypeLacksAreStillReported()
        {
            DocumentAnalysis a = Check("""
                import "lib/notes.kscript" as notes
                b = @notes.find('x', 1)
                echo $b.links[0]->adress()
                """);
            Assert.Contains(a.Diagnostics, d => d.Code == KScript.Syntax.DiagnosticCodes.UnknownMethod && d.Message.Contains("adress", StringComparison.Ordinal));
        }
    }
}
