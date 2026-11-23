using KScript.Language.Analysis;
using KScript.Language.Features;

namespace KScript.Language.Tests
{
    /// <summary>Lines of a .kscript type or model block: settings and rules are suggested as you type, with their docs on hover.</summary>
    public class TypeBodyCompletionTests
    {
        private static readonly LanguageService Service = new LanguageService(files: EmptyFileSystem.Instance);

        private static string Path => System.IO.Path.Combine(AnalyzerCoverageTests.Folder, "app.kscript");

        private static List<CompletionItem> Complete(string textWithCaret, string trigger = null)
        {
            (string text, int offset) = TestHelpers.WithCaret(textWithCaret);
            return Service.GetCompletions(Service.Analyze(text, Path), offset, trigger);
        }

        private static List<string> Labels(string textWithCaret) => Complete(textWithCaret).Select(i => i.Label).ToList();

        private static HoverResult Hover(string textWithCaret)
        {
            (string text, int offset) = TestHelpers.WithCaret(textWithCaret);
            return Service.GetHover(Service.Analyze(text, Path), offset);
        }

        private static string Model(string body) => "import KScript.Data\n\nmodel Note {\n" + body + "\n}\n";

        [Fact]
        public void AModelsBracketsSuggestItsOptions()
        {
            List<CompletionItem> items = Complete("import KScript.Data\n\nmodel Note(timestamps, hi‸) {\n  user_id: string\n}\n");
            CompletionItem hidden = Assert.Single(items, i => i.Label == "hidden");
            Assert.Equal("hidden: [$1]", hidden.InsertText);
            Assert.True(hidden.IsSnippet);
            Assert.Equal(2, hidden.ReplaceSpan.Length);
            Assert.Contains("left out of JSON", hidden.Documentation);
            Assert.Equal("model option", hidden.Detail);
            // An option the model already has isn't offered again.
            Assert.DoesNotContain(items, i => i.Label == "timestamps");
            // A flag is on by being named, and the others take a value.
            items = Complete("import KScript.Data\n\nmodel Note(‸) {\n}\n");
            Assert.Equal("timestamps", Assert.Single(items, i => i.Label == "timestamps").InsertText);
            Assert.Equal("key: \"$1\"", Assert.Single(items, i => i.Label == "key").InsertText);
            Assert.Equal("table: \"$1\"", Assert.Single(items, i => i.Label == "table").InsertText);
            // Not where a value goes.
            Assert.DoesNotContain("timestamps", Labels("import KScript.Data\n\nmodel Note(table: ‸) {\n}\n"));
        }

        [Fact]
        public void AListInAModelsBracketsSuggestsItsFields()
        {
            const string import = "import KScript.Data\n\n";
            Assert.Equal(new[] { "title", "body", "id", "created_at", "updated_at", "deleted_at" },
                Labels(import + "model Note(timestamps, soft_deletes, hidden: [‸]) {\n  title: string\n  body?: string\n}\n"));
            // Fields already listed aren't offered again, and a word typed is replaced.
            List<CompletionItem> items = Complete(import + "model Note(index: [title, bo‸]) {\n  title: string\n  body: string\n}\n");
            Assert.Equal(new[] { "body", "id" }, items.Select(i => i.Label));
            Assert.Equal(2, items[0].ReplaceSpan.Length);
            Assert.Equal("string", items[0].Detail);
            // A set of fields that is unique together is a list in the list.
            Assert.Equal(new[] { "slug", "id" }, Labels(import + "model Post(unique: [[team, ‸]]) {\n  slug: string\n  team: int\n}\n"));
            // After the list, the next option.
            Assert.Contains("timestamps", Labels(import + "model Note(hidden: [title], ‸) {\n  title: string\n}\n"));
            Assert.DoesNotContain("title", Labels(import + "model Note(hidden: [title], ‸) {\n  title: string\n}\n"));
        }

        private const string License = """
            import KScript.Data
            type ProductName with [string] { min: 1 }
            model Other { other_field: string }
            model PortalLicense(
              key: 'key_hash',
              index: [owner_id],
              hidden: [FIELDS]
            ) {
              key_hash: string
              product: ProductName
              owner_id?: string
              registered_at?: string
            }
            """;

        [Fact]
        public void OpeningTheHiddenListSuggestsOnlyTheFieldsOfThisModel()
        {
            var fields = Complete(License.Replace("FIELDS", "‸"), "[");
            Assert.Equal(new[] { "key_hash", "product", "owner_id", "registered_at" }, fields.Select(i => i.Label));
            Assert.All(fields, i => Assert.Equal(CompletionKind.Field, i.Kind));
            Assert.Equal("ProductName", fields.Single(i => i.Label == "product").Detail);
            Assert.True(Complete(License.Replace("hidden: [FIELDS]", "hi‸")).Single(i => i.Label == "hidden").SuggestAgain);
        }

        [Fact]
        public void EditingAFieldListUsesTheWholeTokenAndOmitsSiblingsOnEitherSide()
        {
            var fields = Complete(License.Replace("FIELDS", "key_hash,\n    ow‸ner, registered_at"));
            Assert.Equal(new[] { "product", "owner_id" }, fields.Select(i => i.Label));
            Assert.Equal(5, fields.Single(i => i.Label == "owner_id").ReplaceSpan.Length);

            fields = Complete(License.Replace("FIELDS", "key_hash,‸"), ",");
            Assert.Equal(new[] { "product", "owner_id", "registered_at" }, fields.Select(i => i.Label));
            Assert.Equal(" owner_id", fields.Single(i => i.Label == "owner_id").InsertText);
        }

        [Theory]
        [InlineData("key: 'ke‸y_hash'")]
        [InlineData("key: \"ke‸y_hash\"")]
        public void AKeyOffersDeclaredFieldsAndReplacesJustTheName(string key)
        {
            var fields = Complete(License.Replace("key: 'key_hash'", key).Replace("FIELDS", "key_hash"));
            Assert.Equal(new[] { "key_hash", "product", "owner_id", "registered_at" }, fields.Select(i => i.Label));
            Assert.Equal(8, fields[0].ReplaceSpan.Length);
        }

        [Fact]
        public void CompositeIndexesCanReuseFieldsFromOtherIndexes()
        {
            var fields = Complete(License.Replace("index: [owner_id]", "index: [owner_id, [product, ‸]]").Replace("FIELDS", ""));
            Assert.Equal(new[] { "key_hash", "owner_id", "registered_at" }, fields.Select(i => i.Label));
            Assert.Empty(Complete("x = [‸]", "["));
        }

        [Fact]
        public void ALineInAModelSuggestsRulesAndFnNotOptions()
        {
            // Options go in the brackets, so a line of the block is a field, validate or fn.
            List<string> typed = Labels(Model("  hi‸\n  user_id: string"));
            Assert.Equal(new[] { "validate", "fn" }, typed);
            List<string> blank = Labels(Model("  user_id: string\n  ‸"));
            Assert.DoesNotContain(blank, l => l is "timestamps" or "hidden" or "if" or "echo" or "model");
            Assert.Contains("validate", blank);
            Assert.Contains("fn", blank);
        }

        [Fact]
        public void SettingValuesSuggestYesNoAndTheModelsFields()
        {
            Assert.Equal(new[] { "true", "false" }, Labels(Model("  timestamps: ‸\n  title: string")));
            Assert.Empty(Labels(Model("  timestamps: yes ‸\n  title: string")));
            Assert.Equal(new[] { "title", "body", "id", "created_at", "updated_at", "deleted_at" },
                Labels(Model("  timestamps: yes\n  soft_deletes: yes\n  hidden: [‸]\n  title: string\n  body?: string")));
            // Fields already listed aren't offered again, and a word typed is replaced.
            List<CompletionItem> items = Complete(Model("  index: [title, bo‸]\n  title: string\n  body: string"));
            Assert.Equal(new[] { "body", "id" }, items.Select(i => i.Label));
            Assert.Equal(2, items[0].ReplaceSpan.Length);
            Assert.Equal("string", items[0].Detail);
            Assert.Equal(new[] { "title" }, Labels(Model("  key: ‸\n  title: string")));
            // After a value, or for settings that aren't fields, nothing.
            Assert.Empty(Labels(Model("  hidden: [title] ‸\n  title: string")));
            Assert.Empty(Labels(Model("  table: ‸\n  title: string")));
        }

        [Fact]
        public void TypeBlocksSuggestTheirRules()
        {
            Assert.Equal(new[] { "pattern", "parse", "validate", "fn" }, Labels("type Note {\n  p‸\n  title: string\n}\n"));
            List<CompletionItem> refined = Complete("type Age with [int] {\n  min: 0\n  ‸\n}\n");
            Assert.Equal(new[] { "max", "pattern", "one_of", "not", "validate", "normalize", "fn" }, refined.Select(i => i.Label));
            Assert.Equal("one_of: [$1]", refined.Single(i => i.Label == "one_of").InsertText);
            Assert.Equal("pattern: /${1:^[a-z]+\\$}/", refined.Single(i => i.Label == "pattern").InsertText);
            Assert.StartsWith("normalize: ", refined.Single(i => i.Label == "normalize").InsertText);
            Assert.StartsWith("validate: ", refined.Single(i => i.Label == "validate").InsertText);
            Assert.Contains("largest value", refined.Single(i => i.Label == "max").Documentation);
            Assert.Contains("Cleans up a value", refined.Single(i => i.Label == "normalize").Documentation);
        }

        [Fact]
        public void FieldTypesAndTheirRulesAreSuggested()
        {
            Assert.Contains("string", Labels(Model("  title: st‸")));
            List<CompletionItem> rules = Complete(Model("  title: string(m‸)"));
            Assert.Equal(new[] { "min", "max", "pattern", "one_of", "not" }, rules.Select(i => i.Label));
            Assert.Equal("min: ", rules[0].InsertText);
            Assert.Contains("smallest value", rules[0].Documentation);
            // Rules already given aren't offered again; a native type's own options follow.
            Assert.Equal(new[] { "max", "pattern", "one_of", "not", "v" }, Labels(Model("  ref: uuid(min: 1, ‸)")));
            Assert.Contains("min", Labels("fn f(name: string(‸)) {\n}\n"));
            // A call, text or a nested list isn't a type's rules.
            Assert.DoesNotContain("min", Labels("x = @round(‸)\n"));
            Assert.DoesNotContain("min", Labels("x = [@round(1, ‸)]\n"));
            Assert.DoesNotContain("min", Labels(Model("  title: string(pattern: '‸')")));
            Assert.DoesNotContain("min", Labels(Model("  title: string(one_of: [a, ‸])")));
            Assert.DoesNotContain("min", Labels("x = (‸)\n"));
            Assert.DoesNotContain("min", Labels("x = nope(‸)\n"));
            Assert.DoesNotContain("min", Labels("x = ‸\n"));
            Assert.DoesNotContain("min", Labels("‸"));
            Assert.DoesNotContain("min", Labels("x = (1,‸)\n"));
        }

        [Fact]
        public void HoverExplainsSettingsAndRules()
        {
            HoverResult setting = Hover(Model("  times‸tamps: yes\n  title: string"));
            Assert.StartsWith("**timestamps** (model setting)", setting.Markdown);
            Assert.Contains("created_at and updated_at", setting.Markdown);
            Assert.StartsWith("**min** (rule)", Hover("type Age with [int] {\n  m‸in: 0\n}\n").Markdown);
            Assert.StartsWith("**min** (rule)", Hover(Model("  title: string(m‸in: 1)")).Markdown);
            Assert.Equal("**v** (uuid option)", Hover(Model("  ref: uuid(‸v: 4)")).Markdown);
            Assert.Null(Hover(Model("  ref: uuid(no‸pe: 4)")));
            Assert.Null(Hover(Model("  title: string(min‸)")));
        }

        [Fact]
        public void XmlScriptsKeepTheirOwnCompletion()
        {
            (string text, int offset) = TestHelpers.WithCaret("<kscript>\n<import package=\"KScript.Data\" />\n<model name=\"Note\" ‸>\n</model>\n</kscript>");
            List<CompletionItem> items = Service.GetCompletions(Service.Analyze(text, System.IO.Path.Combine(AnalyzerCoverageTests.Folder, "app.ks.xml")), offset);
            Assert.Contains(items, i => i.Label == "hidden");
        }
    }
}
