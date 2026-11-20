using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Syntax;
using KScript.Syntax.Markup;

using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests
{
    /// <summary>The editor side of email, password, handle, uuid and regex, refinement and joined types, and raw values.</summary>
    public class SemanticTypesAnalysisTests
    {
        private static readonly LanguageService Service = new LanguageService();

        private static DocumentAnalysis Code(string code) => Analyze(code, "types.kscript");

        [Theory]
        [InlineData("e: email = 'joan@'", "'joan@' isn't an email: there's nothing after the @.")]
        [InlineData("e: email = joan@example", "'joan@example' isn't an email: the domain needs a dot")]
        [InlineData("u: uuid(v: 4) = 3f2a8c1e-9b7d-8c2a-8e1f-0a9b8c7d6e5f", "This doesn't fit uuid(v: 4): it must be a version 4 UUID, but it is version 8.")]
        [InlineData("w: email(domains: ['example.com']) = joan@other.com", "it must be at example.com.")]
        [InlineData("p: password = 3", "This can't be a password: a password is text, in quotes.")]
        [InlineData("p: password(min: 12) = 'short'", "This doesn't fit password(min: 12): it must be at least 12 characters.")]
        [InlineData("h: handle = '@'", "'@' isn't a handle: it needs a name after the @.")]
        [InlineData("enum Color { red, green }\nc: Color = blu", "'blu' isn't a Color. Use one of: red, green.")]
        [InlineData("enum Color { red, green }\nc: Color = gren", "Did you mean green?")]
        [InlineData("type Age with [int] { min: 0; max: 150 }\na: Age = 200", "'200' doesn't fit Age: it must be at most 150.")]
        [InlineData("type Mail with [email] { max: 10 }\nm: Mail = 'joan@example.com'", "it must be at most 10 characters.")]
        [InlineData("type Mail with [email] {}\nm: Mail = 'joan@'", "'joan@' doesn't fit Mail: there's nothing after the @.")]
        [InlineData("type Slug with [string] { pattern: /^[a-z]+$/ }\ns: Slug = A1", "'A1' doesn't fit Slug: it must match /^[a-z]+$/.")]
        [InlineData("type Name with [handle] { not: [@admin] }\ntype Staff with [Name] { max: 5 }\nn: Staff = @admin", "'@admin' doesn't fit Staff: it is reserved ('@admin').")]
        [InlineData("type Size {\n  pattern: /^(?<w>\\d+)x(?<h>\\d+)$/\n  w: int\n  h: int\n}\ns: Size = big", "'big' doesn't fit Size: it must match /^(?<w>\\d+)x(?<h>\\d+)$/.")]
        [InlineData("x: string(min: 3) = ab", "This doesn't fit string(min: 3): it must be at least 3 characters.")]
        [InlineData("n: int(max: 3) = 5", "This doesn't fit int(max: 3): it must be at most 3.")]
        [InlineData("h: handle = @a\nok = $h == '@'", "'@' isn't a handle")]
        [InlineData("<kscript><let name=\"h\" type=\"handle\" value=\"'@'\" /></kscript>", "isn't a handle")]
        [InlineData("<kscript><let name=\"e\" type=\"email\">'joan@'</let></kscript>", "isn't an email")]
        public void LiteralsThatDontFitTheirTypeAreReported(string code, string message)
        {
            DocumentAnalysis a = code.StartsWith('<') ? Analyze(code) : Code(code);
            if (message == "'@' isn't a handle")
            {
                // Compared with quoted text, a handle isn't checked: only @name next to it is.
                Assert.DoesNotContain(a.Diagnostics, d => d.Code == DiagnosticCodes.InvalidTypedValue);
                return;
            }
            Diagnostic d = AssertSingle(a, DiagnosticCodes.InvalidTypedValue);
            Assert.Contains(message, d.Message);
        }

        [Theory]
        [InlineData("enum Color { red, green }\nc: Color = red\necho $c")]
        [InlineData("h: handle = @Joan\necho $h->name()")]
        [InlineData("type Size {\n  pattern: /^(?<w>\\d+)x(?<h>\\d+)$/\n  w: int\n  h: int\n}\ns: Size = 3x4\necho $s.w")]
        [InlineData("type Slug with [string] { pattern: /^[a-z]+$/; normalize: $value->lower() }\ns: Slug = A1\necho $s")]
        [InlineData("type Slug with [string] { pattern: /^[a-z]+$/ }\ns: Slug = 3\necho $s")]
        [InlineData("type Name with [handle] { one_of: [$x] }\nn: Name = @admin\necho $n")]
        [InlineData("type Tags with [string[]] { max: 2 }\nt: Tags = ['a']\necho $t")]
        [InlineData("type Mail with [email] {}\nm: Mail = 4\necho $m")]
        [InlineData("type Age with [int] { min: 0 }\na: Age = 'x'\necho $a")]
        [InlineData("type Age with [int] { min: 0 }\nfn f(a: Age = 5) => $a\necho @f()")]
        [InlineData("type Who with [handle] {}\nfn f(h: Who = @me) => $h\necho @f()")]
        [InlineData("fn who(h: handle) => $h->name()\necho @who(@Sam)\necho @who(h: @Ann)")]
        [InlineData("h: handle = @a\nok = $h == @a\nno = @b != $h\necho \"$ok $no\"")]
        [InlineData("u: uuid = 3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f\necho $u->version()")]
        [InlineData("e = joan@example.com\necho $e->domain()\necho $e->upper()")]
        [InlineData("r = /ab+/i\necho $r->test('abb')")]
        [InlineData("type User { name: string; fn hi(to: string = 'you') => 'hi ' + $to }\nu = $User->from({ name: 'x' })\necho $u->hi()\necho $u->hi('me')\necho $u->hi(to: 'me')")]
        [InlineData("type Slug with [string] { pattern: 'a+' }\ns: Slug = aa\necho $s")]
        [InlineData("type A { a: int }\ntype B with [A] { b: int }\nx = $B->from({ a: 1, b: 2 })\necho $x.a")]
        [InlineData("<kscript><let name=\"h\" type=\"handle\" value=\"@me\" /><echo>$h</echo></kscript>")]
        [InlineData("<kscript><let name=\"h\" type=\"handle\">@me</let><echo>$h</echo></kscript>")]
        [InlineData("<kscript><type name=\"Who\" with=\"[handle]\" max=\"5\" /><let name=\"h\" type=\"Who?\" value=\"@me\" /><echo>$h</echo></kscript>")]
        public void ValuesThatFitAreClean(string code) => AssertClean(WithoutUnused(code.StartsWith('<') ? Analyze(code) : Code(code)));

        [Theory]
        [InlineData("x = @nobody", "KS5001")]
        [InlineData("fn who(h: handle, n: int) => $h->name()\necho @who(@Sam, @nobody)", "KS5001")]
        [InlineData("type User { name: string; fn hi() => 'hi' }\nu = $User->from({ name: 'x' })\necho $u->nope()", "KS5003")]
        [InlineData("type User { name: string; fn hi() => 'hi' }\nu = $User->from({ name: 'x' })\necho $u->hi(1, 2)", "KS5004")]
        [InlineData("type User { name: string; fn hi(a: int) => 'hi' }\nu = $User->from({ name: 'x' })\necho $u->hi(b: 1)", null)]
        [InlineData("r = /ab+/i\necho $r->nope()", "KS5003")]
        [InlineData("type C with [Nope] {}", "KS3006")]
        public void MistakesAroundTypesAreReported(string code, string expected)
        {
            DocumentAnalysis a = WithoutUnused(Code(code));
            Assert.NotEmpty(a.Diagnostics);
            if (expected != null) Assert.Contains(a.Diagnostics, d => d.Code == expected);
        }

        [Fact]
        public void TypesAreColouredWhereverTheyAreNamed()
        {
            string code = "enum Unit { cm, in }\ntype Age with [int] { min: 0 }\ntype Size with [Age] { max: 9 }\nfn f(a: Age, u: uuid(v: 4)[]?) => $a\nx: Age = 3\nr = /a+/i\ne = joan@example.com";
            DocumentAnalysis a = Service.Analyze(code, "t.kscript");
            List<SemanticToken> tokens = Service.GetSemanticTokens(a);
            Assert.Contains(tokens, t => a.Source[t.Span] == "Age" && t.Type == SemanticTokenType.Type && t.Modifiers.HasFlag(SemanticTokenModifiers.Declaration));
            Assert.Contains(tokens, t => a.Source[t.Span] == "Unit" && t.Modifiers.HasFlag(SemanticTokenModifiers.Declaration));
            Assert.Contains(tokens, t => a.Source[t.Span] == "uuid" && t.Type == SemanticTokenType.Type);
            Assert.Contains(tokens, t => a.Source[t.Span] == "int" && t.Type == SemanticTokenType.Type);
            Assert.DoesNotContain(tokens, t => a.Source[t.Span] == "v" && t.Type == SemanticTokenType.Type);
            Assert.Contains(tokens, t => a.Source[t.Span] == "/a+/i" && t.Type == SemanticTokenType.Regexp);
            Assert.Contains(tokens, t => a.Source[t.Span] == "joan@example.com" && t.Type == SemanticTokenType.String);
        }

        [Fact]
        public void TypeNamesComplete()
        {
            (string text, int offset) = WithCaret("<kscript><type name=\"Age\" with=\"int\" /><let name=\"x\" type=\"‸\" /></kscript>");
            List<CompletionItem> items = Service.GetCompletions(Service.Analyze(text, "test.ks.xml"), offset);
            Assert.Contains(items, i => i.Label == "email");
            Assert.Contains(items, i => i.Label == "Age");
        }

        [Theory]
        [InlineData("type Username with [handle] {\n  max: 16\n  not: [@admin, @root]\n  validate: not $value->name()->starts_with('_'), 'A username cannot start with _'\n}")]
        [InlineData("type Age with [int] { min: 0; max: 150 }")]
        [InlineData("type Admin with [User, Auditable] {\n  level: int = 1\n  fn up(by: int = 1) => $level + $by\n}")]
        [InlineData("type Slug with [string] {\n  pattern: /^[a-z0-9]+(-[a-z0-9]+)*$/\n  normalize: $value->trim()->lower()\n}")]
        [InlineData("type Size {\n  pattern: /^(?<w>\\d+)x(?<h>\\d+)$/i\n  w: int\n  h: int\n}")]
        [InlineData("type Pair {\n  parse: { a: $value, b: $value }\n  a: string\n  b: string\n}")]
        [InlineData("id: uuid(v: 4) = 3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f\nwho: handle = @joan")]
        public void TheNewSyntaxConvertsBothWays(string code)
        {
            ConversionResult xml = ScriptConverter.Convert(code, "a.kscript");
            Assert.Empty(xml.Warnings);
            ConversionResult back = ScriptConverter.Convert(xml.Text, "a.ks.xml");
            Assert.Empty(back.Warnings);
            Assert.Equal(ScriptConverter.Convert(code, "a.kscript").Text, ScriptConverter.Convert(back.Text, "a.kscript").Text);
        }
    }
}
