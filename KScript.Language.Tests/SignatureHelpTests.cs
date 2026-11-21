using KScript.Language.Analysis;
using KScript.Language.Features;

using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests
{
    /// <summary>Signature help highlights the argument being written: in calls, methods, functions and .kscript statements.</summary>
    public class SignatureHelpTests
    {
        private static readonly LanguageService Service = new LanguageService(files: EmptyFileSystem.Instance);

        private static SignatureHelpResult Help(string code)
        {
            (string text, int offset) = WithCaret(code);
            return Service.GetSignatureHelp(Service.Analyze(text, "test.kscript"), offset);
        }

        private static string ActiveLabel(SignatureHelpResult help) =>
            help.ActiveParameter < help.Signatures[help.ActiveSignature].Parameters.Count ? help.Signatures[help.ActiveSignature].Parameters[help.ActiveParameter].Label : null;

        [Theory]
        [InlineData("$log->info(‸)", "message")]
        [InlineData("$log->info('a', ‸)", "fields?")]
        [InlineData("$log->log(level: ‸)", "level")]
        [InlineData("t = 'a'\necho $t->replace(‸)", "old")]
        [InlineData("t = 'a'\necho $t->replace(new: ‸)", "new")]
        [InlineData("echo @round(3.14, ‸)", "digits?")]
        [InlineData("fn greet(who, greeting = 'Hi') => $who\necho @greet(greeting: 'Yo', ‸)", "greeting = 'Hi'")]
        [InlineData("fn greet(who, greeting = 'Hi') => $who\necho @greet(‸)", "who")]
        public void CallsHighlightTheArgumentBeingWritten(string code, string active) => Assert.Equal(active, ActiveLabel(Help(code)));

        [Theory]
        [InlineData("log(‸)", "level?: trace|debug|info|warn|error|fatal")]
        [InlineData("log(level: warn, ‸)", "message?: text")]
        [InlineData("log(level: warn, message: 'a', ‸)", "category?: text")]
        [InlineData("log(category: ‸) \"a\"", "category?: text")]
        [InlineData("log(category: 'x', level: ‸)", "level?: trace|debug|info|warn|error|fatal")]
        [InlineData("fn f() {\n  log(fields: { a: 1, b: 2 }, ‸)\n}", "level?: trace|debug|info|warn|error|fatal")]
        [InlineData("wait(‸)", "ms?: any")]
        public void StatementsShowTheirAttributesAsParameters(string code, string active)
        {
            SignatureHelpResult help = Help(code);
            Assert.StartsWith("log(level?: trace|debug|info|warn|error|fatal, message?: text,", Help("log(‸)").Signatures.Single().Label);
            Assert.Equal(active, ActiveLabel(help));
            Assert.NotNull(help.Signatures.Single().Documentation);
        }

        [Fact]
        public void ACallInsideAStatementWins()
        {
            Assert.Equal("number", ActiveLabel(Help("log(fields: { a: @round(‸) })")));
        }

        [Theory]
        [InlineData("log(level: warn) \"hi ‸\"")]
        [InlineData("log(level: warn)‸")]
        [InlineData("if (‸) {\n}")]
        [InlineData("‸log(level: warn)")]
        [InlineData("nosuch(‸)")]
        public void NoHelpOutsideTheParentheses(string code) => Assert.Null(Help(code));

        [Fact]
        public void AllAttributesWrittenHighlightsNone()
        {
            SignatureHelpResult help = Help("wait(ms: 1, seconds: 2‸)");
            Assert.Equal("seconds?: any", ActiveLabel(help));
            SignatureHelpResult none = Help("wait(ms: 1, seconds: 2, ‸)");
            Assert.Null(ActiveLabel(none));
        }

        [Fact]
        public void XmlStatementsHaveNoStatementHelp()
        {
            (string text, int offset) = WithCaret("<kscript>\n<log level=\"warn\">a‸</log>\n</kscript>");
            Assert.Null(Service.GetSignatureHelp(Service.Analyze(text, "test.ks.xml"), offset));
        }
    }
}
