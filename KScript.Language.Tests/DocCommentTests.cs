using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Syntax.Markup;

using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests
{
    /// <summary>/// doc comments (and &lt;!--- --&gt; in XML) are parsed into parts and shown in the editor.</summary>
    public class DocCommentTests
    {
        private static readonly LanguageService Service = new LanguageService(files: EmptyFileSystem.Instance);

        private const string Greet = """
            /// Greets someone politely.
            ///
            /// @param who The person to greet.
            /// @param greeting How to start, such as 'Hi'.
            /// @returns The greeting, ending with '!'.
            /// @example
            ///   @greet('Kyle')   // Hello, Kyle!
            fn greet(who, greeting = 'Hello') {
              return $greeting + ', ' + $who + '!'
            }
            """;

        private static (DocumentAnalysis, int) At(string textWithCaret, string path)
        {
            (string text, int offset) = WithCaret(textWithCaret);
            return (Service.Analyze(text, path), offset);
        }

        [Fact]
        public void ParsesTags()
        {
            DocComment doc = DocComment.Parse("Greets.\nMore text.\n\n@param who The person\n  on two lines.\n@param $greeting How to start.\n@returns A string.\n@example\n  @greet('a')\n  @greet('b')\n@deprecated Use @welcome.\n@see @welcome");
            Assert.Equal("Greets.\nMore text.", doc.Summary);
            Assert.Equal(new[] { "who", "greeting" }, doc.Params.Select(p => p.Name));
            Assert.Equal("The person\non two lines.", doc.ParamText("who"));
            Assert.Equal("A string.", doc.Returns);
            Assert.Equal("@greet('a')\n@greet('b')", Assert.Single(doc.Examples));
            Assert.Equal("Use @welcome.", doc.Deprecated);
            Assert.Equal("@welcome", Assert.Single(doc.SeeAlso));
            Assert.Null(DocComment.Parse("  "));
            Assert.Equal("Calls @greet() for you.", DocComment.Parse("Calls @greet() for you.").Summary);
        }

        [Fact]
        public void HoverShowsTheWholeDocComment()
        {
            (DocumentAnalysis a, int offset) = At(Greet + "\necho @gre‸et('Kyle')", "test.kscript");
            AssertClean(a);
            string hover = Service.GetHover(a, offset).Markdown;
            Assert.Contains("@greet(who, greeting = 'Hello')", hover);
            Assert.Contains("Greets someone politely.", hover);
            Assert.Contains("- `who`: The person to greet.", hover);
            Assert.Contains("**Returns** The greeting, ending with '!'.", hover);
            Assert.Contains("```kscript\n@greet('Kyle')   // Hello, Kyle!\n```", hover);
        }

        [Fact]
        public void HoverOnAParameterShowsItsDoc()
        {
            (DocumentAnalysis a, int offset) = At(Greet.Replace("+ $wh", "+ $w‸h"), "test.kscript");
            Assert.Contains("The person to greet.", Service.GetHover(a, offset).Markdown);
        }

        [Fact]
        public void SignatureHelpShowsParameterDocs()
        {
            (DocumentAnalysis a, int offset) = At(Greet + "\necho @greet('Kyle', ‸)", "test.kscript");
            SignatureHelpResult help = Service.GetSignatureHelp(a, offset);
            SignatureInfo sig = Assert.Single(help.Signatures);
            Assert.Contains("Greets someone politely.", sig.Documentation);
            Assert.DoesNotContain("**Parameters**", sig.Documentation);
            Assert.Equal("How to start, such as 'Hi'.", sig.Parameters[1].Documentation);
            Assert.Equal(1, help.ActiveParameter);
        }

        [Fact]
        public void XmlDocCommentsWork()
        {
            const string xml = """
                <kscript>
                  <!---
                    Doubles a number.
                    @param x The number.
                    @returns Twice x.
                  -->
                  <fn name="double" params="x" value="$x * 2" />
                  <echo>{ @dou‸ble(2) }</echo>
                </kscript>
                """;
            (DocumentAnalysis a, int offset) = At(xml, "test.ks.xml");
            AssertClean(a);
            string hover = Service.GetHover(a, offset).Markdown;
            Assert.Contains("Doubles a number.", hover);
            Assert.Contains("- `x`: The number.", hover);
            Assert.Contains("**Returns** Twice x.", hover);
        }

        [Fact]
        public void APlainCommentIsNotDocumentation()
        {
            (DocumentAnalysis a, int offset) = At("<kscript>\n<!-- just a note -->\n<fn name=\"f\" value=\"1\" />\n<echo>{ @‸f() }</echo>\n</kscript>", "test.ks.xml");
            Assert.DoesNotContain("just a note", Service.GetHover(a, offset).Markdown);
        }

        [Fact]
        public void WarnsAboutUnknownParamsAndDeprecatedCalls()
        {
            DocumentAnalysis a = Analyze("/// @param nope Not a parameter.\nfn f(x) => $x\n/// @deprecated Use @g.\nfn old() => 1\necho @old()", "test.kscript");
            Assert.Contains("nope", AssertSingle(a, "KS4007").Message);
            Assert.Contains("Use @g.", AssertSingle(a, "KS4008").Message);
        }

        [Fact]
        public void ExamplesCanStartOnTheTagLineAndSeeAlsoIsListed()
        {
            DocComment doc = DocComment.Parse("Adds.\n@example @add(1, 2)\n@see sum\n@see total");
            Assert.Equal("@add(1, 2)", doc.Examples.Single());
            string md = doc.ToMarkdown();
            Assert.Contains("**See also** sum, total", md);
        }

        [Fact]
        public void ADocumentedLambdaKeepsItsSignatureAndItsDocs()
        {
            DocumentAnalysis a = Analyze(Script("<!--- Doubles a number. -->\n<let name=\"twice\" value=\"fn($x) => $x * 2\" />"));
            Symbol twice = a.Symbols.Single(s => s.Name == "twice");
            Assert.Equal("fn($x)\n\nDoubles a number.", twice.Description);
        }

        [Fact]
        public void APlainCommentHasNoDocText()
        {
            MarkupDocument doc = MarkupParser.Parse("<kscript><!-- plain --></kscript>", DocumentKind.Script);
            MarkupComment comment = doc.RootElements.Single().Children.OfType<MarkupComment>().Single();
            Assert.False(comment.IsDoc);
            Assert.Null(comment.DocText);
        }
    }
}
