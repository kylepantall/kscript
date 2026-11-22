using KScript.Metadata;
using KScript.Runtime;
using KScript.Syntax;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Tests;

/// <summary>The parsers and text helpers, below the interpreter.</summary>
public class SyntaxTests
{
    [Fact]
    public void Diagnostics_tokens_and_errors_print_readably()
    {
        MarkupDocument doc = MarkupParser.Parse("<kscript><![CDATA[never closed", DocumentKind.Script);
        Diagnostic error = doc.Diagnostics.First(d => d.Message.Contains("CDATA"));
        Assert.Contains("This CDATA section is never closed with ]]>.", error.ToString());

        ParseResult parsed = ExpressionParser.ParseExpression("fn(1) => 2");
        Assert.Equal("fn(1) => 2", parsed.Text);
        Assert.Contains("Expected a parameter name such as x, found '1'.", parsed.Errors[0].ToString());

        Token token = Lexer.Tokenize("$x")[0];
        Assert.Equal("Variable 'x' [0..2)", token.ToString());
        Assert.Equal("def", Retired.Elements["def"].Name);
    }

    [Fact]
    public void Raw_text_elements_run_to_the_end_when_unclosed()
    {
        MarkupDocument doc = MarkupParser.Parse("<html><script>let x = 1 < 2", DocumentKind.Page);
        MarkupElement script = doc.DescendantElements().First(e => e.Name == "script");
        Assert.Equal("<script>", script.ToString());
        Assert.Contains("1 < 2", string.Concat(script.Children.OfType<MarkupText>().Select(t => t.Value)));
    }

    [Fact]
    public void Decoded_text_maps_offsets_both_ways()
    {
        var text = new DecodedText("abc", new[] { 10, 11, 12, 14 });
        Assert.Equal("abc", text.ToString());
        Assert.Equal(-1, text.FromSource(5));
        Assert.Equal(-1, text.FromSource(20));
        Assert.Equal(0, text.FromSource(10));
        Assert.Equal(3, text.FromSource(13));
    }

    [Fact]
    public void Source_text_and_spans_answer_questions()
    {
        var source = new SourceText("one\ntwo");
        Assert.Equal(2, source.LineCount);
        Assert.Equal(4, source.GetOffset(new LinePosition(1, 0)));
        Assert.Equal(0, source.GetOffset(-1, 3));
        Assert.Equal(7, source.GetOffset(9, 0));

        var span = new TextSpan(2, 3);
        Assert.False(span.IsEmpty);
        Assert.True(new TextSpan(4, 0).IsEmpty);
        Assert.True(span.ContainsStrict(2));
        Assert.False(span.ContainsStrict(5));
        Assert.Equal(new TextSpan(2, 3).GetHashCode(), span.GetHashCode());
    }

    [Fact]
    public void Math_and_markdown_commands()
    {
        Assert.Equal("12 0 true 3 true 2\n", ScriptRunner.RunBody(
            "<echo>{ @lcm(4, 6) } { @lcm(0, 3) } { @pick([1, 2, 3]) in [1, 2, 3] } { @shuffle([1, 2, 3])->count() } { @shuffle([1, 2, 3])->sort() == [1, 2, 3] } { @stdev([2, 4, 4, 4, 5, 5, 7, 9]) }</echo>",
            "", "KScript.Math"));
        Assert.Contains("needs a list with at least one item", ScriptRunner.Fails("<echo>{ @pick([]) }</echo>", "KScript.Math").Message);
        Assert.Equal("a\\*b \\_c\\_ &lt;i&gt; &amp;\n", ScriptRunner.RunBody("<echo>{ 'a*b _c_ &lt;i&gt; &amp;'->markdown_escape() } { ''->markdown_escape() }</echo>", "", "KScript.Markdown"));
    }
}
