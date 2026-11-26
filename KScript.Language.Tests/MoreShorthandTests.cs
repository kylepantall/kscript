using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Syntax;

namespace KScript.Language.Tests
{
    /// <summary>The checker for @if and @each on a tag, short functions, expect's value first and the retired spellings.</summary>
    public class MoreShorthandTests
    {
        private static readonly string Root = Path.Combine(Path.GetTempPath(), "kscript-more-shorthands");
        private static readonly LanguageService Service = new(files: new MemoryFiles());

        private static DocumentAnalysis Analyze(string text, string file) => Service.Analyze(text, Path.Combine(Root, file));

        private static string Fixed(DocumentAnalysis doc, Diagnostic diagnostic)
        {
            string text = doc.Source.Text;
            foreach (TextChange change in Assert.Single(diagnostic.Fixes).Changes.OrderByDescending(c => c.Span.Start))
                text = text[..change.Span.Start] + change.NewText + text[change.Span.End..];
            return text;
        }

        private const string Live = "<live id=\"b\"><state name=\"draft\" value=\"''\" /><state name=\"tasks\" value=\"[{ title: 'a', done: false }]\" />";

        [Fact]
        public void Each_names_its_item_for_the_tag_and_if_reads_it()
        {
            DocumentAnalysis doc = Analyze(Live + "<li @each($tasks, as: task, at: i) @if(not $task.done)>{$i} {$task.title}</li></live>", "index.html");
            Assert.Empty(doc.Errors);
            DocumentAnalysis outside = Analyze(Live + "<li @each($tasks, as: task)>{$task.title}</li><p>{$task}</p></live>", "index.html");
            Assert.Contains(outside.Errors, e => e.Message.Contains("$task is not defined"));
        }

        [Theory]
        [InlineData("<li @each($tasks)>x</li>", "@each needs as:")]
        [InlineData("<li @each($tasks, as: task, by: 2)>x</li>", "@each takes the list")]
        public void A_wrong_each_is_reported(string tag, string message)
        {
            Assert.Contains(Analyze(Live + tag + "</live>", "index.html").Errors, e => e.Message.Contains(message));
        }

        [Theory]
        [InlineData("<input @bind('draft')>", "<input @bind($draft)>")]
        [InlineData("<input bind=\"draft\">", "<input bind=\"$draft\">")]
        [InlineData("<input @bind($draft) @bind-on('blur')>", "<input @bind($draft, on: blur)>")]
        [InlineData("<button @on-click(reset: $draft) live-confirm=\"Sure?\">x</button>", "<button @on-click(reset: $draft, confirm: 'Sure?')>x</button>")]
        [InlineData("<input @on-keydown(reset: $draft, key: escape) prevent-default=\"keydown\">", "<input @on-keydown(reset: $draft, key: escape, prevent: true)>")]
        public void An_old_spelling_is_an_error_with_a_fix_to_the_one_that_replaced_it(string tag, string expected)
        {
            DocumentAnalysis doc = Analyze(Live + tag + "</live>", "index.html");
            Diagnostic old = Assert.Single(doc.Errors);
            Assert.Equal(DiagnosticCodes.OneSpelling, old.Code);
            Assert.Equal(Live + expected + "</live>", Fixed(doc, old).Replace(" >", ">"));
        }

        [Fact]
        public void Expect_takes_the_value_first_and_value_colon_is_an_old_spelling()
        {
            const string ok = "import KScript.Test\n\ntest(name: \"adds\") {\n  total = 1 + 2\n  expect($total, equals: 3)\n}\n";
            Assert.Empty(Analyze(ok, "adds.test.kscript").Errors);
            DocumentAnalysis old = Analyze(ok.Replace("expect($total", "expect(value: $total"), "adds.test.kscript");
            Diagnostic spelling = Assert.Single(old.Errors, e => e.Code == DiagnosticCodes.OneSpelling);
            Assert.Equal(ok, Fixed(old, spelling));
        }

        [Fact]
        public void A_short_function_is_checked_like_the_lambda_it_stands_for()
        {
            const string script = "names = ['a ', ' b']->map(->trim())\nfirst = [{ n: 1 }]->map(.n)\necho \"{$names} {$first}\"\n";
            Assert.Empty(Analyze(script, "short.kscript").Errors);
            Assert.NotEmpty(Analyze(script.Replace("->trim()", "->nope()"), "short.kscript").Errors);
        }
    }
}
