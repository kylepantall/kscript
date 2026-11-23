using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Runtime;
using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests
{
    public class ExpectedContainerValueTests
    {
        private static readonly LanguageService Service = new LanguageService();
        private const string Fields = """
            enum Size { small, large }
            type Shirt { tag: Size; tags: Size[] }
            type Box { shirt: Shirt; items: Shirt[] }
            """ + "\n";

        private static List<CompletionItem> Values(string body)
        {
            (string text, int offset) = WithCaret(Fields + body);
            DocumentAnalysis analysis = Analyze(text, "choices.kscript");
            return Service.GetCompletions(analysis, offset).Where(i => i.Kind == CompletionKind.Value).ToList();
        }

        [Theory]
        [InlineData("sizes: Size[] = [‸]")]
        [InlineData("sizes: Size[] = [small, la‸]")]
        [InlineData("sizes: Size[] = [\n  small,\n  ‸\n]")]
        [InlineData("sizes: Size[] = [small]\nsizes = [‸]")]
        [InlineData("shirt = Shirt(tag: ‸)")]
        [InlineData("shirt = Shirt(tags: [small, ‸])")]
        public void TypedListsAndConstructorsOfferEnumChoices(string body)
        {
            var choices = Values(body);
            Assert.Equal(new[] { "small", "large" }, choices.Select(i => i.Label));
            Assert.All(choices, i => Assert.Null(i.InsertText));
        }

        [Theory]
        [InlineData("shirt: Shirt = {tag: ‸}")]
        [InlineData("shirt: Shirt = {tags: [‸]}")]
        [InlineData("box: Box = { shirt: { tag: ‸ }}")]
        [InlineData("box: Box = { items: [{ tag: ‸ }]}")]
        [InlineData("box = Box(shirt: { tag: ‸ })")]
        [InlineData("shirts: Shirt[] = [{ tag: ‸ }]")]
        [InlineData("shirt: Shirt = {\n  'tag': ‸\n}")]
        public void TypedMapFieldsInsertQuotedEnumExpressions(string body)
        {
            var item = Assert.Single(Values(body), i => i.Label == "small");
            Assert.Equal("'small'", item.InsertText);
        }

        [Fact]
        public void InsertedMapValuesRespectTheXmlAttributeQuotes()
        {
            (string text, int at) = WithCaret(Script("""
                <enum name="Size" values="small, large"/>
                <type name="Shirt"><field name="tag" type="Size"/></type>
                <let name="shirt" type="Shirt" value='{tag: ‸}'/>
                """));
            var item = Assert.Single(Service.GetCompletions(Analyze(text), at), i => i.Label == "small");
            Assert.Equal("&apos;small&apos;", item.InsertText);
        }

        [Fact]
        public void AFieldValueReplacesTheWholePartialValue()
        {
            (string text, int at) = WithCaret(Fields + "shirt: Shirt = {tag: la‸rge}");
            var item = Assert.Single(Service.GetCompletions(Analyze(text, "choices.kscript"), at), i => i.Label == "small");
            Assert.Equal("large", text.Substring(item.ReplaceSpan.Start, item.ReplaceSpan.Length));
            Assert.Equal(Fields + "shirt: Shirt = {tag: 'small'}", text[..item.ReplaceSpan.Start] + item.InsertText + text[item.ReplaceSpan.End..]);
        }

        [Theory]
        [InlineData("shirt: Shirt = {tag: 'la‸rge'}", "'large'")]
        [InlineData("shirt = Shirt(tag: \"la‸rge\")", "\"large\"")]
        [InlineData("sizes: Size[] = ['la‸", "'la")]
        public void ReplacingAQuotedPrefixKeepsOnePairOfQuotes(string body, string oldText)
        {
            (string text, int at) = WithCaret(Fields + body);
            var item = Assert.Single(Service.GetCompletions(Analyze(text, "choices.kscript"), at), i => i.Label == "small");
            Assert.Equal(oldText, text.Substring(item.ReplaceSpan.Start, item.ReplaceSpan.Length));
            Assert.Equal("'small'", item.InsertText);
        }

        [Fact]
        public void FiniteRefinementsHonorInheritedChoicesAndOtherStaticRules()
        {
            var values = Values("""
                type Paint with [string] { one_of: ['red', 'blue sky', 'tan'] }
                type Label with [Paint] { not: ['red'] }
                label: Label = ‸
                """);
            Assert.Equal(new[] { "blue sky", "tan" }, values.Select(i => i.Label));
            Assert.Equal("'blue sky'", values[0].InsertText);
        }

        [Fact]
        public void UnfinishedContainersStopRequestingTypesBeyondTheEditingDepthLimit()
        {
            const int depth = 40;
            string source = "items: Size" + string.Concat(Enumerable.Repeat("[]", depth)) + " = " + new string('[', depth) + "‸";
            Assert.DoesNotContain(Values(source), i => i.Label == "small");
        }

        [Fact]
        public void FiniteSetsCompareConvertedValuesAndKeepTheNearestSpelling()
        {
            var values = Values("""
                type Original with [country] { one_of: [GB, IE, FR] }
                type Region with [Original] { one_of: [FRA, GBR, USA, IRL]; not: [IE] }
                region: Region = ‸
                """);
            Assert.Equal(new[] { "FRA", "GBR" }, values.Select(i => i.Label));
            Assert.Empty(Values("type Dynamic with [string] { one_of: [@compute()] }\nx: Dynamic = ‸"));
        }

        [Fact]
        public void IncreasingFiniteMembershipListsDoesNotSquareTheAllocationsForSuggestions()
        {
            static long Allocations(int count)
            {
                var words = Enumerable.Range(0, count).Select(i => (object)("value" + i)).ToList();
                var parent = TypeRules.FromOptions(new Dictionary<string, object> { ["one_of"] = words }, out _);
                var own = TypeRules.FromOptions(new Dictionary<string, object> { ["one_of"] = words, ["min"] = 2.0 }, out _);
                var levels = new[] { own, parent };
                long before = GC.GetAllocatedBytesForCurrentThread();
                var choices = Analyzer.ChoicesFromRules(own, levels, "string");
                long used = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.Equal(count, choices.Count);
                return used;
            }
            Allocations(8);
            long small = Allocations(128);
            long large = Allocations(512);
            Assert.True(large < 8 * small, $"Four times the choices allocated {large} bytes versus {small}.");
        }

        [Fact]
        public void ImportedRefinementChoicesFollowTheDeclaredRecordField()
        {
            var files = new AnalyzerCoverageTests.Files(("inventory.kscript", """
                type Paint with [string] { one_of: ['red', 'blue'] }
                type Product { paint: Paint }
                """));
            var service = new LanguageService(files: files);
            (string text, int at) = WithCaret("import \"inventory.kscript\"\nx: Product = { paint: ‸ }");
            var analysis = service.Analyze(text, Path.Combine(AnalyzerCoverageTests.Folder, "catalog.kscript"));
            var choice = Assert.Single(service.GetCompletions(analysis, at), i => i.Label == "blue");
            Assert.Equal("'blue'", choice.InsertText);
        }

        [Fact]
        public void InlineOneOfUsesTheSameFiniteChoicesAndInsertionRules()
        {
            var items = Values("name: string(min: 3, one_of: ['x', 'blue sky', 'null']) = ‸");
            Assert.Equal(new[] { "'blue sky'", "'null'" }, items.Select(i => i.InsertText));
        }

        [Fact]
        public void ATextChoiceThatLooksLikeALanguageLiteralStaysText()
        {
            var items = Values("type Label with [string] { one_of: ['null', '1', 'true'] }\nx: Label = ‸");
            Assert.Equal(new[] { "'null'", "'1'", "'true'" }, items.Select(i => i.InsertText));
        }

        [Theory]
        [InlineData("xs = [small, ‸]")]
        [InlineData("shirt: Shirt = { other: ‸ }")]
        [InlineData("shirt: Shirt = { tag: @round(1, ‸) }")]
        [InlineData("shirt: Shirt = { tag: 'small' + ‸ }")]
        public void TypesFromOuterContainersDoNotLeakIntoUnrelatedExpressions(string body) =>
            Assert.DoesNotContain(Values(body), i => i.Label is "small" or "large");
    }
}
