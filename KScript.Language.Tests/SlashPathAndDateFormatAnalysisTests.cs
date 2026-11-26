using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Syntax;

using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests
{
    /// <summary>The editor side of slash_path and date_format: the bare literal, checks, completion and route paths.</summary>
    public class SlashPathAndDateFormatAnalysisTests
    {
        private static readonly LanguageService Service = new LanguageService();

        private static DocumentAnalysis Code(string code, string file = "paths.kscript") => Analyze(code, file);

        [Theory]
        [InlineData("/docs/start", TokenKind.SlashPath, 11)]
        [InlineData("/people/{id}", TokenKind.SlashPath, 12)]
        [InlineData("/files/{path*}", TokenKind.SlashPath, 14)]
        [InlineData("/people/$id/edit", TokenKind.SlashPath, 16)]
        [InlineData("/docs/{ $s->lower() }/x", TokenKind.SlashPath, 23)]
        [InlineData("/a/b/c", TokenKind.SlashPath, 6)]
        [InlineData("/docs/start->name()", TokenKind.SlashPath, 11)]
        [InlineData("/blog/", TokenKind.Regex, 6)]
        [InlineData("/a/i", TokenKind.Regex, 4)]
        [InlineData("/^[a-z]+$/i", TokenKind.Regex, 11)]
        [InlineData("/old, redirect: \"/x\")", TokenKind.SlashPath, 4)]
        [InlineData("/old, to: '/sim')", TokenKind.SlashPath, 4)]
        [InlineData("/a, \"x/i\"]", TokenKind.SlashPath, 2)]
        [InlineData("/a, b/i", TokenKind.Regex, 7)]
        [InlineData("/a,b\"/i, \"x\"", TokenKind.Regex, 7)]
        [InlineData("/I can't/i, 'x')", TokenKind.Regex, 10)]
        [InlineData("/a b/i)", TokenKind.Regex, 6)]
        public void TheLexerTellsSlashPathsFromRegexes(string text, TokenKind kind, int length)
        {
            Token first = Lexer.Tokenize(text)[0];
            Assert.Equal(kind, first.Kind);
            Assert.Equal(length, first.Span.Length);
        }

        [Fact]
        public void DivisionAndCommentsStayWhatTheyWere()
        {
            Assert.DoesNotContain(Lexer.Tokenize("$a /b"), t => t.Kind == TokenKind.SlashPath);
            Assert.Equal(6, Lexer.Tokenize("/a/b/c // a comment")[0].Span.Length);
        }

        [Theory]
        [InlineData("p: slash_path = '/a/../..'", "goes above /")]
        [InlineData("f: date_format = YYYY-MM-dd", "years are y")]
        [InlineData("f: date_format = 'hh:mm a'", "AM/PM is tt")]
        [InlineData("echo @now()->format(YYYY-MM-dd)", "years are y")]
        [InlineData("b: color = '#ff8800'\necho $b->contrast_ratio(light-blue)", "isn't a color")]
        public void LiteralsThatDontFitAreReported(string code, string message)
        {
            Diagnostic d = AssertSingle(Code(code), DiagnosticCodes.InvalidTypedValue);
            Assert.Contains(message, d.Message);
        }

        [Theory]
        [InlineData("p: slash_path = /My Apps/Notes\necho $p.parent.name")]
        [InlineData("p = /docs/start\necho $p->is_within(/docs)")]
        [InlineData("id = 3\necho /people/$id/edit")]
        [InlineData("p = /docs + 'start'\necho $p.depth")]
        [InlineData("for part in /a/b {\n  echo $part.name\n}")]
        [InlineData("f: date_format = dd-MM-yyyy\necho $f.example")]
        [InlineData("echo @now()->format(dd-MM-yyyy)")]
        [InlineData("echo '22-11-2026'->to_date(dd-MM-yyyy)")]
        [InlineData("fn f(x: semver) => $x.major\necho @f(1.4.2)")]
        [InlineData("v: semver = 1.4.2\necho $v->compare(1.10.0-beta.1)")]
        public void ValidUsesAreClean(string code) => AssertClean(Code(code));

        [Fact]
        public void RoutePathsNeedNoQuotes()
        {
            const string routes = "import KScript.Http\n\nRoutes {\n  Get(path: /, view: \"home\")\n  Get(path: /people/{id}, view: \"person\", where: \"id: int\")\n  Get(path: \"/quoted\", view: \"home\")\n  Group(prefix: /admin) {\n    Resource(path: /users, to: users)\n  }\n  Get(path: /team, redirect: \"/people\", status: 301)\n}\n";
            DocumentAnalysis a = Code(routes, "routes.kscript");
            Assert.DoesNotContain(a.Diagnostics, d => d.Code == DiagnosticCodes.QuoteText || d.Severity == DiagnosticSeverity.Error && d.Code.StartsWith("KS1"));
        }

        [Fact]
        public void ABarePathBeforeAQuotedAddressIsNotARegex()
        {
            // "/x" (and "/mix", "/sim") after a bare path once read as the end of a regex with flags: /old, redirect: "/x.
            const string routes = "import KScript.Http\n\nRoutes {\n  Get(path: /old, redirect: \"/x\", status: 301)\n  Get(path: /team, redirect: \"/mix\")\n}\n";
            DocumentAnalysis a = Code(routes, "routes.kscript");
            Assert.DoesNotContain(a.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            Assert.Equal(routes, KScript.Language.Formatting.Formatter.Format(routes, "routes.kscript").Text);

            List<Token> tokens = Lexer.Tokenize("Get(path: /old, redirect: \"/x\", status: 301)");
            Assert.Contains(tokens, t => t.Kind == TokenKind.SlashPath && t.Span.Length == 4);
            Assert.DoesNotContain(tokens, t => t.Kind == TokenKind.Regex);
            Assert.Contains(tokens, t => t.Kind == TokenKind.String);
        }

        [Fact]
        public void TheFormatterKeepsRouteParametersTogether()
        {
            const string code = "import KScript.Http\n\nRoutes {\n  Get(path: /files/{path*}, view: \"file\")\n}\n";
            Assert.Equal(code, KScript.Language.Formatting.Formatter.Format(code, "routes.kscript").Text);
        }

        [Theory]
        [InlineData("f: date_format = dd-M", "MM")]
        [InlineData("echo @now()->format('yyyy-", "dd")]
        [InlineData("echo '1'->to_date(format: 'd", "dddd")]
        [InlineData("f: date_format = '", "d MMMM yyyy")]
        public void DateFormatsOfferTheirLetters(string text, string label)
        {
            List<CompletionItem> items = Service.GetCompletions(Service.Analyze(text, "dates.kscript"), text.Length);
            CompletionItem item = Assert.Single(items, i => i.Label == label);
            Assert.False(string.IsNullOrEmpty(item.Detail));
        }

        [Fact]
        public void PickingALetterReplacesTheRunBeingTyped()
        {
            const string text = "f: date_format = dd-MM";
            CompletionItem item = Assert.Single(Service.GetCompletions(Service.Analyze(text, "dates.kscript"), text.Length), i => i.Label == "MMMM");
            Assert.Equal(text.Length - 2, item.ReplaceSpan.Start);
        }
    }
}
