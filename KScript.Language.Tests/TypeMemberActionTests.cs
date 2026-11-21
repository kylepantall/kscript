using KScript.Language.Analysis;
using KScript.Syntax;
using KScript.Syntax.Text;

using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests
{
    /// <summary>
    /// Like C#'s "implement missing members" and "generate overrides": a job's missing fn handle() has a fix that
    /// writes it, a job can add fn failed(err), a joined type can override what its bases give it, and a method two
    /// bases give differently must be declared.
    /// </summary>
    public class TypeMemberActionTests
    {
        private static string Apply(string text, QuickFix fix)
        {
            foreach (TextChange c in fix.Changes.OrderByDescending(c => c.Span.Start))
            {
                text = text.Substring(0, c.Span.Start) + c.NewText + text.Substring(c.Span.End);
            }
            return text;
        }

        private static QuickFix Action(DocumentAnalysis a, string title) => Assert.Single(a.Actions, x => x.Fix.Title == title).Fix;

        private static IEnumerable<Diagnostic> Errors(DocumentAnalysis a) => a.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);

        private const string Job = "import KScript.Queue\n\njob Count {\n  n: int\n}\n";

        [Fact]
        public void A_job_without_handle_gets_a_fix_that_writes_it()
        {
            DocumentAnalysis a = Analyze(Job, "jobs.kscript");
            Diagnostic missing = Assert.Single(a.Diagnostics, d => d.Code == DiagnosticCodes.MissingAttribute);
            QuickFix fix = Assert.Single(missing.Fixes);
            Assert.Equal("Add fn handle()", fix.Title);
            string fixedText = Apply(Job, fix);
            Assert.Equal("import KScript.Queue\n\njob Count {\n  n: int\n\n  fn handle() {\n    // The job's work. Its fields are variables here, and $job says which attempt this is.\n  }\n}\n", fixedText);
            Assert.Empty(Errors(Analyze(fixedText, "jobs.kscript")));
        }

        [Fact]
        public void A_job_can_add_failed_until_it_has_one()
        {
            string withHandle = "import KScript.Queue\n\njob Count {\n  n: int\n\n  fn handle() {\n    echo $n\n  }\n}\n";
            DocumentAnalysis a = Analyze(withHandle, "jobs.kscript");
            string fixedText = Apply(withHandle, Action(a, "Add fn failed(err), run when the job gives up"));
            Assert.Contains("  fn failed(err) {\n    // Runs once when the job gives up, with the last error as $err.\n  }\n}\n", fixedText, StringComparison.Ordinal);
            DocumentAnalysis after = Analyze(fixedText, "jobs.kscript");
            Assert.Empty(Errors(after));
            Assert.Empty(after.Actions);
        }

        [Fact]
        public void Jobs_in_xml_get_the_xml_form()
        {
            string xml = "<kscript>\n  <import package=\"KScript.Queue\" />\n  <job name=\"Count\" fields=\"n: int\">\n  </job>\n</kscript>";
            DocumentAnalysis a = Analyze(xml, "jobs.ks.xml");
            string fixedText = Apply(xml, Assert.Single(Assert.Single(a.Diagnostics, d => d.Code == DiagnosticCodes.MissingAttribute).Fixes));
            Assert.Contains("  <job name=\"Count\" fields=\"n: int\">\n    <fn name=\"handle\">\n      <!-- The job's work.", fixedText, StringComparison.Ordinal);
            Assert.Contains("    </fn>\n  </job>", fixedText, StringComparison.Ordinal);
            Assert.Empty(Errors(Analyze(fixedText, "jobs.ks.xml")));
            Assert.Contains("<fn name=\"failed\" params=\"err\">", Apply(xml, Action(a, "Add fn failed(err), run when the job gives up")), StringComparison.Ordinal);
        }

        private const string Types = "type User {\n  name: string\n\n  fn greet(who: string) => 'Hi ' + $who\n}\n";

        [Theory]
        [InlineData("type Admin with [User] {\n  level: int = 1\n}\n", "type Admin with [User] {\n  level: int = 1\n\n  fn greet(who: string) {\n    // Replaces User's greet in Admin.\n  }\n}\n")]
        [InlineData("type Admin with [User] {}\n", "type Admin with [User] {\n  fn greet(who: string) {\n    // Replaces User's greet in Admin.\n  }\n}\n")]
        [InlineData("type Admin with [User]\n", "type Admin with [User] {\n  fn greet(who: string) {\n    // Replaces User's greet in Admin.\n  }\n}\n")]
        public void A_joined_type_can_override_its_bases_methods(string admin, string expected)
        {
            DocumentAnalysis a = Analyze(Types + admin, "types.kscript");
            string fixedText = Apply(Types + admin, Action(a, "Override fn greet(who: string) from User"));
            Assert.Equal(Types + expected, fixedText);
            DocumentAnalysis after = Analyze(fixedText, "types.kscript");
            Assert.Empty(Errors(after));
            Assert.DoesNotContain(after.Actions, x => x.Fix.Title.StartsWith("Override", StringComparison.Ordinal));
        }

        [Fact]
        public void The_override_action_is_offered_on_the_type_line()
        {
            string text = Types + "type Admin with [User] {\n  level: int = 1\n}\n";
            DocumentAnalysis a = Analyze(text, "types.kscript");
            int onHeader = text.IndexOf("Admin", StringComparison.Ordinal);
            int inBody = text.IndexOf("level", StringComparison.Ordinal);
            var service = new KScript.Language.Features.LanguageService();
            Assert.Contains(service.GetCodeActions(a, new TextSpan(onHeader, 0)), f => f.Title.StartsWith("Override fn greet", StringComparison.Ordinal));
            Assert.Empty(service.GetCodeActions(a, new TextSpan(inBody, 0)));
        }

        [Fact]
        public void Xml_types_open_a_self_closing_tag()
        {
            string xml = "<kscript>\n  <type name=\"User\" fields=\"name: string\"><fn name=\"greet\" value=\"'hi'\" /></type>\n  <type name=\"Admin\" with=\"User\" />\n</kscript>";
            string fixedText = Apply(xml, Action(Analyze(xml, "types.ks.xml"), "Override fn greet() from User"));
            Assert.Contains("  <type name=\"Admin\" with=\"User\">\n    <fn name=\"greet\">\n      <!-- Replaces User's greet in Admin. -->\n    </fn>\n  </type>\n", fixedText, StringComparison.Ordinal);
            Assert.Empty(Errors(Analyze(fixedText, "types.ks.xml")));
        }

        [Fact]
        public void A_method_two_bases_give_differently_must_be_declared()
        {
            string text = "type A {\n  a: int\n  fn label() => 'a'\n}\ntype B {\n  b: int\n  fn label() => 'b'\n}\ntype C with [A, B] {\n  c: int\n}\n";
            DocumentAnalysis a = Analyze(text, "types.kscript");
            Diagnostic clash = Assert.Single(a.Diagnostics, d => d.Code == DiagnosticCodes.DuplicateDeclaration);
            Assert.Equal("C gets the method 'label' from A and B. Declare fn label in C to choose.", clash.Message);
            QuickFix fix = Assert.Single(clash.Fixes);
            Assert.Equal("Declare fn label in C", fix.Title);
            string fixedText = Apply(text, fix);
            Assert.Contains("  c: int\n\n  fn label() {\n    // A and B both have label: write the one C uses.\n  }\n}\n", fixedText, StringComparison.Ordinal);
            Assert.DoesNotContain(Analyze(fixedText, "types.kscript").Diagnostics, d => d.Code == DiagnosticCodes.DuplicateDeclaration);
        }

        [Fact]
        public void A_base_shared_through_two_paths_is_no_clash()
        {
            string text = Types + "type A with [User] {\n  a: int\n}\ntype B with [User] {\n  b: int\n}\ntype C with [A, B] {\n  c: int\n}\n";
            DocumentAnalysis a = Analyze(text, "types.kscript");
            Assert.DoesNotContain(a.Diagnostics, d => d.Code == DiagnosticCodes.DuplicateDeclaration);
            Assert.Contains(a.Actions, x => x.Fix.Title == "Override fn greet(who: string) from A");
        }
    }
}
