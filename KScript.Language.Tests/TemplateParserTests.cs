using KScript.Language.Syntax;

using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests
{
    public class TemplateParserTests
    {
        private static string Describe(Template t) => string.Join(" | ", t.Segments.Select(s => s.Kind switch
        {
            TemplateSegmentKind.Text => "text:" + s.Text,
            TemplateSegmentKind.Interpolation => "{" + Print(s.Node) + "}",
            TemplateSegmentKind.RawInterpolation => "{!" + Print(s.Node) + "}",
            _ => "ref:" + Print(s.Node),
        }));

        [Theory]
        [InlineData("Hello, $name!", true, "text:Hello,  | ref:$name | text:!")]
        [InlineData("Hello, $name!", false, "text:Hello, $name!")]
        [InlineData("You have { $items->count() } items.", false, "text:You have  | {(-> $items count)} | text: items.")]
        [InlineData("{! $post.html }", false, "{!(. $post html)}")]
        [InlineData("Use {{ and }} for braces.", true, "text:Use { and } for braces.")]
        [InlineData("mail kyle@example.com today", true, "text:mail kyle@example.com today")]
        [InlineData("It's $name's turn", true, "text:It's  | ref:$name | text:'s turn")]
        [InlineData("@greet('World')", true, "ref:@greet('World')")]
        [InlineData("@home is not a call", true, "text:@home is not a call")]
        [InlineData("{ { a: 1 }.a }", false, "{(. {a:1} a)}")]
        [InlineData("/people/{$p.id}", false, "text:/people/ | {(. $p id)}")]
        [InlineData("costs $5", true, "text:costs $5")]
        public void SplitsTextAndExpressions(string text, bool bare, string expected) =>
            Assert.Equal(expected, Describe(TemplateParser.Parse(text, bare)));

        [Fact]
        public void SpansCoverTheBraces()
        {
            const string text = "a { $x } b";
            TemplateSegment segment = TemplateParser.Parse(text, false).Expressions.Single();
            Assert.Equal("{ $x }", text.Substring(segment.Span.Start, segment.Span.Length));
            Assert.Equal("$x", text.Substring(segment.Node.Span.Start, segment.Node.Span.Length));
        }

        [Fact]
        public void ReportsAnUnclosedBrace()
        {
            Template t = TemplateParser.Parse("Total { $a + 1", false);
            Assert.Contains("never closed", Assert.Single(t.Errors).Message);
        }

        [Fact]
        public void RecognisesASingleInterpolation()
        {
            Assert.True(TemplateParser.Parse("{$done}", false).IsSingleInterpolation);
            Assert.False(TemplateParser.Parse("x {$done}", false).IsSingleInterpolation);
        }
    }
}
