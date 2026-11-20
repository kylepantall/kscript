using KScript.Language.Analysis;
using KScript.Language.Features;

namespace KScript.Language.Tests
{
    /// <summary>Type-aware completion in pages: JSON data, list items in lambdas and loops, and component props.</summary>
    public class IntelliSenseTests
    {
        private sealed class FakeFiles : IFileSystem
        {
            private readonly Dictionary<string, string> files;
            public FakeFiles(Dictionary<string, string> files) => this.files = files.ToDictionary(k => Path.GetFullPath(k.Key), k => k.Value);
            public bool FileExists(string path) => files.ContainsKey(Path.GetFullPath(path));
            public bool DirectoryExists(string path) => files.Keys.Any(f => f.StartsWith(Path.GetFullPath(path) + Path.DirectorySeparatorChar, StringComparison.Ordinal));
            public string ReadText(string path) => files.TryGetValue(Path.GetFullPath(path), out string t) ? t : null;
            public IEnumerable<string> ListFiles(string directory, string pattern) =>
                files.Keys.Where(f => Path.GetDirectoryName(f) == Path.GetFullPath(directory) && f.EndsWith(pattern.TrimStart('*'), StringComparison.Ordinal));
        }

        private static readonly string Site = Path.Combine(Path.GetTempPath(), "kscript-intellisense");

        private static readonly LanguageService Service = new LanguageService(files: new FakeFiles(new Dictionary<string, string>
        {
            [Path.Combine(Site, "_layout.html")] = "<html><slot /></html>",
            [Path.Combine(Site, "data", "people.json")] = "[{\"name\": \"Joan\", \"age\": 82, \"address\": {\"city\": \"Leeds\"}}, {\"name\": \"Ellie\", \"role\": null}]",
            [Path.Combine(Site, "_components", "person-card.html")] = "<div class=\"card\"><h2>{$person.name}</h2><p>{$caption ?? ''}</p><slot /></div>",
        }));

        private const string People = "<let name=\"people\" value=\"@read_json('data/people.json')\" />\n";

        private static List<CompletionItem> Complete(string textWithCaret, string file = "index.html")
        {
            int offset = textWithCaret.IndexOf('‸');
            string text = textWithCaret.Remove(offset, 1);
            DocumentAnalysis analysis = Service.Analyze(text, Path.Combine(Site, file));
            return Service.GetCompletions(analysis, offset);
        }

        private static List<string> Labels(string textWithCaret, string file = "index.html") => Complete(textWithCaret, file).Select(i => i.Label).ToList();

        [Theory]
        [InlineData(People + "<p>{$people->first().‸}</p>")]
        [InlineData(People + "<for each=\"p\" in=\"$people\"><p>{$p.‸}</p></for>")]
        [InlineData(People + "<p>{$people->filter(fn($p) => $p.‸)}</p>")]
        [InlineData(People + "<p>{$people |> max_by(fn($p) => $p.‸)}</p>")]
        [InlineData(People + "<p>{$people->reduce(fn($total, $p) => $total + $p.‸, initial: 0)}</p>")]
        public void JsonFileFieldsComplete(string page)
        {
            List<string> labels = Labels(page);
            Assert.Equal(new[] { "address", "age", "name", "role" }, labels.Intersect(new[] { "name", "age", "address", "role" }).OrderBy(l => l));
        }

        [Fact]
        public void NestedJsonAndMappedListsHaveTypes()
        {
            Assert.Contains("city", Labels(People + "<p>{$people->first().address.‸}</p>"));
            List<string> methods = Labels(People + "<p>{$people->map(fn($p) => $p.name)->first()->‸}</p>");
            Assert.Contains("upper", methods);
            Assert.DoesNotContain("filter", methods);
        }

        [Fact]
        public void ListMethodsAreOfferedForJsonLists()
        {
            List<string> methods = Labels(People + "<p>{$people->‸}</p>");
            Assert.Contains("filter", methods);
            Assert.DoesNotContain("upper", methods);
        }

        [Fact]
        public void MissingJsonFilesAreStillUnknown()
        {
            Assert.Empty(Labels("<let name=\"x\" value=\"@read_json('data/missing.json')\" />\n<p>{$x.‸}</p>"));
        }

        [Fact]
        public void ComponentPropsCompleteAsAttributes()
        {
            List<CompletionItem> items = Complete("<person-card ‸ />");
            Assert.Equal(new[] { "person", "caption" }, items.Select(i => i.Label));
            Assert.All(items, i => Assert.Equal(CompletionKind.Attribute, i.Kind));
            Assert.Equal(new[] { "caption" }, Labels("<person-card person=\"{$x}\" ‸ />"));
        }

        [Fact]
        public void ComponentSnippetFillsInItsProps()
        {
            CompletionItem card = Complete("<‸").Single(i => i.Label == "person-card");
            Assert.Equal("person-card person=\"$1\" caption=\"$2\" />", card.InsertText);
            Assert.Contains("person, caption", card.Documentation);
        }

        [Fact]
        public void ComponentPropsAreItsUndeclaredVariables()
        {
            DocumentAnalysis a = Service.Analyze("<let name=\"local\" value=\"1\" /><p>{$local} {$request.path} {$title} {$title}</p>", Path.Combine(Site, "_components", "x.html"));
            Assert.Equal(new[] { "title" }, a.Inputs.Keys);
        }
    }
}
