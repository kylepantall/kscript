using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Language.Formatting;
using KScript.Syntax;
using KScript.Syntax.Markup;

using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests
{
    /// <summary>
    /// <c>&lt;if $ready&gt;</c>: the condition written straight in the start tag, without <c>test="…"</c>.
    /// </summary>
    public class ShorthandConditionTests
    {
        private static readonly LanguageService Service = new LanguageService(files: EmptyFileSystem.Instance);

        /// <summary>Scans "&lt;if …" and returns the expression and what is left after the tag.</summary>
        private static (string Expression, string After, bool Closed, bool SelfClosing) Scan(string tag)
        {
            int afterName = tag.IndexOfAny(new[] { ' ', '\t', '\n', '>', '/' });
            Assert.True(ShorthandCondition.Scan(tag, afterName, out ShorthandCondition.Head head), "Expected a shorthand head in " + tag);
            return (tag.Substring(head.Start, head.End - head.Start), tag.Substring(head.TagEnd), head.Closed, head.SelfClosing);
        }

        // ---- where the tag ends -----------------------------------------------------------------------------------

        [Theory]
        [InlineData("<if $user.is_logged_in>body", "$user.is_logged_in", "body")]
        [InlineData("<if $n > 3>body", "$n > 3", "body")]
        [InlineData("<if $n >= 3>body", "$n >= 3", "body")]
        [InlineData("<if $n < 3>body", "$n < 3", "body")]
        [InlineData("<if $n <= 3 and $m <$k>body", "$n <= 3 and $m <$k", "body")]
        [InlineData("<if $list->count() gt 0>body", "$list->count() gt 0", "body")]
        [InlineData("<if $list->any(fn(x) => $x > 1)>body", "$list->any(fn(x) => $x > 1)", "body")]
        [InlineData("<if ($a>1)>body", "($a>1)", "body")]
        [InlineData("<if [1, 2][0]>body", "[1, 2][0]", "body")]
        [InlineData("<if { a: 1 }.a>body", "{ a: 1 }.a", "body")]
        [InlineData("<if $name == '>'>body", "$name == '>'", "body")]
        [InlineData("<if $name == \"a > b\">body", "$name == \"a > b\"", "body")]
        [InlineData("<if   $ok   >body", "$ok", "body")]
        [InlineData("<if $ok >\n  <p>", "$ok", "\n  <p>")]
        [InlineData("<if $ok > <p>", "$ok", " <p>")]
        [InlineData("<if $ok >Hi", "$ok", "Hi")]
        [InlineData("<if $a>$b>", "$a", "$b>")]
        [InlineData("<if true>", "true", "")]
        [InlineData("<if count == 1>", "count == 1", "")]
        [InlineData("<if f => 1>", "f => 1", "")]
        [InlineData("<if\n  $a and\n  $b\n>x", "$a and\n  $b", "x")]
        public void FindsTheEndOfTheTag(string tag, string expression, string rest)
        {
            var r = Scan(tag);
            Assert.Equal(expression, r.Expression);
            Assert.Equal(rest, r.After);
            Assert.True(r.Closed);
            Assert.False(r.SelfClosing);
        }

        [Fact]
        public void ASpacedGreaterThanThatRunsIntoTheNextTagEndsTheTagAfterAll()
        {
            var r = Scan("<if $ok > Yes</if>");
            Assert.Equal("$ok", r.Expression);
            Assert.Equal(" Yes</if>", r.After);
            Assert.True(r.Closed);

            r = Scan("<if $ok > Yes");
            Assert.Equal("$ok", r.Expression);
            Assert.True(r.Closed);
        }

        [Theory]
        [InlineData("<match $total eq 30 />", "$total eq 30")]
        [InlineData("<match $total eq 30/>", "$total eq 30")]
        [InlineData("<match $a / 2 eq 1 />", "$a / 2 eq 1")]
        public void ReadsSelfClosingTags(string tag, string expression)
        {
            var r = Scan(tag);
            Assert.Equal(expression, r.Expression);
            Assert.Equal(string.Empty, r.After);
            Assert.True(r.SelfClosing);
        }

        [Theory]
        [InlineData("<if $ok", "$ok", "")]
        [InlineData("<if $ok <p>x", "$ok", "<p>x")]
        [InlineData("<if $ok </if>", "$ok", "</if>")]
        [InlineData("<if $ok <!-- c -->", "$ok", "<!-- c -->")]
        [InlineData("<if 'never closed", "'never closed", "")]
        public void StopsAtTheNextTagWhenTheTagIsNeverClosed(string tag, string expression, string rest)
        {
            var r = Scan(tag);
            Assert.Equal(expression, r.Expression);
            Assert.Equal(rest, r.After);
            Assert.False(r.Closed);
        }

        [Theory]
        [InlineData("<if test=\"$x\">")]
        [InlineData("<if test = \"$x\">")]
        [InlineData("<if data-x=\"1\">")]
        [InlineData("<if>")]
        [InlineData("<if />")]
        [InlineData("<if   >")]
        [InlineData("<if")]
        [InlineData("<if ")]
        public void LeavesOrdinaryTagsAlone(string tag) =>
            Assert.False(ShorthandCondition.Scan(tag, 3, out _));

        [Fact]
        public void KnowsWhichElementsHaveAShorthand()
        {
            Assert.Equal("test", ShorthandCondition.AttributeFor("if"));
            Assert.Equal("test", ShorthandCondition.AttributeFor("elif"));
            Assert.Equal("test", ShorthandCondition.AttributeFor("while"));
            Assert.Null(ShorthandCondition.AttributeFor("assert")); // @assert() is a command
            Assert.Equal("value", ShorthandCondition.AttributeFor("match"));
            Assert.Null(ShorthandCondition.AttributeFor("for"));
            Assert.Null(ShorthandCondition.AttributeFor("IF"));
            Assert.All(ShorthandCondition.Elements, e => Assert.NotNull(ShorthandCondition.AttributeFor(e)));
        }

        [Theory]
        [InlineData("$ready", true)]
        [InlineData("  $n > 3  ", true)]
        [InlineData("$list->any(fn(x) => $x>1)", true)]
        [InlineData("$a>3", false)]
        [InlineData("$a >3", false)]
        [InlineData("$a > 3 /", false)]
        [InlineData("'Tom & Jerry' == $name", false)]
        [InlineData("$a and\n$b", false)]
        [InlineData("$a <b", false)]
        [InlineData("test=\"x\"", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void KnowsWhenAnExpressionCanBeWrittenAsAShorthand(string expression, bool expected) =>
            Assert.Equal(expected, ShorthandCondition.CanWrite(expression));

        // ---- the markup parser ------------------------------------------------------------------------------------

        [Fact]
        public void ScriptsReadTheShorthandAsATestAttribute()
        {
            const string text = "<kscript>\n  <if $n > 3>\n    <echo>big</echo>\n  </if>\n</kscript>";
            MarkupDocument doc = MarkupParser.Parse(text, DocumentKind.Script);
            Assert.Empty(doc.Diagnostics);

            MarkupElement e = doc.RootElements.Single().ChildElements.Single();
            Assert.True(e.StartTagClosed);
            MarkupAttribute test = Assert.Single(e.Attributes);
            Assert.Equal("test", test.Name);
            Assert.True(test.IsShorthand);
            Assert.Equal("$n > 3", test.ValueText);
            Assert.Equal('\0', test.Quote);
            Assert.Equal(0, test.NameSpan.Length);
            Assert.Equal(text.IndexOf("$n", StringComparison.Ordinal), test.NameSpan.Start);
            Assert.Equal("$n > 3", text.Substring(test.Span.Start, test.Span.Length));
            Assert.Equal("$n > 3", text.Substring(test.Value.SourceSpan.Start, test.Value.SourceSpan.Length));
            Assert.Equal("<if $n > 3>", text.Substring(e.StartTagSpan.Start, e.StartTagSpan.Length));
            Assert.Equal("echo", e.ChildElements.Single().Name);
        }

        [Fact]
        public void ScriptsAcceptRawOperatorsAndQuotesInTheShorthand()
        {
            MarkupDocument doc = MarkupParser.Parse("<if $a < 1 and $b == \"x\" />", DocumentKind.Script);
            Assert.Empty(doc.Diagnostics);
            MarkupElement e = doc.RootElements.Single();
            Assert.True(e.SelfClosing);
            Assert.Equal("$a < 1 and $b == \"x\"", e.GetAttributeValue("test"));
        }

        [Fact]
        public void EntitiesInTheShorthandAreDecoded()
        {
            const string text = "<if $a &gt; 1>x</if>";
            MarkupAttribute test = MarkupParser.Parse(text, DocumentKind.Script).RootElements.Single().GetAttribute("test");
            Assert.Equal("$a > 1", test.ValueText);
            Assert.Equal(text.IndexOf("&gt;", StringComparison.Ordinal), test.Value.ToSource(3));
        }

        [Fact]
        public void TheTestAttributeStillWorks()
        {
            MarkupAttribute test = MarkupParser.Parse("<if test=\"$ok\">x</if>", DocumentKind.Script).RootElements.Single().GetAttribute("test");
            Assert.Equal("$ok", test.ValueText);
            Assert.False(test.IsShorthand);
            Assert.Equal("test", "<if test=\"$ok\">x</if>".Substring(test.NameSpan.Start, test.NameSpan.Length));
        }

        [Fact]
        public void MatchTakesItsValueInTheStartTag()
        {
            MarkupElement match = MarkupParser.Parse("<match $choice><default /></match>", DocumentKind.Script).RootElements.Single();
            Assert.Equal("$choice", match.GetAttributeValue("value"));
            Assert.Null(match.GetAttribute("test"));
        }

        [Fact]
        public void OtherElementsKeepTheXmlRules()
        {
            MarkupDocument doc = MarkupParser.Parse("<echo $x>hi</echo>", DocumentKind.Script);
            Assert.Contains(doc.Diagnostics, d => d.Code == DiagnosticCodes.InvalidAttribute);
        }

        [Fact]
        public void AnUnclosedShorthandIsReported()
        {
            const string text = "<kscript>\n  <if $ok\n  <echo>x</echo>\n</kscript>";
            MarkupDocument doc = MarkupParser.Parse(text, DocumentKind.Script);
            MarkupElement e = doc.RootElements.Single().ChildElements.First();
            Assert.False(e.StartTagClosed);
            Assert.Equal("$ok", e.GetAttributeValue("test"));
            Assert.Contains(doc.Diagnostics, d => d.Code == DiagnosticCodes.MarkupSyntax && d.Message.Contains("<if>"));
        }

        [Fact]
        public void PagesReadTheShorthandAndKeepItsCase()
        {
            const string text = "<ul><IF User.isLoggedIn><li><a href=\"/profile\">Profile</a></li></IF></ul>";
            MarkupDocument doc = MarkupParser.Parse(text, DocumentKind.Page);
            Assert.Empty(doc.Diagnostics);
            MarkupElement e = doc.RootElements.Single().ChildElements.Single();
            Assert.Equal("User.isLoggedIn", e.GetAttributeValue("test"));
            Assert.Equal("li", e.ChildElements.Single().Name);
        }

        // ---- the analyzer and editor features ---------------------------------------------------------------------

        [Fact]
        public void TheShorthandIsAnalyzedLikeTheTestAttribute()
        {
            AssertClean(Analyze(Script("<let name=\"n\" value=\"5\" />\n<if $n > 3><echo>big</echo></if>\n<elif $n lt 2><echo>small</echo></elif>\n<while $n lt 3><break /></while>\n<match $n><default /></match>")));

            string text = Script("<if $missing > 3><echo>x</echo></if>");
            DocumentAnalysis a = Analyze(text);
            Diagnostic d = AssertSingle(a, DiagnosticCodes.UndefinedVariable);
            Assert.Equal("missing", a.Source[d.Span]);
        }

        [Fact]
        public void AnEmptyIfStillNeedsACondition()
        {
            DocumentAnalysis a = Analyze(Script("<if><echo>x</echo></if>"));
            Assert.Contains(a.Diagnostics, d => d.Code == DiagnosticCodes.MissingAttribute);
        }

        [Fact]
        public void PageShorthandsAreAnalyzed()
        {
            DocumentAnalysis a = Analyze("<if @no_such_thing()><p>x</p></if>", "index.html");
            Diagnostic d = Assert.Single(a.Diagnostics);
            Assert.Equal(Analyze("<if test=\"@no_such_thing()\"><p>x</p></if>", "index.html").Diagnostics.Single().Code, d.Code);
            Assert.Contains("no_such_thing", a.Source[d.Span]);
        }

        [Fact]
        public void CompletionInsideTheShorthandOffersVariablesNotAttributes()
        {
            (string text, int offset) = WithCaret(Script("<let name=\"ready\" value=\"true\" />\n<if $re‸><echo>x</echo></if>"));
            List<CompletionItem> items = Service.GetCompletions(Service.Analyze(text, "test.ks.xml"), offset);
            Assert.Contains(items, i => i.Label == "$ready" || i.Label == "ready");
            Assert.DoesNotContain(items, i => i.Kind == CompletionKind.Attribute);

            (text, offset) = WithCaret(Script("<let name=\"ready\" value=\"true\" />\n<if ‸$ready><echo>x</echo></if>"));
            items = Service.GetCompletions(Service.Analyze(text, "test.ks.xml"), offset);
            Assert.DoesNotContain(items, i => i.Kind == CompletionKind.Attribute);
        }

        [Fact]
        public void ElementSnippetsUseTheShorthand()
        {
            (string text, int offset) = WithCaret(Script("<‸"));
            List<CompletionItem> items = Service.GetCompletions(Service.Analyze(text, "test.ks.xml"), offset);
            Assert.Equal("if $1>\n\t$0\n</if>", items.Single(i => i.Label == "if").InsertText);
            Assert.Equal("while $1>\n\t$0\n</while>", items.Single(i => i.Label == "while").InsertText);
            Assert.Equal("match $1>\n\t$0\n</match>", items.Single(i => i.Label == "match").InsertText);
            Assert.Contains("each=", items.Single(i => i.Label == "for").InsertText);
        }

        [Fact]
        public void HoverWorksInsideTheShorthand()
        {
            (string text, int offset) = WithCaret(Script("<let name=\"items\" value=\"[1]\" />\n<if $items->cou‸nt() gt 0><echo>x</echo></if>"));
            HoverResult hover = Service.GetHover(Service.Analyze(text, "test.ks.xml"), offset);
            Assert.NotNull(hover);
            Assert.Contains("count", hover.Markdown);
        }

        [Fact]
        public void SemanticTokensCoverTheShorthand()
        {
            DocumentAnalysis a = Service.Analyze(Script("<let name=\"n\" value=\"1\" />\n<if $n->round() gt 2><echo>x</echo></if>"), "test.ks.xml");
            List<SemanticToken> tokens = Service.GetSemanticTokens(a);
            Assert.Contains(tokens, t => a.Source[t.Span] == "round" && t.Type == SemanticTokenType.Method);
            Assert.Contains(tokens, t => a.Source[t.Span] == "gt" && t.Type == SemanticTokenType.Keyword);
        }

        // ---- the formatter and converter --------------------------------------------------------------------------

        [Fact]
        public void TheFormatterKeepsTheShorthand()
        {
            const string text = "<kscript>\n<let name=\"n\" value=\"5\" />\n<if   $n > 3  ><echo>big</echo></if>\n<while   $n > 9><break /></while>\n</kscript>\n";
            FormatResult result = Formatter.Format(text, "test.ks.xml");
            Assert.True(result.Succeeded, result.Error);
            Assert.Equal("<kscript>\n  <let name=\"n\" value=\"5\" />\n  <if $n > 3>\n    <echo>big</echo>\n  </if>\n  <while $n > 9>\n    <break />\n  </while>\n</kscript>\n", result.Text);
            Assert.Equal(result.Text, Formatter.Format(result.Text, "test.ks.xml").Text);
        }

        [Fact]
        public void TheFormatterKeepsPageShorthands()
        {
            const string text = "<ul>\n<if $user.is_logged_in>\n<li><a href=\"/profile\">Profile</a></li>\n</if>\n</ul>\n";
            FormatResult result = Formatter.Format(text, "index.html");
            Assert.True(result.Succeeded, result.Error);
            Assert.Contains("<if $user.is_logged_in>", result.Text);
        }

        [Fact]
        public void ConvertingToXmlWritesTheShorthand()
        {
            ConversionResult r = ScriptConverter.Convert("n = 5\nif $n > 3 {\n  echo \"big\"\n} elif $n gt 1 {\n  echo \"mid\"\n}\nwhile $n lt 3 {\n  break\n}\nmatch $n {\n  5 => {\n    echo \"five\"\n  }\n}\n", "a.kscript");
            Assert.Contains("<if $n > 3>", r.Text);
            Assert.Contains("<elif $n gt 1>", r.Text);
            Assert.Contains("<while $n lt 3>", r.Text);
            Assert.Contains("<match $n>", r.Text);
            Assert.DoesNotContain("test=", r.Text);
        }

        [Fact]
        public void ConvertingToXmlFallsBackToTestWhenTheShorthandWouldNotReadBack()
        {
            ConversionResult r = ScriptConverter.Convert("name = 'x'\nif $name == 'Tom & Jerry' {\n  echo \"yes\"\n}\n", "a.kscript");
            Assert.Contains("<if test=\"$name == 'Tom &amp; Jerry'\">", r.Text);
        }

        [Fact]
        public void ShorthandScriptsConvertToKScriptAndBack()
        {
            const string xml = "<kscript>\n  <let name=\"n\" value=\"5\" />\n  <if $n > 3>\n    <echo>big</echo>\n  </if>\n  <match $n>\n    <case is=\"5\">\n      <echo>five</echo>\n    </case>\n  </match>\n</kscript>\n";
            ConversionResult code = ScriptConverter.Convert(xml, "a.ks.xml");
            Assert.Contains("if $n > 3 {", code.Text);
            Assert.Contains("match $n {", code.Text);
            Assert.Equal(xml, ScriptConverter.Convert(code.Text, "a.kscript").Text);
        }
    }
}
