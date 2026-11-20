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
        [InlineData(Greet + "<echo>{ @greet(who: 'K', 'Hi') }</echo>", DiagnosticCodes.NamedArgument, "must come before")]
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
            CompletionItem item = Service.GetCompletions(a, offset).First();
            Assert.Equal("name:", item.Label);
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
            Assert.Contains("known_folder(name)", hover.Markdown);
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
