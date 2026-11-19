using KScript.Language.Analysis;
using KScript.Language.Markup;

namespace KScript.Language.Tests
{
    public class MarkupParserTests
    {
        private static MarkupDocument Xml(string text) => MarkupParser.Parse(text, DocumentKind.Script);

        private static MarkupDocument Html(string text) => MarkupParser.Parse(text, DocumentKind.Page);

        [Fact]
        public void RecordsExactPositions()
        {
            const string text = "<kscript>\n  <let name=\"x\" value=\"1\" />\n  <echo>hi</echo>\n</kscript>";
            MarkupDocument doc = Xml(text);
            Assert.Empty(doc.Diagnostics);

            MarkupElement root = Assert.Single(doc.RootElements);
            Assert.Equal("kscript", text.Substring(root.NameSpan.Start, root.NameSpan.Length));
            Assert.Equal(text.Length, root.Span.End);
            Assert.Equal("kscript", text.Substring(root.EndNameSpan.Value.Start, root.EndNameSpan.Value.Length));

            MarkupElement let = root.ChildElements.First();
            Assert.True(let.SelfClosing);
            MarkupAttribute value = let.GetAttribute("value");
            Assert.Equal("1", value.ValueText);
            Assert.Equal("1", text.Substring(value.Value.SourceSpan.Start, value.Value.SourceSpan.Length));
            Assert.Equal("value=\"1\"", text.Substring(value.Span.Start, value.Span.Length));
            Assert.Equal('"', value.Quote);

            MarkupElement echo = root.ChildElements.Last();
            Assert.Equal("hi", echo.SingleText.Value);
            Assert.Equal("hi", text.Substring(echo.ContentSpan.Start, echo.ContentSpan.Length));
        }

        [Fact]
        public void DecodesEntitiesAndMapsBackToTheSource()
        {
            const string text = "<if test=\"$a &gt; 1 &amp;&amp; $b\" />";
            MarkupAttribute test = Xml(text).RootElements.Single().GetAttribute("test");
            Assert.Equal("$a > 1 && $b", test.ValueText);
            int b = test.ValueText.IndexOf("$b", StringComparison.Ordinal);
            Assert.Equal(text.IndexOf("$b", StringComparison.Ordinal), test.Value.ToSource(b));
            Assert.Equal(text.IndexOf("&gt;", StringComparison.Ordinal), test.Value.ToSource(test.ValueText.IndexOf('>')));
            Assert.Equal(b, test.Value.FromSource(text.IndexOf("$b", StringComparison.Ordinal)));
        }

        [Fact]
        public void DecodesNumericEntities() =>
            Assert.Equal("Aé", Xml("<echo>&#65;&#xE9;</echo>").RootElements.Single().SingleText.Value);

        [Theory]
        [InlineData("<a><b></a>", DiagnosticCodes.UnclosedElement)]
        [InlineData("<a></b>", DiagnosticCodes.MismatchedEndTag)]
        [InlineData("<a>", DiagnosticCodes.UnclosedElement)]
        [InlineData("<a x=1 />", DiagnosticCodes.InvalidAttribute)]
        [InlineData("<a x />", DiagnosticCodes.InvalidAttribute)]
        [InlineData("<a x=\"1\" x=\"2\" />", DiagnosticCodes.DuplicateAttribute)]
        [InlineData("<a test=\"$a < 1\" />", DiagnosticCodes.InvalidAttribute)]
        [InlineData("<a x=\"open />", DiagnosticCodes.InvalidAttribute)]
        [InlineData("<a>1 < 2</a>", DiagnosticCodes.MarkupSyntax)]
        [InlineData("<a>Tom & Jerry</a>", DiagnosticCodes.MarkupSyntax)]
        [InlineData("<a><!-- open", DiagnosticCodes.MarkupSyntax)]
        [InlineData("<a", DiagnosticCodes.MarkupSyntax)]
        public void ReportsXmlErrors(string text, string code)
        {
            MarkupDocument doc = Xml(text);
            Assert.Contains(doc.Diagnostics, d => d.Code == code);
        }

        [Fact]
        public void KeepsGoingAfterAnError()
        {
            MarkupDocument doc = Xml("<kscript><a x=1><echo>hi</echo></a><echo>after</echo></kscript>");
            Assert.NotEmpty(doc.Diagnostics);
            Assert.Equal(2, doc.DescendantElements().Count(e => e.Name == "echo"));
        }

        [Fact]
        public void UnfinishedTagIsStillAnElement()
        {
            MarkupDocument doc = Xml("<kscript>\n  <ec\n</kscript>");
            MarkupElement typing = doc.DescendantElements().Single(e => e.Name == "ec");
            Assert.False(typing.StartTagClosed);
        }

        [Fact]
        public void CDataAndCommentsAreKept()
        {
            MarkupDocument doc = Xml("<kscript><!-- note --><echo><![CDATA[a < b & c]]></echo></kscript>");
            Assert.Empty(doc.Diagnostics);
            MarkupText text = doc.DescendantElements().Single(e => e.Name == "echo").Children.OfType<MarkupText>().Single();
            Assert.True(text.IsCData);
            Assert.Equal("a < b & c", text.Value);
            Assert.Single(doc.DescendantNodes().OfType<MarkupComment>());
        }

        [Fact]
        public void HtmlAllowsVoidElementsAndLooseAttributes()
        {
            MarkupDocument doc = Html("<!doctype html><p>Hi<br><input type=checkbox checked><img src=a.png></p>");
            Assert.Empty(doc.Diagnostics);
            MarkupElement p = doc.RootElements.Single();
            Assert.Equal(new[] { "br", "input", "img" }, p.ChildElements.Select(e => e.Name));
            MarkupElement input = p.ChildElements.ElementAt(1);
            Assert.Equal("checkbox", input.GetAttributeValue("type"));
            Assert.Null(input.GetAttribute("checked").Value);
        }

        [Fact]
        public void HtmlClosesOptionalEndTagsQuietly()
        {
            MarkupDocument doc = Html("<ul><li>One<li>Two</ul><p>After");
            Assert.Empty(doc.Diagnostics);
        }

        [Fact]
        public void HtmlStillReportsUnclosedKScriptElements()
        {
            MarkupDocument doc = Html("<div><if test=\"$a\"><p>x</p></div>");
            Assert.Contains(doc.Diagnostics, d => d.Code == DiagnosticCodes.UnclosedElement && d.Message.Contains("<if>"));
        }

        [Fact]
        public void HtmlScriptAndStyleAreRawText()
        {
            MarkupDocument doc = Html("<script>if (a < b && c) { x(); }</script><style>p > a { }</style>");
            Assert.Empty(doc.Diagnostics);
            Assert.All(doc.RootElements, e => Assert.True(e.Children.OfType<MarkupText>().Single().IsCData));
        }

        [Fact]
        public void KScriptScriptBlocksAreParsedAsMarkup()
        {
            MarkupDocument doc = Html("<script type=\"text/kscript\"><kscript><echo>hi</echo></kscript></script>");
            Assert.Contains(doc.DescendantElements(), e => e.Name == "echo");
        }

        [Fact]
        public void MarkdownReadsFrontMatter()
        {
            const string text = "---\ntitle: Team\nlayout: _layout.html\n---\n# Hello { $title }\n";
            MarkupDocument doc = MarkupParser.Parse(text, DocumentKind.Markdown);
            Assert.Empty(doc.Diagnostics);
            Assert.Equal(new[] { "title", "layout" }, doc.FrontMatter.Entries.Select(e => e.Key));
            FrontMatterEntry title = doc.FrontMatter.Entries[0];
            Assert.Equal("Team", title.Value.Value);
            Assert.Equal("title", text.Substring(title.KeySpan.Start, title.KeySpan.Length));
            Assert.Contains("# Hello", doc.Children.OfType<MarkupText>().Single().Value);
        }

        [Fact]
        public void MarkdownTreatsStrayAngleBracketsAsText()
        {
            MarkupDocument doc = MarkupParser.Parse("1 < 2 and 3 > 2\n", DocumentKind.Markdown);
            Assert.Empty(doc.Diagnostics);
        }

        [Theory]
        [InlineData("a.ks.xml", DocumentKind.Script)]
        [InlineData("a.xml", DocumentKind.Script)]
        [InlineData("a.html", DocumentKind.Page)]
        [InlineData("A.HTM", DocumentKind.Page)]
        [InlineData("post.md", DocumentKind.Markdown)]
        [InlineData(null, DocumentKind.Script)]
        public void ChoosesKindFromPath(string path, DocumentKind kind) => Assert.Equal(kind, MarkupParser.KindFromPath(path));

        [Fact]
        public void FindsTheInnermostElement()
        {
            const string text = "<kscript><if test=\"1\"><echo>x</echo></if></kscript>";
            MarkupDocument doc = Xml(text);
            Assert.Equal("echo", doc.FindElementAt(text.IndexOf('x')).Name);
            Assert.Equal("kscript", doc.FindElementAt(text.IndexOf("</kscript>", StringComparison.Ordinal)).Name);
        }

        [Fact]
        public void HandlesWindowsLineEndings()
        {
            const string text = "<kscript>\r\n  <echo>hi</echo>\r\n</kscript>";
            MarkupDocument doc = Xml(text);
            Assert.Empty(doc.Diagnostics);
            MarkupElement echo = doc.DescendantElements().Single(e => e.Name == "echo");
            Assert.Equal(1, doc.Source.GetPosition(echo.NameSpan.Start).Line);
            Assert.Equal(3, doc.Source.GetPosition(echo.NameSpan.Start).Character);
        }

        [Fact]
        public void NeverThrowsOnArbitraryMarkup()
        {
            Random random = new Random(42);
            const string alphabet = "<>/=\"' abc-!?[]&;#x\n{}$";
            for (int i = 0; i < 3000; i++)
            {
                char[] chars = new char[random.Next(0, 60)];
                for (int j = 0; j < chars.Length; j++)
                {
                    chars[j] = alphabet[random.Next(alphabet.Length)];
                }
                string s = new string(chars);
                foreach (DocumentKind kind in Enum.GetValues<DocumentKind>())
                {
                    MarkupDocument doc = MarkupParser.Parse(s, kind);
                    Assert.All(doc.DescendantElements(), e => Assert.InRange(e.Span.End, 0, s.Length));
                }
            }
        }
    }
}
