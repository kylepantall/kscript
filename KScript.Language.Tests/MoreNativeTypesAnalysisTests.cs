using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Syntax;

using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests
{
    /// <summary>The editor side of the newer native types (url, ip, country, ...) and of a project's natives/ folder.</summary>
    public class MoreNativeTypesAnalysisTests
    {
        private static readonly LanguageService Service = new LanguageService();

        private static DocumentAnalysis Code(string code) => Analyze(code, "natives.kscript");

        [Theory]
        [InlineData("u: url = 'example.com'", "isn't a url")]
        [InlineData("u: url = https://", "isn't a url")]
        [InlineData("p: port = 70000", "isn't a port")]
        [InlineData("c: country = Atlantis", "isn't a country")]
        [InlineData("c: currency = 'ABC'", "isn't a currency")]
        [InlineData("z: timezone = Mars/Olympus", "isn't a timezone")]
        [InlineData("v: semver = '1.2'", "isn't a semver")]
        [InlineData("c: color = '#ggg'", "isn't a color")]
        [InlineData("d: duration = soon", "isn't a duration")]
        [InlineData("i: iban = 'GB00WEST12345698765432'", "isn't an iban")]
        [InlineData("ip: ip(version: 6) = 10.0.0.1", "IPv6")]
        [InlineData("type Market with [country] { one_of: [GB, IE] }\nm: Market = FR", "doesn't fit Market")]
        [InlineData("fn host(u: url) => $u->host()\necho @host('nope')", "isn't a url")]
        public void LiteralsThatDontFitTheirTypeAreReported(string code, string message)
        {
            Diagnostic d = AssertSingle(Code(code), DiagnosticCodes.InvalidTypedValue);
            Assert.Contains(message, d.Message);
        }

        [Theory]
        [InlineData("u: url = https://example.com/docs?page=2 // the docs\necho $u->host()")]
        [InlineData("u = https://example.com/docs\necho $u->path()\necho $u->contains('docs')")]
        [InlineData("echo https://example.com/a->host()")]
        [InlineData("d: domain = example.com\necho $d->tld()")]
        [InlineData("z: timezone = Europe/London\necho $z->offset()")]
        [InlineData("p: port = 8080\necho $p + 1")]
        [InlineData("c: color = red\necho $c->contrast(white)")]
        [InlineData("ip: ip = 10.0.0.1\necho $ip->in('10.0.0.0/8')")]
        [InlineData("c: country = gb\necho $c->currency()->symbol()")]
        [InlineData("enum Color { red, green }\nc: Color = green\necho $c")]
        [InlineData("type Duration { minutes: int }\nd = $Duration->from({ minutes: 1 })\necho $d.minutes")]
        [InlineData("type Site with [url] { validate: $value->host()->ends_with('example.com'), 'Use a company site' }\ns: Site = https://www.example.com\necho $s->host()")]
        public void ValuesThatFitAreClean(string code) => AssertClean(WithoutUnused(Code(code)));

        [Theory]
        [InlineData("u: url = https://example.com\necho $u->to_date()", "KS5005")]
        [InlineData("c: card = '4242424242424242'\necho $c->upper()", "KS5005")]
        [InlineData("u: url = https://example.com\necho $u->nope()", "KS5003")]
        public void MethodsAreCheckedForEachType(string code, string diagnostic)
        {
            Assert.Contains(Code(code).Diagnostics, d => d.Code == diagnostic);
        }

        [Fact]
        public void CompletionOffersEachTypesMethodsAndTheStringMethodsItKeeps()
        {
            (string text, int offset) = WithCaret("u: url = https://example.com\necho $u->‸");
            List<string> labels = Service.GetCompletions(Service.Analyze(text, "/tmp/natives.kscript"), offset).Select(i => i.Label).ToList();
            Assert.Contains("host", labels);
            Assert.Contains("with_query", labels);
            Assert.Contains("contains", labels);
            Assert.DoesNotContain("to_date", labels);
            Assert.DoesNotContain("version", labels);
        }

        [Fact]
        public void UrlsArePlainLiteralsNotComments()
        {
            List<Token> tokens = Lexer.Tokenize("https://example.com/docs?page=2, 'x' // note");
            Assert.Equal(TokenKind.Url, tokens[0].Kind);
            Assert.Equal("https://example.com/docs?page=2", tokens[0].Text);
            Assert.Equal(-1, Lexer.UrlEnd("ahttps://x.com", 1, 14));
            Assert.Equal(-1, Lexer.UrlEnd("https:///x", 0, 10));
            Assert.Equal(19, Lexer.UrlEnd("https://example.com.", 0, 20));
            Assert.True(Lexer.AfterUrlScheme("x = https://a.b", 10));
            Assert.False(Lexer.AfterUrlScheme("x = 1 // c", 6));
        }

        private static string TempProject(params (string Path, string Text)[] files)
        {
            string dir = Path.Combine(Path.GetTempPath(), "kscript-natives-lsp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            foreach ((string path, string text) in files)
            {
                string full = Path.Combine(dir, path);
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                File.WriteAllText(full, text);
            }
            return dir;
        }

        [Fact]
        public void TheProjectsNativeTypesAreKnownWithoutAnImport()
        {
            string dir = TempProject(
                ("kscript.json", "{ \"name\": \"p\" }"),
                ("natives/sku.kscript", "/// A stock code.\ntype Sku with [string] {\n  pattern: /^[A-Z]{3}-\\d{4}$/\n}\n"),
                ("natives/tier.kscript", "enum Tier { gold, silver }\n"));
            string main = Path.Combine(dir, "main.kscript");

            DocumentAnalysis ok = WithoutUnused(Service.Analyze("sku: Sku = ABC-1234\nt: Tier = gold\necho \"$sku $t\"", main));
            AssertClean(ok);

            DocumentAnalysis bad = Service.Analyze("sku: Sku = abc\necho $sku", main);
            Assert.Contains(bad.Diagnostics, d => d.Code == DiagnosticCodes.InvalidTypedValue && d.Message.Contains("doesn't fit Sku"));

            // A type of the file's own wins, and the natives file itself doesn't see itself twice.
            AssertClean(WithoutUnused(Service.Analyze("type Sku with [string] {}\ns: Sku = anything\necho $s", main)));
            AssertClean(WithoutUnused(Service.Analyze(File.ReadAllText(Path.Combine(dir, "natives", "sku.kscript")), Path.Combine(dir, "natives", "sku.kscript"))));

            // Go to definition and hover reach the natives file.
            (string text, int offset) = WithCaret("sku: Sk‸u = ABC-1234\necho $sku");
            DocumentAnalysis a = Service.Analyze(text, main);
            Assert.Contains("A stock code.", Service.GetHover(a, offset)?.Markdown ?? string.Empty);
        }

        [Fact]
        public void KscriptJsonCanNameTheNativesFolders()
        {
            string dir = TempProject(
                ("kscript.json", "{ \"natives\": [\"src/kinds\"] }"),
                ("src/kinds/level.kscript", "type Level with [int] { min: 1; max: 3 }\n"));
            DocumentAnalysis a = Service.Analyze("l: Level = 5\necho $l", Path.Combine(dir, "main.kscript"));
            Assert.Contains(a.Diagnostics, d => d.Code == DiagnosticCodes.InvalidTypedValue && d.Message.Contains("doesn't fit Level"));
        }
    }
}
