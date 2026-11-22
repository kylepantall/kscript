using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Syntax;

using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests
{
    /// <summary>Diagnostics, completion, hover and signature help for named arguments (name: value).</summary>
    public class NamedArgumentAnalysisTests
    {
        private static readonly LanguageService Service = new LanguageService(files: EmptyFileSystem.Instance);
        private const string IO = "<import package=\"KScript.IO\" />\n";
        private const string Greet = "<fn name=\"greet\" params=\"who, greeting = 'Hello'\" value=\"$greeting + $who\" />\n";

        private static (DocumentAnalysis, int) At(string textWithCaret)
        {
            (string text, int offset) = WithCaret(textWithCaret);
            return (Service.Analyze(text, "test.ks.xml"), offset);
        }

        [Theory]
        [InlineData(IO + "<echo>{ @known_folder(name: 'downloads') }</echo>")]
        [InlineData("<echo>{ @range(1, step: 2, end: 9) }</echo>")]
        [InlineData("<echo>{ 'abc'->pad_left(char: '*', width: 5) }</echo>")]
        [InlineData("<echo>{ 'a b' |> replace(new: '-', old: ' ') }</echo>")]
        [InlineData(Greet + "<echo>{ @greet(greeting: 'Hi', who: 'K') }</echo>")]
        [InlineData(Greet + "<echo>{ 'K' |> @greet(greeting: 'Hi') }</echo>")]
        public void ValidNamedArgumentsAreClean(string body) => AssertClean(Analyze(Script(body)));

        [Fact]
        public void UnknownNamesSuggestTheParameter()
        {
            DocumentAnalysis a = Analyze(Script(IO + "<echo>{ @known_folder(nme: 'downloads') }</echo>"));
            Diagnostic d = AssertSingle(a, DiagnosticCodes.UnknownParameter);
            Assert.Equal("nme", a.Source[d.Span]);
            Assert.Contains("Did you mean 'name'?", d.Message);
            Assert.Equal("name", d.Fixes.Single().Changes.Single().NewText);
        }

        [Fact]
        public void UnknownNamesOnFunctionsAndMethods()
        {
            AssertSingle(Analyze(Script(Greet + "<echo>{ @greet(who: 'K', greting: 'Hi') }</echo>")), DiagnosticCodes.UnknownParameter);
            AssertSingle(Analyze(Script("<echo>{ 'x'->pad_left(wdth: 3) }</echo>")), DiagnosticCodes.UnknownParameter);
        }

        [Theory]
        [InlineData(Greet + "<echo>{ @greet('K', who: 'J') }</echo>", DiagnosticCodes.NamedArgument, "already given as argument 1")]
        [InlineData(Greet + "<echo>{ 'K' |> @greet(who: 'J') }</echo>", DiagnosticCodes.NamedArgument, "piped value")]
        [InlineData(Greet + "<echo>{ @greet(who: 'K', who: 'J') }</echo>", DiagnosticCodes.NamedArgument, "given twice")]
        [InlineData(Greet + "<echo>{ @greet(greeting: 'Hi') }</echo>", DiagnosticCodes.ArgumentCount, "needs a value for 'who'")]
        [InlineData("<echo>{ @range(end: 3) }</echo>", DiagnosticCodes.ArgumentCount, "needs a value for 'start'")]
        public void BindingMistakesAreReported(string body, string code, string message)
        {
            Diagnostic d = AssertSingle(Analyze(Script(body)), code);
            Assert.Contains(message, d.Message);
        }

        [Fact]
        public void CompletionOffersParameterNames()
        {
            (DocumentAnalysis a, int offset) = At(Script(IO + "<echo>{ @known_folder(‸) }</echo>"));
            CompletionItem item = Service.GetCompletions(a, offset).First(c => c.Label == "name:");
            Assert.Equal("name: ", item.InsertText);
        }

        [Fact]
        public void CompletionLeavesOutParametersAlreadyGiven()
        {
            (DocumentAnalysis a, int offset) = At(Script(Greet + "<echo>{ @greet(greeting: 'Hi', ‸) }</echo>"));
            List<string> labels = Service.GetCompletions(a, offset).Select(c => c.Label).ToList();
            Assert.Contains("who:", labels);
            Assert.DoesNotContain("greeting:", labels);

            (a, offset) = At(Script("<echo>{ @range(1, ‸) }</echo>"));
            labels = Service.GetCompletions(a, offset).Select(c => c.Label).ToList();
            Assert.DoesNotContain("start:", labels);
            Assert.Contains("end:", labels);
            Assert.Contains("step:", labels);
        }

        [Fact]
        public void CompletionOfMethodParameterNames()
        {
            (DocumentAnalysis a, int offset) = At(Script("<echo>{ 'a'->pad_left(5, ‸) }</echo>"));
            List<string> labels = Service.GetCompletions(a, offset).Select(c => c.Label).ToList();
            Assert.Contains("char:", labels);
            Assert.DoesNotContain("width:", labels);
        }

        [Fact]
        public void HoverOnANamedArgumentShowsItsParameter()
        {
            (DocumentAnalysis a, int offset) = At(Script(IO + "<echo>{ @known_folder(na‸me: 'downloads') }</echo>"));
            HoverResult hover = Service.GetHover(a, offset);
            Assert.NotNull(hover);
            Assert.Contains("name: home | desktop", hover.Markdown);
            Assert.Equal("name", a.Source[hover.Span]);
        }

        [Fact]
        public void SignatureHelpFollowsTheNamedParameter()
        {
            (DocumentAnalysis a, int offset) = At(Script("<echo>{ @range(step: ‸) }</echo>"));
            SignatureHelpResult help = Service.GetSignatureHelp(a, offset);
            Assert.Equal(2, help.ActiveParameter);

            (a, offset) = At(Script(Greet + "<echo>{ @greet(greeting: 'Hi', who: ‸) }</echo>"));
            Assert.Equal(0, Service.GetSignatureHelp(a, offset).ActiveParameter);
        }
    }
}

namespace KScript.Language.Tests
{
    /// <summary>Enums, bare enum values and yes/no flags in the editor.</summary>
    public class EnumAndFlagAnalysisTests
    {
        private static readonly LanguageService Service = new LanguageService(files: EmptyFileSystem.Instance);
        private const string IO = "<import package=\"KScript.IO\" />\n";
        private const string Pack = "<enum name=\"Size\" values=\"small, medium, large\" />\n<fn name=\"pack\" params=\"size: Size, gift: bool = false\" value=\"$size\" />\n";
        private const string Sizes = "<enum name=\"Size\" values=\"small, medium, large\" />\n<fn name=\"pack_all\" params=\"sizes: Size[]\" value=\"$sizes\" />\n<fn name=\"plain\" params=\"items\" value=\"$items\" />\n";

        private static (DocumentAnalysis, int) At(string textWithCaret)
        {
            (string text, int offset) = WithCaret(textWithCaret);
            return (Service.Analyze(text, "test.ks.xml"), offset);
        }

        [Theory]
        [InlineData(Pack + "<echo>{ @pack(large) } { @pack(size: small, gift) } { @pack('medium', not gift) } { Size.small }</echo>")]
        [InlineData(IO + "<echo>{ @known_folder(downloads) } { @known_folder(name: home) } { @files('.', pattern: '*', recursive) }</echo>")]
        [InlineData(IO + "<echo>{ @join_path(base: '/a', ['b', 'c']) }</echo>")]
        [InlineData("<const name=\"max\" value=\"3\" /><echo>$max</echo>")]
        [InlineData(Sizes + "<echo>{ @pack_all([large, small]) } { @pack_all(sizes: [medium, 'small']) } { @pack_all([]) }</echo>")]
        public void ValidCallsAreClean(string body) => AssertClean(Analyze(Script(body)));

        [Theory]
        [InlineData(Pack + "<echo>{ @pack(huge) }</echo>", DiagnosticCodes.InvalidArgumentValue, "huge")]
        [InlineData(Pack + "<echo>{ @pack('huge') }</echo>", DiagnosticCodes.InvalidArgumentValue, "'huge'")]
        [InlineData(IO + "<echo>{ @known_folder(dowloads) }</echo>", DiagnosticCodes.InvalidArgumentValue, "dowloads")]
        [InlineData(Pack + "<echo>{ @pack(large, giift) }</echo>", DiagnosticCodes.UnknownParameter, "giift")]
        [InlineData(Pack + "<echo>{ @pack(size, large) }</echo>", DiagnosticCodes.NamedArgument, "size")]
        [InlineData("<fn name=\"f\" params=\"x: Nope\" value=\"$x\" />", DiagnosticCodes.InvalidAttributeValue, "Nope")]
        [InlineData(Sizes + "<echo>{ @pack_all([large, lrage]) }</echo>", DiagnosticCodes.InvalidArgumentValue, "lrage")]
        [InlineData(Sizes + "<echo>{ @pack_all(['huge']) }</echo>", DiagnosticCodes.InvalidArgumentValue, "'huge'")]
        [InlineData(Sizes + "<echo>{ @plain([large]) }</echo>", DiagnosticCodes.BareIdentifier, "large")]
        public void MistakesAreReported(string body, string code, string marked)
        {
            DocumentAnalysis a = Analyze(Script(body));
            Diagnostic d = a.Diagnostics.FirstOrDefault(x => x.Code == code);
            Assert.True(d != null, "Expected " + code + ":\n" + Describe(a));
            Assert.Equal(marked, a.Source[d.Span]);
        }

        [Fact]
        public void BadEnumValuesGetAQuickFix()
        {
            DocumentAnalysis a = Analyze(Script(IO + "<echo>{ @known_folder(dowloads) }</echo>"));
            Assert.Equal("downloads", AssertSingle(a, DiagnosticCodes.InvalidArgumentValue).Fixes.Single().Changes.Single().NewText);
            a = Analyze(Script(IO + "<echo>{ @known_folder('dowloads') }</echo>"));
            Assert.Equal("'downloads'", AssertSingle(a, DiagnosticCodes.InvalidArgumentValue).Fixes.Single().Changes.Single().NewText);
        }

        [Fact]
        public void CompletionSuggestsEnumValuesAndFlags()
        {
            (DocumentAnalysis a, int offset) = At(Script(IO + "<echo>{ @known_folder(‸) }</echo>"));
            List<string> labels = Service.GetCompletions(a, offset).Select(c => c.Label).ToList();
            Assert.Contains("downloads", labels);
            Assert.Contains("home", labels);

            (a, offset) = At(Script(IO + "<echo>{ @known_folder(name: ‸) }</echo>"));
            Assert.Contains("downloads", Service.GetCompletions(a, offset).Select(c => c.Label));

            (a, offset) = At(Script(Sizes + "<echo>{ @pack_all([large, ‸]) }</echo>"));
            Assert.Contains("medium", Service.GetCompletions(a, offset).Select(c => c.Label));

            (a, offset) = At(Script(Pack + "<echo>{ @pack(‸) }</echo>"));
            labels = Service.GetCompletions(a, offset).Select(c => c.Label).ToList();
            Assert.Contains("large", labels);
            Assert.Contains("gift", labels);
            Assert.Contains("not gift", labels);

            (a, offset) = At(Script(IO + "<echo>{ @files('.', ‸) }</echo>"));
            labels = Service.GetCompletions(a, offset).Select(c => c.Label).ToList();
            Assert.Contains("recursive", labels);
            Assert.Contains("not recursive", labels);
        }

        [Fact]
        public void ListsOfOtherParametersOfferNoEnumValues()
        {
            (DocumentAnalysis a, int offset) = At(Script(Sizes + "<echo>{ @plain([‸]) }</echo>"));
            Assert.DoesNotContain("medium", (Service.GetCompletions(a, offset) ?? new List<CompletionItem>()).Select(c => c.Label));

            (a, offset) = At(Script(Sizes + "<echo>{ @pack_all(other: [‸]) }</echo>"));
            Assert.DoesNotContain("medium", (Service.GetCompletions(a, offset) ?? new List<CompletionItem>()).Select(c => c.Label));
        }

        [Fact]
        public void HoverExplainsBareWords()
        {
            (DocumentAnalysis a, int offset) = At(Script(IO + "<echo>{ @known_folder(down‸loads) }</echo>"));
            Assert.Contains("a value of `name`", Service.GetHover(a, offset).Markdown);
            (a, offset) = At(Script(IO + "<echo>{ @files('.', not recur‸sive) }</echo>"));
            Assert.Contains("to no", Service.GetHover(a, offset).Markdown);
        }

        [Fact]
        public void EnumsCompleteTheirValuesAsFields()
        {
            (DocumentAnalysis a, int offset) = At(Script(Pack + "<echo>{ Size.‸ }</echo>"));
            Assert.Contains("medium", Service.GetCompletions(a, offset).Select(c => c.Label));
        }
    
        private static List<CompletionItem> CodeCompletions(string code, string trigger)
        {
            (string text, int offset) = WithCaret(code);
            return Service.GetCompletions(Service.Analyze(text, "test.kscript"), offset, trigger);
        }

        [Fact]
        public void OpeningAMethodCallSuggestsItsNamedArguments()
        {
            // Typing '(' after a method (or a command or function) opens the list with its parameters' names.
            List<CompletionItem> items = CodeCompletions("t = 'abc'\necho $t->replace(‸)", "(");
            Assert.Equal(new[] { "old:", "new:" }, items.Where(i => i.Kind == CompletionKind.Parameter).Select(i => i.Label));
            Assert.Contains(items, i => i.Label == "$t");

            List<CompletionItem> after = CodeCompletions("t = 'abc'\necho $t->replace('a',‸)", ",");
            CompletionItem next = Assert.Single(after, i => i.Kind == CompletionKind.Parameter);
            Assert.Equal("new:", next.Label);
            Assert.Equal(" new: ", next.InsertText);
            Assert.Equal("new", next.FilterText);
            Assert.All(after, i => Assert.StartsWith(" ", i.InsertText));

            // A space already typed after the comma is kept as it is.
            Assert.Equal("new: ", CodeCompletions("t = 'abc'\necho $t->replace('a', ‸)", ",").Single(i => i.Kind == CompletionKind.Parameter).InsertText);
        }

        [Fact]
        public void OpeningACallSuggestsEnumValuesAndFlags()
        {
            const string decl = "enum Size { small, large }\nfn pack(size: Size, wrap: bool = false) => 1\n";
            List<CompletionItem> items = CodeCompletions(decl + "echo @pack(‸)", "(");
            Assert.Equal(new[] { "small", "large" }, items.Where(i => i.Kind == CompletionKind.Value).Select(i => i.Label));
            CompletionItem size = items.Single(i => i.Label == "size:");
            Assert.True(size.SuggestAgain);

            List<CompletionItem> values = CodeCompletions(decl + "echo @pack(size: ‸)", null);
            Assert.Equal(new[] { "small", "large" }, values.Where(i => i.Kind == CompletionKind.Value).Select(i => i.Label));

            List<CompletionItem> flags = CodeCompletions(decl + "echo @pack(large,‸)", ",");
            Assert.Contains(flags, i => i.Label == "wrap" && i.Detail == "flag");
            Assert.True(flags.Single(i => i.Label == "wrap:").SuggestAgain);
            Assert.False(CodeCompletions("t = 'abc'\necho $t->replace(‸)", "(").Single(i => i.Label == "old:").SuggestAgain);
        }

        [Theory]
        [InlineData("x = (‸)", "(")]
        [InlineData("x = [1,‸]", ",")]
        [InlineData("x = { a: 1,‸ }", ",")]
        [InlineData("fn f() => 1\necho @f(‸)", "(")]
        [InlineData("fn f(a) => 1\necho @f(1,‸)", ",")]
        public void BracketsAndCommasOutsideACallWithNamesToOfferStayQuiet(string code, string trigger) =>
            Assert.Empty(CodeCompletions(code, trigger));

        [Fact]
        public void AskingWithoutATriggerStillListsEverything()
        {
            Assert.NotEmpty(CodeCompletions("x = (‸)", null));
            Assert.NotEmpty(CodeCompletions("x = [1, ‸]", "["));
        }
    }
}
