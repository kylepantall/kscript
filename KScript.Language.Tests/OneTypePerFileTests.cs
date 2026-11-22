using KScript.Language.Analysis;
using KScript.Syntax;

using static KScript.Language.Tests.AnalyzerCoverageTests;

namespace KScript.Language.Tests
{
    /// <summary>KS6008: in a project, each type, model, enum and error type has its own file.</summary>
    public class OneTypePerFileTests
    {
        private static readonly Files Project = new Files(("kscript.json", "{ \"paths\": { \"errors\": \"app/errors\" } }"));

        private static List<Diagnostic> Ks6007(string text, string file, IFileSystem files = null) =>
            Analyzer.Analyze(text, Path.Combine(Folder, file), files ?? Project).Diagnostics.Where(d => d.Code == DiagnosticCodes.OneTypePerFile).ToList();

        [Fact]
        public void A_second_type_is_flagged_with_where_to_move_it()
        {
            List<Diagnostic> found = Ks6007("enum Visibility { public, private }\n\ntype Owner {\n  login: string\n}\n\nerror NoRepo {\n}\n", "lib/github.kscript");
            Assert.Equal(2, found.Count);
            Assert.All(found, d => Assert.Equal(DiagnosticSeverity.Warning, d.Severity));
            Assert.Contains("already declares Visibility", found[0].Message);
            Assert.Contains("move the type Owner to types/owner.kscript", found[0].Message);
            Assert.Contains("move the error type NoRepo to app/errors/no-repo.kscript", found[1].Message);
        }

        [Fact]
        public void The_type_the_file_is_named_after_stays()
        {
            string order = "import KScript.Data\n\nenum OrderStatus { open, paid }\n\nmodel Order {\n  status: OrderStatus = 'open'\n}\n";
            Diagnostic d = Assert.Single(Ks6007(order, "models/order.kscript"));
            Assert.Contains("already declares Order.", d.Message);
            Assert.Contains("move the enum OrderStatus to types/order-status.kscript", d.Message);
        }

        [Fact]
        public void Xml_files_are_told_the_xml_name()
        {
            string xml = "<kscript>\n  <type name=\"NewPerson\" fields=\"name: string\" />\n  <type name=\"HTTPQuery\" fields=\"q?: string\" />\n</kscript>\n";
            Diagnostic d = Assert.Single(Ks6007(xml, "controllers/people.ks.xml"));
            Assert.Contains("types/http-query.ks.xml", d.Message);
        }

        [Fact]
        public void One_type_a_script_outside_a_project_and_nested_types_are_fine()
        {
            Assert.Empty(Ks6007("type Owner {\n  login: string\n}\n\nfn main() => 1\n", "owner.kscript"));
            Assert.Empty(Ks6007("type A {\n  x: int\n}\n\ntype B {\n  y: int\n}\n", "one-off.kscript", new Files()));
        }

        [Fact]
        public void A_broken_kscript_json_still_makes_a_project_with_the_default_folders()
        {
            Diagnostic d = Assert.Single(Ks6007("type A {\n  x: int\n}\n\nerror Bad {\n}\n", "a.kscript", new Files(("kscript.json", "{ nope"))));
            Assert.Contains("errors/bad.kscript", d.Message);
            Assert.DoesNotContain(Analyzer.Analyze("type A {\n  x: int\n}\n\ntype B {\n  y: int\n}\n", null, Project).Diagnostics, x => x.Code == DiagnosticCodes.OneTypePerFile);
        }

        [Fact]
        public void A_type_imported_only_for_returns_docs_is_used()
        {
            Files files = new Files(("kscript.json", "{}"), ("person.kscript", "type Person {\n  name: string\n}\n"));
            string text = "import \"person.kscript\"\n\n/// One person.\n/// @returns Person\nfn show() => { name: 'Ada' }\n";
            DocumentAnalysis a = Analyzer.Analyze(text, Path.Combine(Folder, "people.kscript"), files);
            Assert.DoesNotContain(a.Diagnostics, d => d.Code == DiagnosticCodes.UnusedImport);
        }
    }
}
