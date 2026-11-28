using KScript.Language.Analysis;
using KScript.Language.Catalog;
using KScript.Syntax;
using KScript.Syntax.Markup;

namespace KScript.Language.Tests
{
    /// <summary>
    /// timestamps and soft_deletes are the Timestamps and SoftDeletes mixins KScript.Data ships. The old options are
    /// KS6014 errors whose fix (and kscript check --fix) extends the mixins instead.
    /// </summary>
    public class ModelMixinTests
    {
        private const string Code = "/tmp/model-mixins/post.kscript";
        private const string Xml = "/tmp/model-mixins/post.ks.xml";

        private static List<Diagnostic> Retired(string text, string path = Code) =>
            Analyzer.Analyze(text, path).Diagnostics.Where(d => d.Code == DiagnosticCodes.TypeForm).ToList();

        [Theory]
        [InlineData("model Post(timestamps) {", "model Post extends Timestamps {")]
        [InlineData("model Post(timestamps, unique: [title]) {", "model Post(unique: [title]) extends Timestamps {")]
        [InlineData("model Post(soft_deletes, timestamps) {", "model Post extends [SoftDeletes, Timestamps] {")]
        [InlineData("model Post(timestamps: false, table: 'posts') {", "model Post(table: 'posts') {")]
        [InlineData("model Post(timestamps) extends Shared {", "model Post extends [Shared, Timestamps] {")]
        [InlineData("model Post(soft_deletes) extends [Shared] {", "model Post extends [Shared, SoftDeletes] {")]
        public void TheOldOptionsAreFixedIntoMixins(string before, string after)
        {
            string text = $"import KScript.Data\n\nmodel Shared(mixin) {{\n  note?: string\n}}\n\n{before}\n  title: string\n}}\n";
            Diagnostic d = Retired(text).First();
            Assert.Equal(DiagnosticSeverity.Error, d.Severity);
            Assert.Contains("is now the", d.Message);
            string fixedText = Fixer.Apply(text, Code).Text;
            Assert.Contains(after, fixedText);
            Assert.Empty(Retired(fixedText));
            Assert.Empty(Analyzer.Analyze(fixedText, Code).Errors);
        }

        [Fact]
        public void XmlModelsMoveTheOptionsToWith()
        {
            string text = "<kscript>\n  <import package=\"KScript.Data\" />\n  <model name=\"Post\" timestamps=\"true\" soft_deletes=\"true\" fields=\"title: string\" />\n</kscript>\n";
            Assert.Equal(2, Retired(text, Xml).Count);
            string fixedText = Fixer.Apply(text, Xml).Text;
            Assert.Contains("<model name=\"Post\" with=\"Timestamps, SoftDeletes\" fields=\"title: string\" />", fixedText);
            Assert.Empty(Analyzer.Analyze(fixedText, Xml).Errors);
        }

        [Fact]
        public void AnOptionOnABaseIsRetiredToo()
        {
            string text = "import KScript.Data\n\nmodel Shared(mixin) {\n  note?: string\n}\n\nmodel Post extends [Shared(timestamps: false)] {\n  title: string\n}\n";
            Assert.Contains(Retired(text), d => d.Message.StartsWith("timestamps is now the Timestamps mixin", StringComparison.Ordinal));
        }

        [Fact]
        public void TheMixinsGiveTheirFieldsToTheEditor()
        {
            const string text = """
                import KScript.Data

                model Post extends [Timestamps, SoftDeletes] {
                  title: string
                }

                post = Post->find(1)
                echo $post.created_at
                echo $post.updated_at
                echo $post.deleted_at
                echo Post->with_trashed()->count()
                """;
            var analysis = Analyzer.Analyze(text, Code);
            Assert.Empty(analysis.Errors);
            var post = Assert.Single(analysis.DeclaredTypes, t => t.Value.Name == "Post").Value;
            Assert.Contains("created_at", post.Fields.Keys);
            Assert.Contains("updated_at", post.Fields.Keys);
            Assert.Contains("deleted_at", post.Fields.Keys);
        }

        [Fact]
        public void EveryRetiredOptionNamesAMixinKScriptDataShips()
        {
            var sources = LanguageCatalog.Default.SourcesOf("KScript.Data").Select(s => s.Text).ToList();
            foreach ((string option, string mixin) in ModelMixins.ForOption)
                Assert.Contains(sources, s => s.Contains($"model {mixin}(mixin, {option})", StringComparison.Ordinal));
        }
    }
}
