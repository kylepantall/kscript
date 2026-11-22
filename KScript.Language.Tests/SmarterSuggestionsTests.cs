using System.Text.Json.Nodes;

using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Syntax;

namespace KScript.Language.Tests
{
    /// <summary>What a learner sees first: everyday suggestions on top, parameter names, hovers with an example, and how often a function is used.</summary>
    public class SmarterSuggestionsTests
    {
        private static readonly LanguageService Service = new LanguageService();

        private static List<string> Labels(string textWithCaret, string path = "test.kscript")
        {
            (string text, int offset) = TestHelpers.WithCaret(textWithCaret);
            return Service.GetCompletions(Service.Analyze(text, path), offset)
                .OrderBy(i => i.SortText ?? i.Label, StringComparer.Ordinal).Select(i => i.Label).ToList();
        }

        [Fact]
        public void An_empty_line_offers_the_everyday_statements_first()
        {
            List<string> labels = Labels("name = 'Joan'\n‸");
            Assert.Equal(new[] { "echo", "if", "for", "fn", "while" }, labels.Take(5));
            // return is only offered where it can be written: inside a function.
            Assert.Equal(new[] { "echo", "if", "for", "fn", "while", "return" }, Labels("fn area(w, h) {\n  ‸\n}").Take(6));
            // The rest follow by name, and still come before commands.
            Assert.True(labels.IndexOf("const") < labels.IndexOf("exit") && labels.IndexOf("exit") < labels.IndexOf("@env"));
        }

        [Fact]
        public void A_test_file_offers_the_test_statements_before_those()
        {
            List<string> labels = Labels("‸", "cart.test.kscript");
            Assert.Equal(new[] { "test", "expect", "describe", "expect_error", "echo", "if" }, labels.Take(6));
        }

        [Theory]
        [InlineData("name = 'Joan'\necho $name->‸", "count", "upper", "lower", "trim")]
        [InlineData("items = [1, 2]\necho $items->‸", "count", "map", "filter", "first")]
        [InlineData("user = { name: 'Joan' }\necho $user->‸", "keys", "values", "has", "get")]
        [InlineData("n = 4.5\necho $n->‸", "round", "format", "abs", "floor")]
        public void A_value_offers_the_methods_reached_for_most_first(string text, params string[] first)
        {
            Assert.Equal(first, Labels(text).Take(first.Length));
        }

        [Fact]
        public void Every_method_ranked_first_exists()
        {
            foreach ((string type, string[] names) in Ranking.Methods)
            {
                foreach (string name in names)
                {
                    Assert.True(Service.Catalog.GetMethods(name, type).Any(), $"{type}->{name} is ranked first but isn't a method.");
                }
            }
            foreach (string statement in Ranking.Statements.Concat(Ranking.TestStatements))
            {
                Assert.True(Service.Catalog.GetElement(statement) != null, $"'{statement}' is ranked first but isn't a statement.");
            }
        }

        [Fact]
        public void The_nearest_variable_comes_first_and_before_built_ins()
        {
            List<string> labels = Labels("first = 1\nsecond = 2\nthird = 3\necho $‸").Select(l => l.TrimStart('$')).ToList();
            Assert.Equal(new[] { "third", "second", "first" }, labels.Take(3));
        }

        private static List<string> Hints(string text, string path = "test.kscript") =>
            Service.GetInlayHints(Service.Analyze(text, path)).Where(h => h.Label.EndsWith(": ", StringComparison.Ordinal)).Select(h => h.Label).ToList();

        [Fact]
        public void Plain_values_in_a_call_with_several_arguments_get_their_parameter_names()
        {
            Assert.Equal(new[] { "width: ", "char: " }, Hints("name = 'Joan'\necho $name->pad_left(10, '.')"));
            // A function of the script works the same way.
            Assert.Equal(new[] { "price: ", "rate: " }, Hints("fn tax(price, rate) => $price * $rate\necho @tax(100, 0.2)"));
        }

        [Fact]
        public void Arguments_that_already_say_what_they_are_get_no_hint()
        {
            // One argument, a named argument, and a variable need no label.
            Assert.Empty(Hints("name = 'Joan'\necho $name->contains('J')"));
            Assert.Empty(Hints("name = 'Joan'\necho $name->pad_left(width: 10, char: '.')"));
            Assert.Equal(new[] { "char: " }, Hints("name = 'Joan'\nwidth = 10\necho $name->pad_left($width, '.')"));
            Assert.Equal(new[] { "width: " }, Hints("name = 'Joan'\necho $name->pad_left(10, char: '.')"));
            // More arguments than the function takes: the names would be a guess.
            Assert.Empty(Hints("fn tax(price, rate) => $price * $rate\necho @tax(100, 0.2, 3)"));
        }

        [Fact]
        public void Arithmetic_on_a_function_says_what_it_is()
        {
            DocumentAnalysis a = Service.Analyze("echo 5 - (fn($n) => $n)\n", "test.kscript");
            Assert.Contains(a.Diagnostics, d => d.Message.Contains("this is a function", StringComparison.Ordinal));
        }

        private static string Hover(LanguageService service, string textWithCaret, string path)
        {
            (string text, int offset) = TestHelpers.WithCaret(textWithCaret);
            return service.GetHover(service.Analyze(text, path), offset)?.Markdown;
        }

        [Fact]
        public void A_hover_shows_an_example_the_way_the_file_is_written()
        {
            string code = Hover(Service, "name = 'Joan'\necho $name->up‸per()", "test.kscript");
            Assert.Contains("**Example**\n```kscript\necho ", code, StringComparison.Ordinal);
            Assert.DoesNotContain("Open docs", code, StringComparison.Ordinal);

            string xml = Hover(Service, "<kscript><let name=\"name\" value=\"'Joan'\" /><echo>{ $name->up‸per() }</echo></kscript>", "test.ks.xml");
            Assert.Contains("**Example**\n```xml\n<echo>{ ", xml, StringComparison.Ordinal);
        }

        [Fact]
        public void A_hover_links_to_the_docs_when_the_editor_can_open_them()
        {
            LanguageService service = new LanguageService { DocsCommand = "kscript.openDocs" };
            string method = Hover(service, "name = 'Joan'\necho $name->up‸per()", "test.kscript");
            Assert.EndsWith("[Open docs](command:kscript.openDocs?%5B%22upper%22%5D)", method, StringComparison.Ordinal);
            string command = Hover(service, "echo @en‸v(name: 'HOME')", "test.kscript");
            Assert.Contains("(command:kscript.openDocs?%5B%22env%22%5D)", command, StringComparison.Ordinal);
        }

        [Fact]
        public void Text_times_a_number_is_text()
        {
            // 'ab' * 3 repeats the text, so the result has text's methods.
            Assert.Contains("upper", Labels("line = '-' * 20\necho $line->‸"));
            Assert.Equal("count", Labels("line = '-' * 20\necho $line->‸")[0]);
        }

        [Fact]
        public void A_function_shows_how_many_times_it_is_used()
        {
            const string text = "fn double(n) => $n * 2\nfn once(n) => $n\nfn never(n) => $n\necho @double(1) + @double(2)\necho @once(1)\n";
            LanguageService service = new LanguageService(files: EmptyFileSystem.Instance);
            DocumentAnalysis a = service.Analyze(text, Path.Combine(Path.GetTempPath(), "kscript-uses", "sums.kscript"));
            List<CodeLens> lenses = service.GetCodeLenses(a);
            Assert.Equal(new[] { "▶ Run", "2 uses", "1 use" }, lenses.Select(l => l.Title));
            CodeLens uses = lenses[1];
            Assert.Equal(LanguageService.ShowUsesCommand, uses.Command);
            Assert.Equal("double", text.Substring(uses.Target.Value.Start, uses.Target.Value.Length));
            Assert.Equal(0, uses.Span.Start);
            Assert.Null(lenses[0].Target);
        }

        private sealed class Folder : IFileSystem
        {
            public Dictionary<string, string> Files { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

            public bool FileExists(string path) => Files.ContainsKey(path);

            public bool DirectoryExists(string path) => Files.Keys.Any(f => Path.GetDirectoryName(f) == path);

            public string ReadText(string path) => Files.TryGetValue(path, out string t) ? t : null;

            public IEnumerable<string> ListFiles(string directory, string pattern) => Files.Keys.Where(f => Path.GetDirectoryName(f) == directory);
        }

        [Fact]
        public void A_function_only_another_file_calls_says_so()
        {
            string root = Path.Combine(Path.GetTempPath(), "kscript-uses-" + Guid.NewGuid().ToString("N"));
            string lib = Path.Combine(root, "strings.kscript");
            const string libText = "fn shout(text) => $text->upper()\nfn spare(text) => $text\n";
            Folder files = new Folder();
            files.Files[lib] = libText;
            files.Files[Path.Combine(root, "main.kscript")] = "import \"strings.kscript\"\necho @shout(text: 'hi')\n";

            LanguageService service = new LanguageService(files: files);
            List<CodeLens> lenses = service.GetCodeLenses(service.Analyze(libText, lib));
            Assert.Equal(new[] { "▶ Run", "used in other files" }, lenses.Select(l => l.Title));
        }
    }
}
