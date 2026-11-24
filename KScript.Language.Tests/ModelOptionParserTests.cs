using KScript.Syntax;
using KScript.Syntax.Markup;

namespace KScript.Language.Tests
{
    public class ModelOptionParserTests
    {
        [Theory]
        [InlineData("key: 'key_hash', hidden: [", "hidden", "[")]
        [InlineData("index: [owner_id,\n key: 'key_hash'", "index", "[owner_id")]
        [InlineData("guarded: [,\n /* setting */ key: 'key_hash'", "guarded", "[")]
        [InlineData("unique: [[key_hash, owner_id], key: 'key_hash'", "unique", "[[key_hash, owner_id]")]
        public void MissingBracketReportsAnErrorAndPreservesTheRemainingModel(string options, string name, string value)
        {
            string text = "model PortalLicense(" + options + ") {\n  key_hash: string\n  owner_id?: string\n}\necho 'after'";
            MarkupDocument doc = CodeParser.Parse(text);
            Diagnostic error = Assert.Single(doc.Diagnostics);
            Assert.Equal(DiagnosticSeverity.Error, error.Severity);
            Assert.Equal(DiagnosticCodes.UnclosedElement, error.Code);
            Assert.Equal("This [ is missing its closing ].", error.Message);
            Assert.Equal(text.IndexOf('['), error.Span.Start);
            Assert.Equal(1, error.Span.Length);

            MarkupElement model = Assert.Single(doc.DescendantElements(), e => e.Name == "model");
            Assert.Equal(value, model.GetAttributeValue(name));
            Assert.Equal("'key_hash'", model.GetAttributeValue("key"));
            Assert.Equal(new[] { "key_hash", "owner_id?" }, model.ChildElements.Select(f => f.GetAttributeValue("name")));
            Assert.Single(doc.RootElements.Single().ChildElements, e => e.Name == "echo");
        }

        [Fact]
        public void MissingBracketAtEndOfFileAlsoReportsTheList()
        {
            MarkupDocument doc = CodeParser.Parse("model Note(hidden: [title");
            Assert.Contains(doc.Diagnostics, d => d.Code == DiagnosticCodes.UnclosedElement && d.Message.Contains("closing ]"));
            Assert.Contains(doc.Diagnostics, d => d.Message.Contains("closing )"));
        }

        [Theory]
        [InlineData("model Note (timestamps, index: [user_id], hidden: [user_id]) { user_id: string }", "hidden", "[user_id]")]
        [InlineData("model Note(unique: [[key, owner], [title, code]], hidden: [title, key]) { title: string }", "unique", "[[key, owner], [title, code]]")]
        [InlineData("model Note(index: [first,\n /* key: value, ) */ second]) { first: string }", "index", "[first,\n /* key: value, ) */ second]")]
        [InlineData("Widget(items: [@make(key: 'x'), { key: 3 }], enabled)", "items", "[@make(key: 'x'), { key: 3 }]")]
        public void BalancedListsAndOrdinaryExpressionArgumentsKeepTheirText(string text, string name, string value)
        {
            MarkupDocument doc = CodeParser.Parse(text);
            Assert.Empty(doc.Diagnostics);
            Assert.Equal(value, doc.RootElements.Single().ChildElements.Single().GetAttributeValue(name));
        }
    }
}
