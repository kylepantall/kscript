using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Language.Text;

using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests
{
    public class FeatureTests
    {
        private static readonly LanguageService Service = new LanguageService(files: EmptyFileSystem.Instance);

        private static (DocumentAnalysis Analysis, int Offset) At(string textWithCaret, string path = "test.ks.xml")
        {
            (string text, int offset) = WithCaret(textWithCaret);
            return (Service.Analyze(text, path), offset);
        }

        private static List<CompletionItem> Complete(string textWithCaret, string path = "test.ks.xml")
        {
            (DocumentAnalysis a, int offset) = At(textWithCaret, path);
            return Service.GetCompletions(a, offset);
        }

        private static List<string> Labels(string textWithCaret, string path = "test.ks.xml") => Complete(textWithCaret, path).Select(c => c.Label).ToList();

        #region Completion

        [Fact]
        public void CompletesElementNames()
        {
            List<CompletionItem> items = Complete(Script("<‸"));
            Assert.Contains(items, i => i.Label == "echo" && i.Kind == CompletionKind.Element);
            Assert.Contains(items, i => i.Label == "let");
            Assert.Contains(items, i => i.Label == "for");
            Assert.DoesNotContain(items, i => i.Label == "def");     // retired
            Assert.DoesNotContain(items, i => i.Label == "case");    // only inside <match>
            CompletionItem forItem = items.Single(i => i.Label == "for");
            Assert.True(forItem.IsSnippet);
            Assert.Contains("each=", forItem.InsertText);
        }

        [Fact]
        public void CompletesContextualElements()
        {
            Assert.Contains("case", Labels(Script("<match value=\"1\"><‸</match>")));
            Assert.Contains("catch", Labels(Script("<try><‸</try>")));
            Assert.Contains("return", Labels(Script("<fn name=\"f\"><‸</fn>")));
            Assert.Contains("break", Labels(Script("<while test=\"true\"><‸</while>")));
            Assert.DoesNotContain("break", Labels(Script("<‸")));
            Assert.Contains("elif", Labels(Script("<if test=\"true\"></if><‸")));
        }

        [Fact]
        public void CompletesPartialElementNamesAndReplacesTheWord()
        {
            (DocumentAnalysis a, int offset) = At(Script("<ec‸"));
            CompletionItem echo = Service.GetCompletions(a, offset).Single(i => i.Label == "echo");
            Assert.Equal("ec", a.Source[echo.ReplaceSpan]);
        }

        [Fact]
        public void CompletesAttributes()
        {
            List<CompletionItem> items = Complete(Script("<for each=\"i\" ‸></for>"));
            Assert.Contains(items, i => i.Label == "in" && i.Kind == CompletionKind.Attribute);
            Assert.Contains(items, i => i.Label == "index");
            Assert.DoesNotContain(items, i => i.Label == "each"); // already present
        }

        [Fact]
        public void CompletesAttributeValues()
        {
            Assert.Equal(new[] { "markdown", "text" }, Labels(Script("<echo format=\"‸\">x</echo>")).OrderBy(x => x));
            Assert.Contains("yes", Labels(Script("<let name=\"x\" value=\"1\" const=\"‸\" />")));
            Assert.Contains("greet", Labels(Script("<fn name=\"greet\" /><call fn=\"‸\" />")));
        }

        [Fact]
        public void CompletesVariablesInScope()
        {
            List<string> labels = Labels(Script("<let name=\"total\" value=\"1\" /><for each=\"item\" in=\"0..2\"><echo>{ $‸ }</echo></for><let name=\"later\" value=\"1\" />"));
            Assert.Contains("total", labels);
            Assert.Contains("item", labels);
            Assert.Contains("args", labels);
            Assert.DoesNotContain("later", labels);
        }

        [Fact]
        public void CompletesBareVariablesInText()
        {
            Assert.Contains("name", Labels(Script("<let name=\"name\" value=\"'x'\" /><echo>Hi $na‸</echo>")));
        }

        [Fact]
        public void CompletesCommandsAndScriptFunctions()
        {
            List<CompletionItem> items = Complete(Script("<fn name=\"greet\" params=\"who\" value=\"$who\" /><echo>@‸</echo>"));
            Assert.Contains(items, i => i.Label == "join_path" && i.Kind == CompletionKind.Command);
            Assert.Contains(items, i => i.Label == "greet" && i.Kind == CompletionKind.Function);
            Assert.DoesNotContain(items, i => i.Label == "more_less");
        }

        [Theory]
        [InlineData("'abc'", "upper", "push")]
        [InlineData("[1, 2]", "push", "upper")]
        [InlineData("{ a: 1 }", "keys", "push")]
        [InlineData("1.5", "round", "push")]
        public void CompletesMethodsForTheReceiverType(string receiver, string expected, string unexpected)
        {
            List<string> labels = Labels(Script("<echo>{ " + receiver + "->‸ }</echo>"));
            Assert.Contains(expected, labels);
            Assert.DoesNotContain(unexpected, labels);
            Assert.Contains("type", labels); // 'any' methods are always offered
        }

        [Fact]
        public void CompletesMethodsForInferredVariables()
        {
            List<string> labels = Labels(Script("<let name=\"xs\" value=\"'a,b'->split(',')\" /><echo>{ $xs->‸ }</echo>"));
            Assert.Contains("join", labels);
            Assert.DoesNotContain("upper", labels);
        }

        [Fact]
        public void CompletesAfterPipe()
        {
            List<string> labels = Labels(Script("<fn name=\"twice\" params=\"x\" value=\"$x * 2\" /><echo>{ 'a' |> ‸ }</echo>"));
            Assert.Contains("upper", labels);
            Assert.Contains("twice", labels);
        }

        [Fact]
        public void CompletesFields()
        {
            Assert.Equal(new[] { "age", "name" }, Labels(Script("<let name=\"p\" value=\"{ name: 'J', age: 2 }\" /><echo>{ $p.‸ }</echo>")).OrderBy(x => x));
            Assert.Contains("query", Labels("<p>{ $request.‸ }</p>", "page.html"));
            Assert.Contains("message", Labels(Script("<try><catch as=\"e\"><echo>{ $e.‸ }</echo></catch></try>")));
        }

        [Fact]
        public void CompletesKeywordsInExpressions()
        {
            List<string> labels = Labels(Script("<if test=\"$args ‸\"></if>"));
            Assert.Contains("and", labels);
            Assert.Contains("gt", labels);
        }

        [Fact]
        public void CompletesClosingTags()
        {
            CompletionItem close = Assert.Single(Complete(Script("<for each=\"i\" in=\"0..1\"></‸")));
            Assert.Equal("for", close.Label);
            Assert.Equal("for>", close.InsertText);
        }

        [Fact]
        public void CompletesFrontMatterKeys()
        {
            Assert.Contains("layout", Labels("---\nla‸\n---\n# Hi", "page.md"));
        }

        [Fact]
        public void CompletesNothingInComments() => Assert.Empty(Complete(Script("<!-- $‸ -->")));

        #endregion

        #region Hover

        private static HoverResult HoverAt(string textWithCaret, string path = "test.ks.xml")
        {
            (DocumentAnalysis a, int offset) = At(textWithCaret, path);
            return Service.GetHover(a, offset);
        }

        [Theory]
        [InlineData("<ec‸ho>x</echo>", "<echo>")]
        [InlineData("<for ea‸ch=\"i\" in=\"0..1\"></for>", "each")]
        [InlineData("<echo>@join_p‸ath('a', 'b')</echo>", "join_path")]
        [InlineData("<echo>{ 'a'->up‸per() }</echo>", "upper")]
        [InlineData("<let name=\"total\" value=\"1\" /><echo>$to‸tal</echo>", "total")]
        [InlineData("<fn name=\"greet\" params=\"who\" value=\"$who\" /><echo>@gr‸eet('a')</echo>", "greet(who)")]
        [InlineData("<if test=\"1 g‸t 2\"></if>", "greater than")]
        [InlineData("<if test=\"1 ==‸= 2\"></if>", "strict")]
        [InlineData("<de‸f id=\"x\" />", "KScript 2")]
        public void HoversShowDocumentation(string body, string expected)
        {
            HoverResult hover = HoverAt(Script(body));
            Assert.NotNull(hover);
            Assert.Contains(expected, hover.Markdown, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void HoverShowsInferredTypes()
        {
            HoverResult hover = HoverAt(Script("<let name=\"xs\" value=\"[1, 2]\" /><echo>$x‸s</echo>"));
            Assert.Contains("list", hover.Markdown);
        }

        [Fact]
        public void HoverOnPlainTextIsNull() => Assert.Null(HoverAt(Script("<echo>hel‸lo</echo>")));

        #endregion

        #region Signature help

        [Fact]
        public void SignatureHelpTracksTheActiveParameter()
        {
            (DocumentAnalysis a, int offset) = At(Script("<echo>@join_path('a', ‸)</echo>"));
            SignatureHelpResult help = Service.GetSignatureHelp(a, offset);
            Assert.NotNull(help);
            Assert.Contains("join_path", help.Signatures[help.ActiveSignature].Label);
            Assert.Equal(1, help.ActiveParameter);
        }

        [Fact]
        public void SignatureHelpForMethodsAndFunctions()
        {
            (DocumentAnalysis a, int offset) = At(Script("<echo>{ 'a'->replace(‸) }</echo>"));
            Assert.Contains("replace(old, new)", Service.GetSignatureHelp(a, offset).Signatures[0].Label);

            (a, offset) = At(Script("<fn name=\"f\" params=\"x, y\" value=\"$x\" /><echo>@f(1, ‸)</echo>"));
            SignatureHelpResult help = Service.GetSignatureHelp(a, offset);
            Assert.Contains("f(x, y)", help.Signatures[0].Label);
            Assert.Equal(1, help.ActiveParameter);
        }

        [Fact]
        public void SignatureHelpOutsideCallsIsNull()
        {
            (DocumentAnalysis a, int offset) = At(Script("<echo>@join_path('a') ‸</echo>"));
            Assert.Null(Service.GetSignatureHelp(a, offset));
        }

        #endregion

        #region Navigation

        [Theory]
        [InlineData("<let name=\"total\" value=\"1\" /><echo>$to‸tal</echo>", "total")]
        [InlineData("<fn name=\"greet\" params=\"who\" value=\"$who\" /><echo>@gr‸eet('a')</echo>", "greet")]
        [InlineData("<fn name=\"greet\" params=\"who\" value=\"$w‸ho\" />", "who")]
        [InlineData("<fn name=\"greet\" /><call fn=\"gre‸et\" />", "greet")]
        [InlineData("<for each=\"item\" in=\"0..1\"><echo>$it‸em</echo></for>", "item")]
        [InlineData("<let name=\"f\" value=\"fn($n) => $‸n\" />", "n")]
        public void GoesToDefinition(string body, string declared)
        {
            (DocumentAnalysis a, int offset) = At(Script(body));
            SourceLocation location = Assert.Single(Service.GetDefinition(a, offset));
            Assert.Equal(declared, a.Source[location.Span]);
            Assert.True(location.Span.Start < offset);
        }

        [Fact]
        public void DefinitionOfBuiltInsIsEmpty()
        {
            (DocumentAnalysis a, int offset) = At(Script("<echo>@join_p‸ath('a')</echo>"));
            Assert.Empty(Service.GetDefinition(a, offset));
        }

        [Fact]
        public void FindsReferencesAndRenames()
        {
            (DocumentAnalysis a, int offset) = At(Script("<let name=\"n\" value=\"1\" /><set name=\"n\" value=\"$n + 1\" /><echo>$‸n { $n }</echo><for each=\"n2\" in=\"0..1\" />"));
            Assert.Equal(5, Service.GetReferences(a, offset, includeDeclaration: true).Count);
            Assert.Equal(4, Service.GetReferences(a, offset, includeDeclaration: false).Count);

            TextSpan? span = Service.PrepareRename(a, offset);
            Assert.NotNull(span);
            Assert.Equal("n", a.Source[span.Value]);

            RenameResult rename = Service.Rename(a, offset, "count");
            Assert.Null(rename.Error);
            string renamed = AnalyzerTests.Apply(a.Source.Text, rename.Changes);
            DocumentAnalysis after = Service.Analyze(renamed, "test.ks.xml");
            AssertClean(after);
            Assert.Equal(5, after.Symbols.Single(s => s.Name == "count").Let(s => after.ReferencesTo(s).Count()));
            Assert.Contains("n2", renamed);
        }

        [Theory]
        [InlineData("2x")]
        [InlineData("and")]
        [InlineData("total")]
        public void RenameRejectsBadNames(string name)
        {
            (DocumentAnalysis a, int offset) = At(Script("<let name=\"total\" value=\"1\" /><let name=\"n\" value=\"1\" /><echo>$‸n</echo>"));
            Assert.NotNull(Service.Rename(a, offset, name).Error);
        }

        [Fact]
        public void BuiltInsCannotBeRenamed()
        {
            (DocumentAnalysis a, int offset) = At(Script("<echo>$ar‸gs</echo>"));
            Assert.Null(Service.PrepareRename(a, offset));
        }

        [Fact]
        public void RenamesFunctionsEverywhere()
        {
            (DocumentAnalysis a, int offset) = At(Script("<fn name=\"gr‸eet\" params=\"x\" value=\"$x\" /><call fn=\"greet\" args=\"1\" /><echo>@greet(1) { 1 |> greet() }</echo>"));
            RenameResult rename = Service.Rename(a, offset, "hello");
            Assert.Equal(4, rename.Changes.Count);
            string renamed = AnalyzerTests.Apply(a.Source.Text, rename.Changes);
            Assert.DoesNotContain("greet", renamed);
            AssertClean(Service.Analyze(renamed, "test.ks.xml"));
        }

        [Fact]
        public void OutlineListsDeclarations()
        {
            DocumentAnalysis a = Service.Analyze(Script("<let name=\"a\" value=\"1\" const=\"yes\" /><fn name=\"f\" params=\"x\"><let name=\"inner\" value=\"1\" /></fn>"), "test.ks.xml");
            List<OutlineItem> outline = Service.GetOutline(a);
            List<OutlineItem> all = Flatten(outline).ToList();
            Assert.Contains(all, o => o.Name == "$a" && o.Kind == OutlineKind.Constant);
            OutlineItem f = all.Single(o => o.Name == "f");
            Assert.Equal(OutlineKind.Function, f.Kind);
            Assert.Contains(Flatten(f.Children), o => o.Name == "$inner");
        }

        private static IEnumerable<OutlineItem> Flatten(IEnumerable<OutlineItem> items) => items.SelectMany(i => new[] { i }.Concat(Flatten(i.Children)));

        [Fact]
        public void SemanticTokensAreOrderedAndClassified()
        {
            DocumentAnalysis a = Service.Analyze(Script("<let name=\"n\" value=\"1 + @length('ab')\" const=\"yes\" /><echo>{ $n->round() gt 2 }</echo>"), "test.ks.xml");
            List<SemanticToken> tokens = Service.GetSemanticTokens(a);
            Assert.Equal(tokens.OrderBy(t => t.Span.Start), tokens);
            for (int i = 1; i < tokens.Count; i++)
            {
                Assert.True(tokens[i].Span.Start >= tokens[i - 1].Span.End, "Tokens overlap: " + tokens[i - 1] + " / " + tokens[i]);
            }
            Assert.Contains(tokens, t => a.Source[t.Span] == "n" && t.Type == SemanticTokenType.Variable && t.Modifiers.HasFlag(SemanticTokenModifiers.Declaration) && t.Modifiers.HasFlag(SemanticTokenModifiers.Readonly));
            Assert.Contains(tokens, t => a.Source[t.Span].Contains("length") && t.Type == SemanticTokenType.Function);
            Assert.Contains(tokens, t => a.Source[t.Span] == "round" && t.Type == SemanticTokenType.Method);
            Assert.Contains(tokens, t => a.Source[t.Span] == "gt" && t.Type == SemanticTokenType.Keyword);
        }

        [Fact]
        public void QuickFixesAreOfferedForTheRange()
        {
            DocumentAnalysis a = Service.Analyze(Script("<ehco>x</ehco>"), "test.ks.xml");
            var fixes = Service.GetQuickFixes(a, a.Diagnostics[0].Span);
            Assert.Contains(fixes, f => f.Fix.Title.Contains("echo"));
            // Renaming the start tag also renames the end tag.
            string fixedText = AnalyzerTests.Apply(a.Source.Text, fixes[0].Fix.Changes);
            AssertClean(Service.Analyze(fixedText, "test.ks.xml"));
        }

        #endregion

        [Fact]
        public void FeaturesNeverThrowAnywhereInTheSamples()
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "samples");
            foreach (string file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                var service = new LanguageService(files: DiskFileSystem.Instance);
                DocumentAnalysis a = service.Analyze(text, file);
                for (int offset = 0; offset <= text.Length; offset++)
                {
                    service.GetCompletions(a, offset);
                    service.GetHover(a, offset);
                    service.GetSignatureHelp(a, offset);
                    service.GetDefinition(a, offset);
                    service.GetReferences(a, offset, true);
                    service.PrepareRename(a, offset);
                }
                service.GetOutline(a);
                service.GetSemanticTokens(a);
            }
        }

        [Fact]
        public void FeaturesNeverThrowOnBrokenInput()
        {
            string[] broken =
            {
                "<kscript><echo>{ $a->",
                "<kscript><for each=\"",
                "<kscript><let>x = [1, {",
                "<kscript><echo>@join_path('a', </echo>",
                "<kscript><<<>>></kscript>",
                "<kscript><if test=\"(((\"></if><elif",
                "---\ntitle: x",
                "<div class=\"{ $x",
                "",
            };
            foreach (string text in broken)
            {
                foreach (string path in new[] { "t.ks.xml", "t.html", "t.md" })
                {
                    DocumentAnalysis a = Service.Analyze(text, path);
                    for (int offset = 0; offset <= text.Length; offset++)
                    {
                        Service.GetCompletions(a, offset);
                        Service.GetHover(a, offset);
                        Service.GetSignatureHelp(a, offset);
                        Service.GetDefinition(a, offset);
                        Service.PrepareRename(a, offset);
                    }
                    Service.GetSemanticTokens(a);
                    Service.GetOutline(a);
                }
            }
        }
    }

    internal static class LetExtensions
    {
        public static TResult Let<T, TResult>(this T value, Func<T, TResult> f) => f(value);
    }
}
