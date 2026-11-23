using KScript.Language.Analysis;
using KScript.Syntax;

namespace KScript.Language.Tests
{
    public class ModelOptionAnalysisTests
    {
        private static Diagnostic[] UnknownFields(string source, string extension = ".kscript") =>
            Analyzer.Analyze(source, Path.Combine(AnalyzerCoverageTests.Folder, "license" + extension), EmptyFileSystem.Instance)
                .Diagnostics.Where(d => d.Code == DiagnosticCodes.UnknownColumn).ToArray();

        [Fact]
        public void OptionsCanNameFieldsDeclaredLaterInTheModel()
        {
            const string source = """
                import KScript.Data
                type ProductName with [string] { min: 1 }
                model PortalLicense(key: 'key_hash', table: 'license_entries', connection: 'archive', index: [owner_id], hidden: [key_hash, owner_id], unique: [[owner_id, product]], guarded: [registered_at]) {
                  key_hash: string
                  product: ProductName
                  owner_id?: string
                  registered_at?: string
                }
                """;
            Assert.Empty(UnknownFields(source));
        }

        [Theory]
        [InlineData("key: 'owner_di'")]
        [InlineData("index: [key_hash, [product, owner_di]]")]
        [InlineData("unique: [[owner_di, product]]")]
        [InlineData("hidden: [key_hash, owner_di]")]
        [InlineData("guarded: [owner_di]")]
        public void UnknownOptionFieldHasAnErrorAndPreciseReplacement(string option)
        {
            string source = "import KScript.Data\nmodel License(" + option + ") {\n  key_hash: string\n  product: string\n  owner_id?: string\n}";
            Diagnostic error = Assert.Single(UnknownFields(source));
            Assert.Equal(DiagnosticSeverity.Error, error.Severity);
            Assert.Equal("License has no field 'owner_di'. Did you mean owner_id?", error.Message);
            Assert.Equal("owner_di", source.Substring(error.Span.Start, error.Span.Length));
            TextChange fix = Assert.Single(Assert.Single(error.Fixes).Changes);
            Assert.Equal(error.Span, fix.Span);
            Assert.Equal("owner_id", fix.NewText);
            Assert.Empty(UnknownFields(source.Remove(fix.Span.Start, fix.Span.Length).Insert(fix.Span.Start, fix.NewText)));
        }

        [Fact]
        public void ListsSeeGeneratedModelColumnsAndKeyRequiresAnExplicitField()
        {
            const string source = """
                import KScript.Data
                model Note(timestamps, soft_deletes, hidden: [id, created_at, updated_at, deleted_at]) {
                  title: string
                }
                model Ticket(key: 'code', index: [id]) {
                  code: string
                }
                model Entry(key: 'id', hidden: [deleted_at]) {
                  title: string
                }
                model Event(key: 'created_at', timestamps) {
                  title: string
                }
                """;
            Diagnostic[] errors = UnknownFields(source);
            Assert.Equal(4, errors.Length);
            Assert.Contains(errors, d => d.Message.StartsWith("Ticket has no field 'id'."));
            Assert.Contains(errors, d => d.Message.StartsWith("Entry has no field 'id'."));
            Assert.Contains(errors, d => d.Message.StartsWith("Entry has no field 'deleted_at'."));
            Assert.Contains(errors, d => d.Message.StartsWith("Event has no field 'created_at'."));
        }

        [Fact]
        public void OptionsDoNotAcceptQueryOnlyRelationFieldsOrColumnsFromAnotherModel()
        {
            const string source = """
                import KScript.Data
                model Author(hidden: [posts, posts_count, title]) {
                  name: string
                  posts: Post[](by: author)
                }
                model Post(index: [author, author_id]) {
                  title: string
                  author: Author
                }
                """;
            Diagnostic[] errors = UnknownFields(source);
            Assert.Equal(3, errors.Length);
            Assert.Contains(errors, d => d.Message.StartsWith("Author has no field 'posts_count'."));
            Assert.Contains(errors, d => d.Message.StartsWith("Author has no field 'title'."));
            Assert.Contains(errors, d => d.Message.StartsWith("Post has no field 'author_id'."));
        }

        [Fact]
        public void XmlInlineFieldsAndDecodedOptionSpansAreRespected()
        {
            const string source = """
                <kscript>
                  <import package="KScript.Data" />
                  <model name="License" fields="key_hash: string, owner_id?: string" key="key_hash" hidden="owner_id, key_ha&#x78;h" />
                </kscript>
                """;
            Diagnostic error = Assert.Single(UnknownFields(source, ".ks.xml"));
            Assert.Equal("License has no field 'key_haxh'. Did you mean key_hash?", error.Message);
            Assert.Equal("key_ha&#x78;h", source.Substring(error.Span.Start, error.Span.Length));
        }
    }
}
