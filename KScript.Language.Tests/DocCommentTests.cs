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
            fn greet(who: string, greeting = 'Hello') {
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
            Assert.Contains("@greet(who: string, greeting = 'Hello')", hover);
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
                  <fn name="double" params="x: int" value="$x * 2" />
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
            DocumentAnalysis a = Analyze(Script("<!--- Doubles a number. -->\n<let name=\"twice\" value=\"fn(x) => $x * 2\" />"));
            Symbol twice = a.Symbols.Single(s => s.Name == "twice");
            Assert.Equal("fn(x)\n\nDoubles a number.", twice.Description);
        }

        [Fact]
        public void APlainCommentHasNoDocText()
        {
            MarkupDocument doc = MarkupParser.Parse("<kscript><!-- plain --></kscript>", DocumentKind.Script);
            MarkupComment comment = doc.RootElements.Single().Children.OfType<MarkupComment>().Single();
            Assert.False(comment.IsDoc);
            Assert.Null(comment.DocText);
        }
    
        [Fact]
        public void ArgAndArgumentAreOtherNamesForParam()
        {
            DocComment doc = DocComment.Parse("Adds.\n@arg a The first.\n@argument $b The second.\n@param c The third.\n@return The sum.");
            Assert.Equal(new[] { "a", "b", "c" }, doc.Params.Select(p => p.Name));
            Assert.Equal("The second.", doc.ParamText("b"));
            Assert.Equal("The sum.", doc.Returns);

            (DocumentAnalysis a, int offset) = At("/// Adds.\n/// @arg a The first.\n/// @arg b The second.\nfn add(a, b) => $a + $b\necho @add(1, ‸)", "test.kscript");
            Assert.Equal("The second.", Service.GetSignatureHelp(a, offset).Signatures.Single().Parameters[1].Documentation);
            DocumentAnalysis wrong = Analyze("/// @arg nope Not a parameter.\nfn f(x) => $x", "test.kscript");
            Assert.Contains("nope", AssertSingle(wrong, "KS4007").Message);
        }

        [Theory]
        [InlineData("import KScript.Http\nRoutes {\n  // @‸\n  Group(prefix: '/api') {}\n}")]
        [InlineData("echo 'ready' // @‸")]
        [InlineData("/* @‸ */\necho 'ready'")]
        [InlineData("/* @‸")]
        public void PlainCodeCommentsHaveNoCompletions(string source)
        {
            (DocumentAnalysis analysis, int offset) = At(source, "test.kscript");

            Assert.Empty(Service.GetCompletions(analysis, offset, "@"));
            Assert.Empty(Service.GetCompletions(analysis, offset));
        }

        [Fact]
        public void CommandsStillCompleteOnTheLineAfterAComment()
        {
            (DocumentAnalysis analysis, int offset) = At("import KScript.Http\n// a comment\necho @‸", "test.kscript");

            CompletionItem item = Assert.Single(Service.GetCompletions(analysis, offset, "@"), item => item.Label == "abort");
            Assert.Equal(CompletionKind.Command, item.Kind);
            Assert.Equal(offset, item.ReplaceSpan.Start);
            Assert.StartsWith("abort(", item.InsertText);
        }

        [Fact]
        public void TagsAreCompletedAfterTheAt()
        {
            (DocumentAnalysis a, int offset) = At("/// Greets.\n/// @param who The person.\n/// @re‸\nfn greet(who, greeting) => $greeting + $who", "test.kscript");
            List<CompletionItem> items = Service.GetCompletions(a, offset);
            string[] labels = items.Select(i => i.Label).ToArray();
            Assert.Contains("@returns", labels);
            Assert.Contains("@arg", labels);
            Assert.Contains("@llm", labels);
            Assert.Contains("@param greeting", labels);
            Assert.DoesNotContain("@param who", labels);
            Assert.DoesNotContain(items, i => i.Kind == CompletionKind.Command);
            CompletionItem returns = items.Single(i => i.Label == "@returns");
            Assert.Equal("returns ", returns.InsertText);
            Assert.Equal("returns", returns.FilterText);
            Assert.Equal(offset - 2, returns.ReplaceSpan.Start);
            Assert.False(string.IsNullOrEmpty(returns.Documentation));
        }

        [Fact]
        public void TagsWrittenOnceAreNotOfferedAgain()
        {
            (DocumentAnalysis a, int offset) = At("/// @returns Text.\n/// @deprecated Old.\n/// @‸\nfn f(x) => $x", "test.kscript");
            string[] labels = Service.GetCompletions(a, offset).Select(i => i.Label).ToArray();
            Assert.DoesNotContain("@returns", labels);
            Assert.DoesNotContain("@deprecated", labels);
            Assert.Contains("@example", labels);
            Assert.Contains("@param x", labels);

            // The comment's earlier lines count however far up it starts, even below a blank first line.
            (DocumentAnalysis b, int at) = At("\n/// @param who The person.\n///\n/// @‸\nfn f(who, b) => 1", "test.kscript");
            string[] more = Service.GetCompletions(b, at).Select(i => i.Label).ToArray();
            Assert.Contains("@param b", more);
            Assert.DoesNotContain("@param who", more);
        }

        [Fact]
        public void ParameterTagsAreLeftOutForAConstant()
        {
            (DocumentAnalysis a, int offset) = At("/// The limit.\n/// @‸\nconst limit = 3\necho $limit", "test.kscript");
            string[] labels = Service.GetCompletions(a, offset).Select(i => i.Label).ToArray();
            Assert.DoesNotContain("@param", labels);
            Assert.DoesNotContain("@returns", labels);
            Assert.Contains("@see", labels);
        }

        [Fact]
        public void ParameterNamesAreCompletedAfterParamOrArg()
        {
            (DocumentAnalysis a, int offset) = At("/// @param who The person.\n/// @arg gr‸\nfn greet(who, greeting: string = 'Hi') => $greeting + $who", "test.kscript");
            CompletionItem item = Assert.Single(Service.GetCompletions(a, offset));
            Assert.Equal("greeting", item.Label);
            Assert.Equal("greeting: string = 'Hi'", item.Detail);
            Assert.Equal(offset - 2, item.ReplaceSpan.Start);

            (DocumentAnalysis b, int at) = At("/// @param $‸\nfn greet(who) => $who", "test.kscript");
            Assert.Equal("who", Assert.Single(Service.GetCompletions(b, at)).Label);
        }

        [Fact]
        public void NothingElseIsSuggestedInADocComment()
        {
            (DocumentAnalysis a, int offset) = At("/// Calls ‸\nfn f(x) => $x", "test.kscript");
            Assert.Empty(Service.GetCompletions(a, offset));
        }

        [Fact]
        public void XmlDocCommentsCompleteTagsAndNames()
        {
            const string xml = """
                <kscript>
                  <!---
                    Doubles a number.
                    @‸
                  -->
                  <fn name="double" params="x, times" value="$x * $times" />
                </kscript>
                """;
            (DocumentAnalysis a, int offset) = At(xml, "test.ks.xml");
            string[] labels = Service.GetCompletions(a, offset).Select(i => i.Label).ToArray();
            Assert.Contains("@param x", labels);
            Assert.Contains("@param times", labels);
            Assert.Contains("@returns", labels);

            (DocumentAnalysis b, int at) = At(xml.Replace("@‸", "@param x The number.\n    @argument ‸"), "test.ks.xml");
            Assert.Equal("times", Assert.Single(Service.GetCompletions(b, at)).Label);

            (DocumentAnalysis plain, int p) = At("<kscript>\n<!-- @‸ -->\n<fn name=\"f\" value=\"1\" />\n</kscript>", "test.ks.xml");
            Assert.Empty(Service.GetCompletions(plain, p));
        }

        [Fact]
        public void HoverExplainsTagsAndTheParametersTheyName()
        {
            const string code = "/// @ar‸g who The person.\n/// @param nope Gone.\nfn greet(who: string) => $who";
            (DocumentAnalysis a, int offset) = At(code, "test.kscript");
            HoverResult tag = Service.GetHover(a, offset);
            Assert.Contains("**@arg**", tag.Markdown);
            Assert.Contains("The same as `@param`.", tag.Markdown);
            Assert.Equal(4, tag.Span.Start);
            Assert.Equal(8, tag.Span.End);

            (DocumentAnalysis b, int at) = At(code.Replace("@ar‸g who", "@arg w‸ho"), "test.kscript");
            Assert.Contains("**who: string**", Service.GetHover(b, at).Markdown);

            (DocumentAnalysis c, int nope) = At(code.Replace("‸", string.Empty).Replace("@param nope", "@param no‸pe"), "test.kscript");
            Assert.Contains("has no parameter called `nope`", Service.GetHover(c, nope).Markdown);

            (DocumentAnalysis d, int summary) = At("/// Gree‸ts.\nfn f() => 1", "test.kscript");
            Assert.Null(Service.GetHover(d, summary));
        }
    
        [Theory]
        [InlineData("editors/vscode/syntaxes/kscript-code.tmLanguage.json")]
        [InlineData("editors/vscode/syntaxes/kscript.tmLanguage.json")]
        public void TheEditorGrammarsKnowEveryTag(string relative)
        {
            string grammar = File.ReadAllText(Path.Combine(FormatterTests.RepoRoot(), relative));
            System.Text.RegularExpressions.Match list = System.Text.RegularExpressions.Regex.Match(grammar, @"\(@\(\?:(returns[^)]*)\)\)");
            Assert.True(list.Success, "The grammar's doc comment tag pattern was not found.");
            string[] names = list.Groups[1].Value.Split('|').Select(n => n == "returns?" ? "returns" : n).ToArray();
            foreach (DocTag tag in DocComment.KnownTags)
            {
                Assert.True(names.Contains(tag.Name) || (tag.Name == "return" && names.Contains("returns")), $"{relative} doesn't highlight @{tag.Name}.");
            }
        }
    }
}
