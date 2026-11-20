using KScript.Language.Features;

namespace KScript.Language.Tests
{
    /// <summary>Completion offered as a word is typed, with no trigger character: statements, functions, commands and variables.</summary>
    public class CompletionWhileTypingTests
    {
        private static readonly LanguageService Service = new LanguageService();

        private static List<CompletionItem> Complete(string textWithCaret, string path = "test.kscript")
        {
            (string text, int offset) = TestHelpers.WithCaret(textWithCaret);
            return Service.GetCompletions(Service.Analyze(text, path), offset);
        }

        private static CompletionItem Item(List<CompletionItem> items, string label) => Assert.Single(items, i => i.Label == label);

        [Fact]
        public void A_word_starting_a_kscript_statement_offers_keywords_commands_functions_and_variables()
        {
            List<CompletionItem> items = Complete("fn greet(name) => 'Hi ' + $name\ncount = 1\nconst limit = 3\ne‸");

            CompletionItem echo = Item(items, "echo");
            Assert.Equal(CompletionKind.Keyword, echo.Kind);
            Assert.Equal("echo $0", echo.InsertText);
            Assert.True(echo.IsSnippet);
            Assert.Equal("0echo", echo.SortText);
            Assert.Equal(1, echo.ReplaceSpan.Length);

            Assert.Contains(items, i => i.Label == "if" && i.InsertText.StartsWith("if ${1:condition} {", StringComparison.Ordinal));
            Assert.Contains(items, i => i.Label == "for" && i.InsertText.Contains("in ${2:\\$list}", StringComparison.Ordinal));
            CompletionItem exit = Item(items, "exit");
            Assert.Null(exit.InsertText);
            Assert.False(exit.IsSnippet);
            // Planned elements don't run yet, so they aren't offered.
            Assert.DoesNotContain(items, i => i.Label == "render");

            // Commands and functions keep their @, but match on the bare name the user is typing.
            CompletionItem env = Item(items, "@env");
            Assert.Equal("env", env.FilterText);
            Assert.StartsWith("@env(", env.InsertText, StringComparison.Ordinal);
            Assert.Equal("greet", Item(items, "@greet").FilterText);

            // x = 3 assigns, so variables come by their bare names; a const can't be assigned.
            Assert.Equal(CompletionKind.Variable, Item(items, "count").Kind);
            Assert.DoesNotContain(items, i => i.Label == "limit");

            // Written another way in a .kscript file, or only after a }.
            foreach (string missing in new[] { "let", "set", "do", "kscript", "field", "else", "elif", "catch", "return", "break" })
            {
                Assert.DoesNotContain(items, i => i.Label == missing);
            }
        }

        [Fact]
        public void Statement_completion_follows_where_the_statement_is()
        {
            List<CompletionItem> inFn = Complete("fn total(list) {\n  r‸\n}");
            Assert.Contains(inFn, i => i.Label == "return" && i.InsertText == "return $0");
            Assert.Equal(CompletionKind.Parameter, Item(inFn, "list").Kind);

            List<CompletionItem> inLoop = Complete("for n in [1, 2] {\n  b‸\n}");
            Assert.Contains(inLoop, i => i.Label == "break");
            Assert.Equal(CompletionKind.Variable, Item(inLoop, "n").Kind);

            Assert.Contains(Complete("x = 1; e‸"), i => i.Label == "echo");
            Assert.Contains(Complete("if true { e‸ }"), i => i.Label == "echo");
            Assert.Contains(Complete("ech‸o 'hi'"), i => i.Label == "echo");
            Assert.Contains(Complete("x = 1\nnot‸"), i => i.Label == "not" && i.Kind == CompletionKind.Keyword);
        }

        [Fact]
        public void Words_that_dont_start_a_statement_keep_their_own_completion()
        {
            // An expression: $ and @ items, matched on the bare name.
            List<CompletionItem> value = Complete("count = 1\necho e‸");
            Assert.DoesNotContain(value, i => i.Label == "echo");
            Assert.Equal("count", Item(value, "$count").FilterText);
            Assert.Equal("env", Item(value, "@env").FilterText);

            Assert.Empty(Complete("// e‸"));
            Assert.Empty(Complete("/* a note\ne‸ */"));
            Assert.Empty(Complete("<kscript>\n1‸\n</kscript>", "test.ks.xml"));
        }

        [Fact]
        public void A_word_on_its_own_line_in_an_xml_script_offers_elements_with_their_bracket()
        {
            List<CompletionItem> items = Complete("<kscript>\n  <let name=\"x\" value=\"1\" />\n  e‸\n</kscript>", "test.ks.xml");
            CompletionItem echo = Item(items, "echo");
            Assert.Equal("echo", echo.FilterText);
            Assert.StartsWith("<echo>", echo.InsertText, StringComparison.Ordinal);
            Assert.Contains(items, i => i.Label == "if" && i.InsertText.StartsWith("<if $1>", StringComparison.Ordinal));
            Assert.DoesNotContain(items, i => i.Label == "else");

            Assert.Contains(Complete("<kscript><if test=\"true\">e‸</if></kscript>", "test.ks.xml"), i => i.Label == "echo");
            Assert.DoesNotContain(Complete("<kscript>\n<echo>hi e‸</echo>\n</kscript>", "test.ks.xml"), i => i.Label == "echo");

            // In a page, text is the page's own: no element completions for a bare word.
            Assert.Empty(Complete("<p>\ne‸\n</p>", "index.html"));
        }
    }
}
