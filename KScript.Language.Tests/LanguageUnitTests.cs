using KScript.Language.Analysis;
using KScript.Language.Catalog;
using KScript.Metadata;
using KScript.Runtime;

using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests
{
    /// <summary>Small pieces of the language tools: types, suggestions, lint levels, file access and package catalogs.</summary>
    public class LanguageUnitTests
    {
        private sealed class ShapesPackage : KScriptPackage
        {
            public override string Name => "KScript.Shapes";

            public override string Description => "Elements with every kind of content, for tests.";

            protected override void Register(PackageBuilder p)
            {
                p.Element(new ElementInfo("total", "Takes an expression as its text.", ContentKind.Expression), run => null);
                p.Element(new ElementInfo("steps", "Takes assignments as its text.", ContentKind.AssignmentStatements), run => null);
            }
        }

        private sealed class HollowPackage : KScriptPackage
        {
            public override string Name => "KScript.Hollow";

            public override string Description => "A package with nothing in it.";

            protected override void Register(PackageBuilder p)
            {
            }
        }

        private static LanguageCatalog Catalog()
        {
            var registry = new PackageRegistry();
            registry.Register(new ShapesPackage());
            registry.Register(new HollowPackage());
            return new LanguageCatalog(registry);
        }

        [Fact]
        public void Package_elements_with_expression_and_assignment_content_are_checked()
        {
            DocumentAnalysis a = AnalyzerCoverageTests.Check("<import package=\"KScript.Shapes\" /><let name=\"n\" value=\"1\" /><total>$n + $missing</total><steps>$n = 2; other = 3</steps><total> </total><steps></steps>", catalog: Catalog());
            Assert.Contains(a.Diagnostics, d => d.Code == DiagnosticCodes.UndefinedVariable && d.Message.Contains("$missing"));
            Assert.Contains(a.Diagnostics, d => d.Code == DiagnosticCodes.SetUndeclared || d.Message.Contains("other"));
        }

        [Fact]
        public void A_package_with_nothing_in_it_is_reported_as_missing()
        {
            DocumentAnalysis a = AnalyzerCoverageTests.Check("<import package=\"KScript.Hollow\" />", catalog: Catalog());
            Assert.Contains(a.Diagnostics, d => d.Code == DiagnosticCodes.UnknownPackage && d.Message == "KScript.Hollow isn't installed in this copy of kscript.");
        }

        [Fact]
        public void Project_packages_come_from_a_readable_kscript_json()
        {
            var unreadable = new AnalyzerCoverageTests.Files();
            unreadable.Unreadable.Add(Path.Combine(AnalyzerCoverageTests.Folder, "kscript.json"));
            Assert.Contains(AnalyzerCoverageTests.Check("<input into=\"x\" />", files: unreadable).Diagnostics, d => d.Code == DiagnosticCodes.MissingImport);

            var broken = new AnalyzerCoverageTests.Files(("kscript.json", "{ nope"));
            Assert.Contains(AnalyzerCoverageTests.Check("<input into=\"x\" />", files: broken).Diagnostics, d => d.Code == DiagnosticCodes.MissingImport);
        }

        [Fact]
        public void Types_print_short_and_long_forms()
        {
            var list = new TypeShape(KType.List) { Element = new TypeShape(KType.Map) };
            foreach (string f in new[] { "alpha", "bravo", "charlie", "delta", "echo", "foxtrot", "golf" }) list.Element.Fields[f] = new FieldInfo(f, new TypeShape(KType.String), null);
            Assert.StartsWith("list<{ alpha", list.Brief(30));
            Assert.StartsWith("list<{\n", list.Pretty());

            var named = new TypeShape(new string('t', 60));
            Assert.Equal(named.Display(), named.Brief());
            Assert.Equal(named.Display(), named.Pretty());

            DocumentAnalysis a = Analyze(Script("<fn name=\"f\" value=\"1\" />"));
            Assert.Equal("Function f", a.Symbols.First(s => s.Name == "f").ToString());
        }

        [Theory]
        [InlineData("{ a: 'x' }['a']", "string")]
        [InlineData("'abc'[0]", "any")]
        [InlineData("1 |> fn(x) => $x", "any")]
        [InlineData("[1] + [2]", "list<number>")]
        public void Expressions_have_types(string expression, string type)
        {
            DocumentAnalysis a = Analyze(Script($"<let name=\"v\" value=\"{expression}\" /><echo>{{$v}}</echo>"));
            Assert.Equal(type, a.Symbols.First(s => s.Name == "v").Shape.Display());
        }

        [Fact]
        public void Suggestions_lint_levels_and_empty_files()
        {
            Assert.Equal("abd", Suggestions.Closest("abc", new[] { "abc", "abd" }));
            Assert.Equal(DiagnosticSeverity.Error, LintRules.ToSeverity("error"));
            Assert.Equal(DiagnosticSeverity.Information, LintRules.ToSeverity("info"));
            Assert.Empty(EmptyFileSystem.Instance.ListFiles("/", "*"));
        }

        [Fact]
        public void Files_that_cannot_be_read_are_treated_as_missing()
        {
            if (!OperatingSystem.IsLinux()) return;
            // /proc/self/mem exists but fails on the first read, and /proc/1/map_files can't be listed (on Linux).
            Assert.Null(DiskFileSystem.Instance.ReadText("/proc/self/mem"));
            if (Directory.Exists("/proc/1/map_files")) Assert.Empty(DiskFileSystem.Instance.ListFiles("/proc/1/map_files", "*"));
        }
            [Fact]
        public void Imported_constants_keep_their_types()
        {
            DocumentAnalysis a = AnalyzerCoverageTests.Check("<import path=\"limits.ks.xml\" /><let name=\"v\" value=\"$limit\" /><echo>{$v}</echo>");
            Assert.Equal("number", a.Symbols.First(s => s.Name == "limit").Shape.Display());
        }
    }
}
